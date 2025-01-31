using Logging.Net.Abstractions;
using System;

namespace Logging.Net.Serilog
{
	internal class LogContextAccessor : ILogContext
	{
		public IDisposable With<T>(string key, T value, bool destructureObjects)
		{
			return global::Serilog.Context.LogContext.PushProperty(key, value, destructureObjects);
		}
	}
}
