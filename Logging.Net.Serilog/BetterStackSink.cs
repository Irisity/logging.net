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
using System.IO;
using System.IO.Compression;
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
	/// The queue has a hard cap of 5000 pending events, enforced as events are queued rather than by the
	/// uploader, which can be parked in a minutes-long retry ladder while the producer keeps going; if the
	/// producer outpaces the uploader the oldest events are dropped and a notice is written to
	/// <see cref="Console"/>. Polly retries transient failures — timeouts, connection errors, 408, 429 and
	/// 5xx — up to 5 times with exponential backoff; events are dropped after retries are exhausted.
	/// Disposing the sink (which is what <c>Logging.Flush()</c> and a re-<c>Init()</c> do) drains whatever
	/// is still queued before returning, bounded by <see cref="DrainTimeout"/> so shutdown cannot hang.
	/// Because this library is intended to run inside containers where stdout is captured by the
	/// orchestrator, <see cref="Console"/> is used as the diagnostic channel for sink-internal errors
	/// rather than a Serilog self-log.
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
			.Handle<FlurlHttpException>(e => IsTransientError(e.StatusCode))
			.WaitAndRetryAsync(
				5,
				retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
				(exception, timeSpan, retryAttempt, context) =>
				{
					SafeWriteLine($"Try {retryAttempt}/5 failed: {exception}");
				});

		private const int QueueCapacity = 5000;
		private const int BatchSize = 500;
		private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

		/// <summary>How long the final drain on Dispose is allowed to take before the rest is abandoned.</summary>
		internal static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

		private readonly string logtailToken;
		private readonly ConcurrentQueue<LogEvent> events = new ConcurrentQueue<LogEvent>();
		private readonly IFlurlClient client;
		private readonly Task sendTask;
		private readonly List<LogEvent> dequeuedEvents = new List<LogEvent>();
		// Signals the uploader to stop polling and make a final pass over the queue.
		private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		// Bounds that final pass, so a slow or unreachable endpoint cannot hold up process shutdown.
		private readonly CancellationTokenSource drainDeadline = new CancellationTokenSource();
		private int droppedEvents;
		private volatile bool disposedValue;

		/// <summary>The number of events waiting to be uploaded. Exposed for tests.</summary>
		internal int QueuedEventCount
		{
			get { return this.events.Count; }
		}

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
			var drainToken = this.drainDeadline.Token;
			var draining = false;

			while (true)
			{
				if (!draining)
				{
					try
					{
						await Task.Delay(PollInterval, token).ConfigureAwait(false);
					}
					catch (OperationCanceledException)
					{
						// Dispose asked us to stop. Make a final pass over whatever is still queued
						// instead of exiting and discarding it — this is the flush path, and the events
						// written just before shutdown are usually the ones worth keeping.
						draining = true;
					}
				}

				try
				{
					var discarded = Interlocked.Exchange(ref this.droppedEvents, 0);
					if (discarded != 0)
					{
						SafeWriteLine($"Discarded {discarded} logs");
					}

					while (this.dequeuedEvents.Count < BatchSize && this.events.TryDequeue(out var logEvent))
						this.dequeuedEvents.Add(logEvent);

					if (this.dequeuedEvents.Any())
					{
						try
						{
							var content = this.serialize(this.dequeuedEvents);
							await this.send(content, draining ? drainToken : CancellationToken.None).ConfigureAwait(false);
						}
						catch (Exception e)
						{
							SafeWriteLine($"Failed uploading {this.dequeuedEvents.Count} logs: {e}");
						}
						this.dequeuedEvents.Clear();
					}
				}
				catch (Exception e)
				{
					// The uploader must never die: a faulted pump would swallow every later event into a
					// queue nobody drains, and make the Dispose that waits on it rethrow.
					SafeWriteLine($"Log upload failed unexpectedly: {e}");
				}

				if (draining && (this.events.IsEmpty || drainToken.IsCancellationRequested))
				{
					break;
				}
			}
		}

		public void Emit(LogEvent logEvent)
		{
			if (this.disposedValue)
			{
				// The uploader has stopped; queuing here would only grow a queue nobody drains.
				return;
			}

			this.events.Enqueue(logEvent);

			// Enforce the cap here rather than in the uploader. The uploader can be parked inside a
			// retry ladder for minutes, during which it applies no back-pressure whatsoever.
			while (this.events.Count > QueueCapacity && this.events.TryDequeue(out var _))
			{
				Interlocked.Increment(ref this.droppedEvents);
			}
		}

		private Task send(HttpContent content, CancellationToken cancellationToken)
		{
			return retryPolicy.ExecuteAsync(async ct =>
			{
				try
				{
					using (await this.client.Request().PostAsync(content, cancellationToken: ct).ConfigureAwait(false))
					{
					}
				}
				catch (FlurlHttpException e)
				{
					// On a failure Flurl throws instead of handing us the response, so this is the only
					// place left to dispose it. Leaving it to the finalizer pins a connection with an
					// unread body — the same failure mode as the original socket exhaustion bug.
					if (e.Call != null && e.Call.Response != null)
						e.Call.Response.Dispose();
					throw;
				}
			}, cancellationToken);
		}

		/// <summary>
		/// Writes a sink diagnostic without ever throwing: stdout may be a closed pipe in a detached
		/// container or a service, and an IOException from here would otherwise kill the uploader.
		/// </summary>
		private static void SafeWriteLine(string message)
		{
			try
			{
				Console.WriteLine(message);
			}
			catch (Exception)
			{
			}
		}

		private HttpContent serialize(IEnumerable<LogEvent> logs)
		{
			var logtailEvents = logs.Select(log =>
			{
				var dict = new Dictionary<string, object>
				{
					{ "dt", log.Timestamp },
					// The rendered message, not the raw template text: messages are escaped literals
					// (see Log.EscapeTemplate), and rendering reverses that escaping.
					{ "message", log.RenderMessage() },
					{ "level", LevelToString(log.Level) }
				};
				if (log.Exception != null)
					dict.Add("exception", log.Exception);
				dict.Add("props", RenderProperties(log.Properties));
				return dict;
			}).ToList();
			var payload = JsonConvert.SerializeObject(logtailEvents, settings);
			return CreateGzipJsonContent(payload);
		}

		/// <summary>
		/// Builds the HTTP body for a batch upload: the UTF-8 JSON, gzip-compressed, with
		/// <c>Content-Type: application/json</c> and <c>Content-Encoding: gzip</c>. BetterStack's
		/// ingest endpoint accepts gzipped bodies and decompresses them server-side (verified:
		/// a gzip-encoded POST returns 202, same as plain JSON). Repetitive log JSON compresses
		/// several-fold, which removes the bulk of this sink's outbound bandwidth.
		/// </summary>
		internal static ByteArrayContent CreateGzipJsonContent(string payload)
		{
			var raw = Encoding.UTF8.GetBytes(payload);
			using (var buffer = new MemoryStream())
			{
				using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
					gzip.Write(raw, 0, raw.Length);
				var content = new ByteArrayContent(buffer.ToArray());
				content.Headers.Add("Content-Type", "application/json");
				content.Headers.Add("Content-Encoding", "gzip");
				return content;
			}
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
				// Set first, so producers stop queuing into a sink that is about to stop draining.
				disposedValue = true;

				if (disposing)
				{
					this.drainDeadline.CancelAfter(DrainTimeout);
					this.cancellationTokenSource.Cancel();

					var drained = false;
					try
					{
						// Bounded: an unreachable endpoint must not hold the process past its shutdown
						// grace period, and on a single-threaded synchronization context (Blazor
						// WebAssembly) an unbounded wait on the uploader would never return at all.
						drained = this.sendTask.Wait(DrainTimeout + TimeSpan.FromSeconds(1));
					}
					catch (Exception e)
					{
						SafeWriteLine($"Failed flushing logs on shutdown: {e}");
					}

					if (!drained)
					{
						SafeWriteLine("Timed out flushing logs on shutdown; remaining events were dropped");
					}

					this.client.Dispose();

					if (drained)
					{
						// Only safe once the uploader has stopped using these tokens.
						this.cancellationTokenSource.Dispose();
						this.drainDeadline.Dispose();
					}
				}
			}
		}

		public void Dispose()
		{
			// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		internal static bool IsTransientError(int? responseStatusCode)
		{
			if (!responseStatusCode.HasValue)
			{
				// No status at all means the request never completed: a timeout, a DNS failure, a reset
				// connection. Those are the most common transient failures of the lot, and requiring a
				// status code here previously excluded every one of them.
				return true;
			}

			var statusCode = responseStatusCode.Value;
			return statusCode == (int)HttpStatusCode.RequestTimeout        // 408
				|| statusCode == 429                                       // Too Many Requests
				|| statusCode >= 500;                                      // any server-side failure
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
