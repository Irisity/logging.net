using Logging.Net.Abstractions;
using Serilog;
using Serilog.Events;
using System;
using System.Text;

namespace Logging.Net.Serilog
{
	internal class Log : ILog
	{
		private const string LevelKey = "log.level";
		private const string NameKey = "log.name";

		private ILogger logger;
		private int level;
		private string name;

		public Log() 
		{
			this.logger = Logging.mainLogger;
		}

		public ILog Level(int level)
		{
			var log = new Log(this);
			log.level += level;
			return log;
		}

		public ILog Name(string name)
		{
			var log = new Log(this);
			if (log.name == null)
				log.name = name;
			else
			{
				var sb = new StringBuilder(log.name, log.name.Length + 1 + name.Length);
				sb.Append('/');
				sb.Append(name);
				log.name = sb.ToString();
			}
			return log;
		}

		public ILog With<T>(string key, T value, bool destructureObjects = false)
		{
			var log = new Log(this);
			log.logger = log.logger.ForContext(key, value, destructureObjects);
			return log;
		}

		void ILog.Log(int level, string message, Exception exception)
		{
			level += this.level;
			if (!Logging.maxLevel.HasValue || level <= Logging.maxLevel.Value)
			{
				var log = this.logger.ForContext("log", new { level, this.name }, true);

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
				default:
				return LogEventLevel.Verbose;
			}
		}

		private Log(Log log)
		{
			this.level = log.level;
			this.name = log.name;
			this.logger = log.logger;
		}
	}
}
