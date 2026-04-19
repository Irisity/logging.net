using Serilog;
using Serilog.Core;
using Serilog.ExceptionalLogContext;
using Serilog.Templates;
using Serilog.Templates.Themes;

namespace Logging.Net.Serilog
{
    public static class Logging
	{
		internal static Logger mainLogger;
		internal static int? maxLevel;

		/// <summary>
		/// Initializes the ILog implementation that logs through Serilog. 
		/// It supports logging to console, single file or to BetterStack logging (former Logtail).
		/// </summary>
		/// <param name="systemName">If supplied, will include this value in the "run.systemName" property.</param>
		/// <param name="filename">Only used when sink parameter is File. Specifies the filename to use for output.</param>
		/// <param name="betterStackToken">Only used when sink parameter is BetterStack. Specifies the access token used to authenticate with BetterStack.</param>
		/// <param name="application">If supplied, will include this value in the "run.application" property.</param>
		/// <param name="maxLevel">If supplied, any logs using a Level above this value will not be output.</param>
		/// <param name="logHostname">If true, will include the hostname value in the "run.hostname" property.</param>
		/// <param name="coloredConsole">Only used when sink parameter is Console. If null (default), ANSI colors are emitted when stdout is an interactive terminal and suppressed when redirected. Pass true or false to override the auto-detection.</param>
		/// <param name="configure">Optional callback for additional Serilog configuration (e.g. MinimumLevel.Override for specific categories).</param>
		/// <returns>The ILog implementation</returns>
		public static Abstractions.ILog Init(SinkType sink, string systemName = null, string filename = null, string betterStackToken = null, string application = null, int? maxLevel = null, bool logHostname = false, bool? coloredConsole = null, System.Action<LoggerConfiguration> configure = null)
		{
			var loggerConfiguration = new LoggerConfiguration();

			switch (sink)
			{
				case SinkType.Console:
					loggerConfiguration = loggerConfiguration.WriteTo.Console(getOutputTemplate(resolveConsoleTheme(coloredConsole)));
					break;
				case SinkType.File:
					loggerConfiguration = loggerConfiguration.WriteTo.File(getOutputTemplate(), filename);
					break;
				case SinkType.BetterStack:
					loggerConfiguration = loggerConfiguration.WriteTo.LogtailSink(betterStackToken);
					break;
			}

			loggerConfiguration = loggerConfiguration
				.MinimumLevel.Verbose()
				.Enrich.FromLogContext()
				.Enrich.WithDemystifiedStackTraces()
				.Enrich.WithExceptionalLogContext();

			if (logHostname)
			{
				loggerConfiguration = loggerConfiguration.Enrich.With<HostnameEnricher>();
			}

			if (systemName != null)
			{
				loggerConfiguration = loggerConfiguration.Enrich.With(new SystemNameEnricher(systemName));
			}

			if (application != null)
			{
				loggerConfiguration = loggerConfiguration.Enrich.With(new ApplicationNameEnricher(application));
			}

			configure?.Invoke(loggerConfiguration);

			mainLogger?.Dispose();
			mainLogger = loggerConfiguration.CreateLogger();
			global::Serilog.Log.Logger = mainLogger;

			Logging.maxLevel = maxLevel;

			Abstractions.LogFactory.LogImplementationAccessor = () => new Log();

			var logContextAccessor = new LogContextAccessor();
			Abstractions.LogContext.LogContextAccessor = () => logContextAccessor; 

			return new Log();
		}

		/// <summary>
		/// Flushes and closes Serilog.
		/// </summary>
		public static void Flush()
		{
			mainLogger.Dispose();
		}

		private static ExpressionTemplate getOutputTemplate(TemplateTheme theme = null)
		{
			// Coalesce to SourceContext so logs routed through Microsoft.Extensions.Logging
			// (EF Core, Kestrel, etc. — which carry the MEL category as SourceContext
			// instead of setting log.name) still render a name in the [LVL name] slot.
			return new ExpressionTemplate("{@t:HH:mm:ss.fff} [{@l:u3} {coalesce(log.name, SourceContext)}] {@m} {@p}\n{@x}", theme: theme);
		}

		private static TemplateTheme resolveConsoleTheme(bool? coloredConsoleOverride)
		{
			var useColor = coloredConsoleOverride ?? !System.Console.IsOutputRedirected;
			return useColor ? TemplateTheme.Code : null;
		}
	}
}
