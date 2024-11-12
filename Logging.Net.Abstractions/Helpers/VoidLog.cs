using System;

namespace Logging.Net.Abstractions.Helpers
{
	public class VoidLog : ILog
	{
		public ILog Level(int level)
		{
			return this;
		}

		public void Log(int level, string message, Exception exception = null)
		{
			// Do nothing
		}

		public ILog Name(string name)
		{
			return this;
		}

		public ILog With<T>(string key, T value, bool destructureObjects = false)
		{
			return this;
		}
	}
}
