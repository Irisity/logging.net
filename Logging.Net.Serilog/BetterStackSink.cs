using Flurl.Http;
using Log.Serilog;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Polly;
using Polly.Retry;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Log.Serilog
{
	/// <summary>
	/// A Serilog sink that outputs the logs to BetterStack logging.
	/// </summary>
	public class BetterStackSink : ILogEventSink, IDisposable
	{
		private readonly static JsonSerializerSettings settings = new JsonSerializerSettings
		{
			ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
			ContractResolver = new DefaultContractResolver
			{
				NamingStrategy = new CamelCaseNamingStrategy()
			},
			Converters = new List<JsonConverter>
			{
				new Newtonsoft.Json.Converters.StringEnumConverter(),
				new ToStringJsonConverter(typeof(System.Reflection.MemberInfo)),
				new ToStringJsonConverter(typeof(System.Reflection.Assembly)),
				new ToStringJsonConverter(typeof(System.Reflection.Module)),
			},
			Error = (sender, args) =>
			{
				args.ErrorContext.Handled = true;   // Ignore Properties that throws Exceptions
			},
		};
		private readonly static AsyncRetryPolicy retryPolicy = Policy
			.Handle<FlurlHttpException>(IsTransientError)
			.WaitAndRetryAsync(
				5,
				retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
				(exception, timeSpan, retryAttempt, context) =>
				{
					Console.WriteLine($"Try {retryAttempt}/5 failed: {exception}");
				});

		private readonly string logtailToken;
		private readonly ConcurrentQueue<LogEvent> events = new ConcurrentQueue<LogEvent>();
		private readonly IFlurlClient client;
		private readonly Task sendTask;
		private readonly List<LogEvent> dequeuedEvents = new List<LogEvent>();
		private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		private bool disposedValue;

		public BetterStackSink(string logtailToken)
		{
			this.logtailToken = logtailToken;

			this.client = new FlurlClient("https://in.logtail.com")
				.WithOAuthBearerToken(this.logtailToken)
				.WithTimeout(TimeSpan.FromSeconds(20));

			this.sendTask = Task.Run(SendTask);
		}

		private async Task SendTask()
		{
			while (true)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(200));

				if (this.cancellationTokenSource.Token.IsCancellationRequested)
				{
					break;
				}

				int discarded = 0;
				while (this.events.Count > 5000 && this.events.TryDequeue(out var _)) 
				{
					++discarded;
				}
				if (discarded != 0)
				{
					Console.WriteLine($"Discarded {discarded} logs");
				}

				while (this.dequeuedEvents.Count < 500 && this.events.TryDequeue(out var logEvent))
					this.dequeuedEvents.Add(logEvent);

				if (this.dequeuedEvents.Any())
				{
					try
					{
						var content = this.serialize(this.dequeuedEvents);
						await this.send(content);
					}
					catch (Exception e)
					{
						Console.WriteLine($"Failed uploading {this.dequeuedEvents.Count} logs: {e}");
					}
					this.dequeuedEvents.Clear();
				}
			}
		}

		public void Emit(LogEvent logEvent)
		{
			this.events.Enqueue(logEvent);
		}

		private Task send(HttpContent content)
		{
			return retryPolicy.ExecuteAsync(async () =>
			{
				Console.WriteLine($"Sending {this.dequeuedEvents.Count} logs");
				var r = await this.client.Request().PostAsync(content);
				Console.WriteLine($"Logs sent: {r.StatusCode}");
				if (r != null)
					r.Dispose();
			});
		}

		private HttpContent serialize(IEnumerable<LogEvent> logs)
		{
			var logtailEvents = logs.Select(log =>
			{
				var dict = new Dictionary<string, object>
				{
					{ "dt", log.Timestamp },
					{ "message", log.MessageTemplate.Text },
					{ "level", LevelToString(log.Level) }
				};
				if (log.Exception != null)
					dict.Add("exception", log.Exception);
				dict.Add("props", RenderProperties(log.Properties));
				return dict;
			}).ToList();
			var payload = JsonConvert.SerializeObject(logtailEvents, settings);
			var content = new ByteArrayContent(Encoding.UTF8.GetBytes(payload));
			content.Headers.Add("Content-Type", "application/json");
			return content;
		}

		private string LevelToString(LogEventLevel level)
		{
			switch (level)
			{
				case LogEventLevel.Fatal:
				case LogEventLevel.Error:
					return "ERR";
				case LogEventLevel.Debug:
					return "DBG";
				case LogEventLevel.Verbose:
					return "VRB";
				case LogEventLevel.Warning:
				case LogEventLevel.Information:
				default:
					return "INF";
			}
		}

		private Dictionary<string, object> RenderProperties(IReadOnlyDictionary<string, LogEventPropertyValue> properties)
		{
			return properties.SelectMany(ExpandProperties).ToDictionary(p => p.Item1, p => p.Item2);
		}

		private IEnumerable<(string, object)> ExpandProperties(KeyValuePair<string, LogEventPropertyValue> property)
		{
			if (property.Value is ScalarValue scv)
			{
				return new[] { (property.Key, scv.Value) };
			}
			else if (property.Value is StructureValue stv)
			{
				return stv.Properties.SelectMany(p => ExpandProperties(new KeyValuePair<string, LogEventPropertyValue>($"{property.Key}.{p.Name}", p.Value)));
			}
			
			return new[] { (property.Key, (object)property.Value.ToString()) };
		}

		/// <summary>
		/// JSON converter that just calls ToString on the target value (when non-null).
		/// This is configured as the converter for types that will otherwise spew a lot of irrelevant JSON
		/// into logs.
		/// </summary>
		internal sealed class ToStringJsonConverter : JsonConverter
		{
			private readonly Type _type;

			/// <inheritdoc />
			public override bool CanRead => false;

			public ToStringJsonConverter(Type type) =>
				_type = type;

			/// <inheritdoc />
			public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
			{
				if (value is null)
				{
					writer.WriteNull();
				}
				else
				{
					writer.WriteValue(value.ToString());
				}
			}

			/// <inheritdoc />
			public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) =>
				throw new NotSupportedException("Only serialization is supported");

			/// <inheritdoc />
			public override bool CanConvert(Type objectType) =>
				this._type.IsAssignableFrom(objectType);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					this.cancellationTokenSource.Cancel();
					this.sendTask.Wait();
				}

				disposedValue = true;
			}
		}

		public void Dispose()
		{
			// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		private static bool IsTransientError(FlurlHttpException exception)
		{
			int[] httpStatusCodesWorthRetrying =
			{
				(int)HttpStatusCode.RequestTimeout, // 408
				(int)HttpStatusCode.BadGateway, // 502
				(int)HttpStatusCode.ServiceUnavailable, // 503
				(int)HttpStatusCode.GatewayTimeout // 504
			};

			return exception.StatusCode.HasValue && httpStatusCodesWorthRetrying.Contains(exception.StatusCode.Value);
		}
	}
}

public static class SerilogLogtailSinkExtensions
{
	public static LoggerConfiguration LogtailSink(
			  this LoggerSinkConfiguration loggerConfiguration, string logtailToken)
	{
		return loggerConfiguration.Sink(new BetterStackSink(logtailToken));
	}
}
