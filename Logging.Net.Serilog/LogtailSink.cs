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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Log.Serilog
{
	public class LogtailSink : ILogEventSink, IDisposable
	{
		private readonly static JsonSerializerSettings settings = new JsonSerializerSettings
		{
			ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
			ContractResolver = new DefaultContractResolver
			{
				NamingStrategy = new CamelCaseNamingStrategy()
			}
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
		private readonly IFlurlRequest url;
		private readonly Task sendTask;
		private readonly List<LogEvent> dequeuedEvents = new List<LogEvent>();
		private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		private bool disposedValue;

		public LogtailSink(string logtailToken)
		{
			this.logtailToken = logtailToken;

			settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
			settings.Converters.Add(new ToStringJsonConverter(typeof(System.Reflection.MemberInfo)));
			settings.Converters.Add(new ToStringJsonConverter(typeof(System.Reflection.Assembly)));
			settings.Converters.Add(new ToStringJsonConverter(typeof(System.Reflection.Module)));
			settings.Error = (sender, args) =>
			{
				args.ErrorContext.Handled = true;   // Ignore Properties that throws Exceptions
			};

			url = "https://in.logtail.com"
				.WithOAuthBearerToken(this.logtailToken)
				.WithTimeout(TimeSpan.FromSeconds(10));

			sendTask = Task.Run(SendTask);
		}

		private async Task SendTask()
		{
			bool runAgain = true;
			do
			{
				await Task.Delay(TimeSpan.FromMilliseconds(200));

				runAgain = !cancellationTokenSource.Token.IsCancellationRequested;

				while (dequeuedEvents.Count < 1000 && events.TryDequeue(out var logEvent))
					dequeuedEvents.Add(logEvent);

				if (dequeuedEvents.Any())
				{
					try
					{
						var content = serialize(dequeuedEvents);
						await send(content);
					}
					catch (Exception e)
					{
						Console.WriteLine($"Failed uploading {dequeuedEvents.Count} logs: {e}");
					}
					dequeuedEvents.Clear();
				}
			} while (runAgain);
		}

		public void Emit(LogEvent logEvent)
		{
			events.Enqueue(logEvent);
		}

		private async Task send(HttpContent content)
		{
			await retryPolicy.ExecuteAsync(() => url.PostAsync(content));
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
					return "ERROR";
				case LogEventLevel.Warning:
				case LogEventLevel.Information:
				case LogEventLevel.Debug:
				case LogEventLevel.Verbose:
				default:
					return "INF";
			}
		}

		private Dictionary<string, object> RenderProperties(IReadOnlyDictionary<string, LogEventPropertyValue> properties)
		{
			return properties.ToDictionary(p => p.Key, p =>
			{
				if (p.Value is ScalarValue sv)
				{
					return sv.Value;
				}
				return p.Value.ToString();
			});
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
				_type.IsAssignableFrom(objectType);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					cancellationTokenSource.Cancel();
					sendTask.Wait();
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
		return loggerConfiguration.Sink(new LogtailSink(logtailToken));
	}
}
