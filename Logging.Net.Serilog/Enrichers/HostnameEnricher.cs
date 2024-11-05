using Serilog.Core;
using Serilog.Events;
using System.Net;

namespace Log.Serilog
{
	public class HostnameEnricher : ILogEventEnricher
	{
		private LogEventProperty cachedProperty;

		public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
		{
			logEvent.AddPropertyIfAbsent(GetLogEventProperty(propertyFactory));
		}

		private LogEventProperty GetLogEventProperty(ILogEventPropertyFactory propertyFactory)
		{
			if (this.cachedProperty == null)
			{
				this.cachedProperty = CreateProperty(propertyFactory);
			}
			return this.cachedProperty;
		}

		private static LogEventProperty CreateProperty(ILogEventPropertyFactory propertyFactory)
		{
			string hostname = Dns.GetHostName();
			return propertyFactory.CreateProperty("run.hostname", hostname);
		}
	}
}