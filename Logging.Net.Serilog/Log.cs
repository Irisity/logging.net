using Logging.Net.Abstractions;
using Logging.Net.Abstractions.Helpers;
using Serilog;
using Serilog.Events;
using System;

namespace Logging.Net.Serilog
{
	/// <summary>
	/// The ILog implementation using Serilog.
	/// </summary>
    internal class Log : ILog
	{
		private NameAndLevelHandler nameAndLevelHandler;
		private ILogger logger;

		public Log() 
		{
			this.logger = Logging.mainLogger;
			this.nameAndLevelHandler = new NameAndLevelHandler();
		}

		public ILog Level(int level)
		{
			var copy = new Log(this);
			copy.nameAndLevelHandler = copy.nameAndLevelHandler.AddLevel(level);
			return copy;
		}

		public ILog Name(string name)
		{
			var copy = new Log(this);
			copy.nameAndLevelHandler = copy.nameAndLevelHandler.AddName(name);
			return copy;
		}

		public ILog With<T>(string key, T value, bool destructureObjects = false)
		{
			var log = new Log(this);
			log.logger = log.logger.ForContext(key, value, destructureObjects);
			return log;
		}

		void ILog.Log(int level, string message, Exception exception)
		{
			level += this.nameAndLevelHandler.Level;
			if (!Logging.maxLevel.HasValue || level <= Logging.maxLevel.Value)
			{
				// Use name and level inside a structured object to be able to refer to them from the template,
				// as Serilog does not allow referencing fields whose name contain dots
				var log = this.logger.ForContext("log", new { level, name = this.nameAndLevelHandler.Name }, true);

				if (exception == null)
					log.Write(GetSerilogLevel(level), message);
				else
					log.Write(GetSerilogLevel(level), exception, message);
			}
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

		private Log(Log log)
		{
			this.nameAndLevelHandler = log.nameAndLevelHandler;
			this.logger = log.logger;
		}
	}
}
