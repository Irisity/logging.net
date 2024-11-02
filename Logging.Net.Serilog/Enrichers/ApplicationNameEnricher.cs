using Serilog.Core;
using Serilog.Events;

namespace Log.Serilog
{
	public class ApplicationNameEnricher : ILogEventEnricher
	{
		private LogEventProperty cachedProperty;
		private string applicationName;

		public ApplicationNameEnricher(string applicationName)
		{
			this.applicationName = applicationName;
		}

		public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
		{
			logEvent.AddPropertyIfAbsent(GetLogEventProperty(propertyFactory));
		}

		private LogEventProperty GetLogEventProperty(ILogEventPropertyFactory propertyFactory)
		{
			if (cachedProperty == null)
			{
				cachedProperty = CreateProperty(propertyFactory, applicationName);
			}
			return cachedProperty;
		}

		private static LogEventProperty CreateProperty(ILogEventPropertyFactory propertyFactory, string applicationName)
		{
			return propertyFactory.CreateProperty("run.app", applicationName);
		}
	}
}