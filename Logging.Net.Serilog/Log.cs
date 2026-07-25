using Logging.Net.Abstractions;
using Logging.Net.Abstractions.Helpers;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Logging.Net.Serilog
{
	/// <summary>
	/// The ILog implementation using Serilog.
	/// </summary>
	/// <remarks>
	/// The properties configured through With are kept as a list rather than being composed into an
	/// ILogger once, so that this instance can rebuild itself against the current logger whenever
	/// Init or Flush replaces it. Binding to a logger permanently would mean that every ILog created
	/// before a re-initialization keeps writing into a disposed logger, which discards the events silently.
	/// </remarks>
    internal class Log : ILog
	{
		private readonly NameAndLevelHandler nameAndLevelHandler;
		private readonly List<ILoggerProperty> properties;

		// The logger composed from the properties above, together with the root logger it was composed
		// from. Published as one immutable object so a reader can never pair the current root logger
		// with a composition made from a previous one.
		private Bound bound;

		public Log()
		{
			this.nameAndLevelHandler = new NameAndLevelHandler();
		}

		public ILog Level(int level)
		{
			return new Log(this.nameAndLevelHandler.AddLevel(level), this.properties);
		}

		public ILog Name(string name)
		{
			return new Log(this.nameAndLevelHandler.AddName(name), this.properties);
		}

		public ILog With<T>(string key, T value, bool destructureObjects = false)
		{
			var property = (ILoggerProperty)new LoggerProperty<T>(SafePropertyName(key), value, destructureObjects);
			List<ILoggerProperty> copy;
			if (this.properties == null)
			{
				copy = new List<ILoggerProperty> { property };
			}
			else
			{
				copy = new List<ILoggerProperty>(this.properties.Count + 1);
				copy.AddRange(this.properties);
				copy.Add(property);
			}
			return new Log(this.nameAndLevelHandler, copy);
		}

		void ILog.Log(int level, string message, Exception exception)
		{
			level += this.nameAndLevelHandler.Level;
			if (Logging.maxLevel.HasValue && level > Logging.maxLevel.Value)
			{
				return;
			}

			var logger = CurrentLogger();
			if (logger == null)
			{
				return;
			}

			// Use name and level inside a structured object to be able to refer to them from the template,
			// as Serilog does not allow referencing fields whose name contain dots.
			// The property is attached via a custom enricher that writes a StructureValue directly
			// onto the log event. Going through ForContext(name, value, destructure) routes the value
			// through PropertyValueConverter, which (a) under Blazor WebAssembly's reflection
			// constraints does not destructure anonymous types into a StructureValue, and (b) wraps
			// any pre-built LogEventPropertyValue inside a ScalarValue, both of which break the
			// ExpressionTemplate's `log.name` member access.
			var log = logger.ForContext(new LogStructureEnricher(level, this.nameAndLevelHandler.Name));

			// The message is a literal, not a message template: this abstraction takes the data in
			// properties and expects messages to stay static so they remain searchable. Escaping means
			// braces in the message survive to the output instead of being parsed as property tokens.
			var template = EscapeTemplate(message);

			if (exception == null)
				log.Write(GetSerilogLevel(level), template);
			else
				log.Write(GetSerilogLevel(level), exception, template);
		}

		private ILogger CurrentLogger()
		{
			var root = Logging.mainLogger;
			if (root == null)
			{
				return null;
			}

			var current = Volatile.Read(ref this.bound);
			if (current != null && ReferenceEquals(current.Root, root))
			{
				return current.Composed;
			}

			ILogger composed = root;
			if (this.properties != null)
			{
				foreach (var property in this.properties)
				{
					composed = property.Apply(composed);
				}
			}

			Volatile.Write(ref this.bound, new Bound(root, composed));
			return composed;
		}

		/// <summary>
		/// The property name this library reserves for its own structured name/level object.
		/// </summary>
		internal const string ReservedPropertyName = "log";

		/// <summary>
		/// Moves a caller's property out of the way if it collides with the reserved name.
		/// </summary>
		/// <remarks>
		/// This has to happen where the property is attached rather than in the enricher: Serilog runs
		/// the most recently added enricher first, and properties added through ForContext use
		/// AddPropertyIfAbsent, so the reserved object is already in place by the time the caller's
		/// value is applied and the caller's value is dropped without a trace.
		/// </remarks>
		internal static string SafePropertyName(string key)
		{
			return key == ReservedPropertyName ? "user." + ReservedPropertyName : key;
		}

		/// <summary>
		/// Doubles the braces in a literal message so that Serilog's message template parser reproduces
		/// it verbatim. LogEvent.RenderMessage reverses this, so sinks see the original text.
		/// </summary>
		internal static string EscapeTemplate(string message)
		{
			if (message == null || message.IndexOf('{') < 0 && message.IndexOf('}') < 0)
			{
				return message;
			}
			return message.Replace("{", "{{").Replace("}", "}}");
		}

		private LogEventLevel GetSerilogLevel(int level)
		{
			if (level < 0)
				return LogEventLevel.Error;

			switch (level)
			{
				case (int)Abstractions.Level.Debug: return LogEventLevel.Debug;
				case (int)Abstractions.Level.Info: return LogEventLevel.Information;
				case (int)Abstractions.Level.Verbose:
				case (int)Abstractions.Level.Trace:
				default:
					return LogEventLevel.Verbose;
			}
		}

		private Log(NameAndLevelHandler nameAndLevelHandler, List<ILoggerProperty> properties)
		{
			this.nameAndLevelHandler = nameAndLevelHandler;
			this.properties = properties;
		}

		private interface ILoggerProperty
		{
			ILogger Apply(ILogger logger);
		}

		private sealed class LoggerProperty<T> : ILoggerProperty
		{
			private readonly string key;
			private readonly T value;
			private readonly bool destructureObjects;

			public LoggerProperty(string key, T value, bool destructureObjects)
			{
				this.key = key;
				this.value = value;
				this.destructureObjects = destructureObjects;
			}

			public ILogger Apply(ILogger logger)
			{
				return logger.ForContext(this.key, this.value, this.destructureObjects);
			}
		}

		private sealed class Bound
		{
			public readonly ILogger Root;
			public readonly ILogger Composed;

			public Bound(ILogger root, ILogger composed)
			{
				Root = root;
				Composed = composed;
			}
		}

		private sealed class LogStructureEnricher : ILogEventEnricher
		{
			private readonly LogEventProperty property;

			public LogStructureEnricher(int level, string name)
			{
				this.property = new LogEventProperty(ReservedPropertyName, new StructureValue(new List<LogEventProperty>
				{
					new LogEventProperty("level", new ScalarValue(level)),
					new LogEventProperty("name", new ScalarValue(name)),
				}));
			}

			public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
			{
				logEvent.AddOrUpdateProperty(this.property);
			}
		}
	}
}
