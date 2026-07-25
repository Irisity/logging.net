using Serilog.Core;
using Serilog.Events;

namespace Logging.Net.Serilog
{
	public sealed class ApplicationNameEnricher : ILogEventEnricher
	{
		private readonly string applicationName;
		private LogEventProperty cachedProperty;

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
			if (this.cachedProperty == null)
			{
				this.cachedProperty = CreateProperty(propertyFactory, this.applicationName);
			}
			return this.cachedProperty;
		}

		private static LogEventProperty CreateProperty(ILogEventPropertyFactory propertyFactory, string applicationName)
		{
			return propertyFactory.CreateProperty("run.app", applicationName);
		}
	}
}
