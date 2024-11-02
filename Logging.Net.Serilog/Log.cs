using Logging.Net.Abstractions;
using Serilog;
using Serilog.Core;
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
			log.logger = log.logger.ForContext(LevelKey, log.level);
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
			log.logger.ForContext(NameKey, log.name);
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
			if (level < 0)
			{
				if (exception == null)
					this.logger.Error(message);
				else
					this.logger.Error(exception, message);

				return;
			}

			level += this.level;
			if (!Logging.maxLevel.HasValue || level <= Logging.maxLevel.Value)
			{
				var log = this.logger.ForContext(LevelKey, level);
				if (exception == null)
					log.Information(message);
				else
					log.Information(exception, message);
			}
		}

		private Log(Log log)
		{
			level = log.level;
			name = log.name;
			logger = log.logger;
		}
	}
}
