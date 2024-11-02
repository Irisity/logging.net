using Serilog.Core;
using Serilog.Events;

namespace Log.Serilog
{
	public class SystemNameEnricher : ILogEventEnricher
	{
		private LogEventProperty cachedProperty;
		private string systemName;

		public SystemNameEnricher(string systemName)
		{
			this.systemName = systemName;
		}

		public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
		{
			logEvent.AddPropertyIfAbsent(GetLogEventProperty(propertyFactory));
		}

		private LogEventProperty GetLogEventProperty(ILogEventPropertyFactory propertyFactory)
		{
			if (cachedProperty == null)
			{
				cachedProperty = CreateProperty(propertyFactory, systemName);
			}
			return cachedProperty;
		}

		private static LogEventProperty CreateProperty(ILogEventPropertyFactory propertyFactory, string systemName)
		{
			return propertyFactory.CreateProperty("run.system", systemName);
		}
	}
}