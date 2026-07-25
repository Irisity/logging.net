using Logging.Net.Abstractions;
using System;

namespace Logging.Net.Serilog
{
	internal class LogContextAccessor : ILogContext
	{
		public IDisposable With<T>(string key, T value, bool destructureObjects)
		{
			// Same reserved-name handling as ILog.With, so a context property named "log" is not
			// silently swallowed by this library's own structured property.
			return global::Serilog.Context.LogContext.PushProperty(Log.SafePropertyName(key), value, destructureObjects);
		}
	}
}
