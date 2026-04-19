using Flurl.Http;
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

namespace Logging.Net.Serilog
{
	/// <summary>
	/// A Serilog sink that outputs the logs to BetterStack logging (formerly Logtail).
	/// </summary>
	/// <remarks>
	/// Events are buffered in an in-memory queue and uploaded asynchronously in batches of up to 500.
	/// The queue has a hard cap of 5000 pending events; if the producer outpaces the uploader, the oldest
	/// events are dropped to make room and a notice is written to <see cref="Console"/>. Polly retries
	/// transient HTTP failures (408/502/503/504) up to 5 times with exponential backoff; events are
	/// dropped after retries are exhausted. Because this library is intended to run inside containers
	/// where stdout is captured by the orchestrator, <see cref="Console"/> is used as the diagnostic
	/// channel for sink-internal errors rather than a Serilog self-log.
	/// </remarks>
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
			var token = this.cancellationTokenSource.Token;
			while (true)
			{
				try
				{
					await Task.Delay(TimeSpan.FromMilliseconds(200), token);
				}
				catch (OperationCanceledException)
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
				var r = await this.client.Request().PostAsync(content);
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

		internal static Dictionary<string, object> RenderProperties(IReadOnlyDictionary<string, LogEventPropertyValue> properties)
		{
			// BetterStack/Logtail rejects property names that contain dots (it expects nested JSON
			// objects instead). Render each property value recursively, then split any dotted keys
			// into nested dictionaries so the uploaded payload has a proper object structure.
			var root = new Dictionary<string, object>();
			foreach (var property in properties)
				MergeNested(root, property.Key, RenderValue(property.Value));
			return root;
		}

		private static object RenderValue(LogEventPropertyValue value)
		{
			switch (value)
			{
				case ScalarValue scv:
					return scv.Value;
				case StructureValue stv:
				{
					var obj = new Dictionary<string, object>();
					foreach (var p in stv.Properties)
						MergeNested(obj, p.Name, RenderValue(p.Value));
					return obj;
				}
				case SequenceValue sqv:
					return sqv.Elements.Select(RenderValue).ToList();
				case DictionaryValue dv:
				{
					var obj = new Dictionary<string, object>();
					foreach (var kvp in dv.Elements)
						MergeNested(obj, kvp.Key.Value?.ToString() ?? "", RenderValue(kvp.Value));
					return obj;
				}
				default:
					return value.ToString();
			}
		}

		private static void MergeNested(Dictionary<string, object> target, string dottedKey, object value)
		{
			// Last-write-wins on shape conflicts: if a path segment already exists as a non-dict
			// scalar, or the leaf already holds a value of a different shape than the incoming one,
			// the earlier value is overwritten. Serilog's property model doesn't produce such
			// conflicts in practice, but we don't attempt to preserve both sides if it ever does.
			var cursor = target;
			string leaf;
			if (dottedKey.IndexOf('.') < 0)
			{
				// Fast path: most keys have no dots, skip the Split allocation.
				leaf = dottedKey;
			}
			else
			{
				var parts = dottedKey.Split('.');
				for (int i = 0; i < parts.Length - 1; i++)
				{
					if (!cursor.TryGetValue(parts[i], out var next) || !(next is Dictionary<string, object> nextDict))
					{
						nextDict = new Dictionary<string, object>();
						cursor[parts[i]] = nextDict;
					}
					cursor = nextDict;
				}
				leaf = parts[parts.Length - 1];
			}
			if (value is Dictionary<string, object> incoming
				&& cursor.TryGetValue(leaf, out var existing)
				&& existing is Dictionary<string, object> existingDict)
			{
				foreach (var kvp in incoming)
					MergeNested(existingDict, kvp.Key, kvp.Value);
			}
			else
			{
				cursor[leaf] = value;
			}
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

	public static class SerilogLogtailSinkExtensions
	{
		public static LoggerConfiguration LogtailSink(
				  this LoggerSinkConfiguration loggerConfiguration, string logtailToken)
		{
			return loggerConfiguration.Sink(new BetterStackSink(logtailToken));
		}
	}
}
