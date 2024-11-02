using Log.Serilog;
using Serilog;
using Serilog.Core;
using Serilog.ExceptionalLogContext;
using System;

namespace Logging.Net.Serilog
{
	public static class Logging
	{
		internal static Logger mainLogger;
		internal static int? maxLevel;

		public static Abstractions.ILog Init(SinkType sink, string systemName = null, string filename = null, string logtailToken = null, string application = null, int? maxLevel = null)
		{
			var loggerConfiguration = new LoggerConfiguration();

			switch (sink)
			{
				case SinkType.Console:
					loggerConfiguration = loggerConfiguration.WriteTo.Console(outputTemplate: getOutputTemplate());
					break;
				case SinkType.File:
					loggerConfiguration = loggerConfiguration.WriteTo.File(filename, outputTemplate: getOutputTemplate());
					break;
				case SinkType.Logtail:
					loggerConfiguration = loggerConfiguration.WriteTo.LogtailSink(logtailToken);
					break;
			}

			loggerConfiguration = loggerConfiguration
				.Enrich.FromLogContext()
				.Enrich.WithDemystifiedStackTraces()
				.Enrich.WithExceptionalLogContext()
				.Enrich.With<HostnameEnricher>();

			if (systemName != null)
			{
				loggerConfiguration = loggerConfiguration.Enrich.With(new SystemNameEnricher(systemName));
			}

			if (application != null)
			{
				loggerConfiguration = loggerConfiguration.Enrich.With(new ApplicationNameEnricher(application));
			}

			mainLogger = loggerConfiguration.CreateLogger();

			Logging.maxLevel = maxLevel;

			return new Log();
		}

		public static void Flush()
		{
			mainLogger.Dispose();
		}

		private static string getOutputTemplate()
		{
			return "{Timestamp:HH:mm:ss.fff} [{Level:u3} {logLevel} {logName}] {Message} {Properties}{NewLine}{Exception}";
		}
	}
}
