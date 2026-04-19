using Serilog.Core;
using Serilog.Events;

namespace Logging.Net.Serilog
{
	public sealed class SystemNameEnricher : ILogEventEnricher
	{
		private readonly string systemName;
		private LogEventProperty cachedProperty;

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
			if (this.cachedProperty == null)
			{
				this.cachedProperty = CreateProperty(propertyFactory, this.systemName);
			}
			return this.cachedProperty;
		}

		private static LogEventProperty CreateProperty(ILogEventPropertyFactory propertyFactory, string systemName)
		{
			return propertyFactory.CreateProperty("run.system", systemName);
		}
	}
}
