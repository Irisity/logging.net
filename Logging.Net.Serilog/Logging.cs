using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.ExceptionalLogContext;
using Serilog.Templates;
using Serilog.Templates.Themes;

namespace Logging.Net.Serilog
{
    public static class Logging
	{
		// Volatile because ILog instances compare against this reference to notice that Init or Flush
		// replaced the logger, and rebuild themselves rather than writing into a disposed one.
		internal static volatile Logger mainLogger;
		internal static int? maxLevel;

		/// <summary>
		/// Initializes the ILog implementation that logs through Serilog. 
		/// It supports logging to console, single file or to BetterStack logging (former Logtail).
		/// </summary>
		/// <param name="sink">Where the logs are written: console, a single file, or BetterStack.</param>
		/// <param name="systemName">If supplied, will include this value in the "run.system" property.</param>
		/// <param name="filename">Only used when sink parameter is File. Specifies the filename to use for output.</param>
		/// <param name="betterStackToken">Only used when sink parameter is BetterStack. Specifies the access token used to authenticate with BetterStack.</param>
		/// <param name="application">If supplied, will include this value in the "run.app" property.</param>
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

			// Point logging at a silent logger before disposing the previous one, so that a concurrent
			// write during the swap goes nowhere rather than into a disposed logger. ILog instances
			// created before this call notice the new reference and rebind themselves to it.
			var previous = mainLogger;
			if (previous != null)
			{
				mainLogger = createSilentLogger();
				global::Serilog.Log.Logger = mainLogger;
				previous.Dispose();
			}

			Logging.maxLevel = maxLevel;

			var created = loggerConfiguration.CreateLogger();
			mainLogger = created;
			global::Serilog.Log.Logger = created;

			// A distinct delegate instance per initialization: ILog instances resolved through
			// LogFactory compare the accessor reference to detect that they must re-resolve.
			Abstractions.LogFactory.LogImplementationAccessor = () => new Log();

			var logContextAccessor = new LogContextAccessor();
			Abstractions.LogContext.LogContextAccessor = () => logContextAccessor; 

			return new Log();
		}

		/// <summary>
		/// Wires ASP.NET Core's ILoggerFactory through the Serilog instance created by Init().
		/// Call this after Init() and before builder.Build(). Log levels are still controlled
		/// by the "Logging" section in appsettings.json.
		/// </summary>
		public static void UseMicrosoftLogging(Microsoft.Extensions.Logging.ILoggingBuilder loggingBuilder)
		{
			loggingBuilder.ClearProviders();
			// No explicit logger: the provider then resolves Serilog's static Log.Logger per write, so
			// the bridge follows a later Init or Flush instead of holding on to a disposed logger.
			loggingBuilder.AddSerilog(dispose: false);
		}

		/// <summary>
		/// Flushes and closes Serilog. Logging after this call is silently discarded until Init is called again.
		/// </summary>
		public static void Flush()
		{
			var previous = mainLogger;
			if (previous == null)
			{
				// Never initialized, or already flushed. A shutdown hook running after a failed Init
				// must not throw over whatever caused the failure.
				return;
			}

			// Swap in a silent logger first: leaving the disposed one in place would mean every
			// existing ILog keeps writing into it, and every such write is discarded without a trace.
			mainLogger = createSilentLogger();
			global::Serilog.Log.Logger = mainLogger;
			previous.Dispose();
		}

		private static Logger createSilentLogger()
		{
			return new LoggerConfiguration().CreateLogger();
		}

		private static ExpressionTemplate getOutputTemplate(TemplateTheme theme = null)
		{
			// Coalesce to SourceContext so logs routed through Microsoft.Extensions.Logging
			// (EF Core, Kestrel, etc. — which carry the MEL category as SourceContext
			// instead of setting log.name) still render a name in the [LVL name] slot.
			// rest() emits the remaining properties as JSON, excluding any already referenced
			// in the template — so `log` (used via log.name) is not duplicated in the output.
			// Warning is rendered as INF to match how the BetterStack sink uploads it: this library's
			// Level scale has no warning, but events bridged in from Microsoft.Extensions.Logging
			// (EF Core, Kestrel, ...) still carry one, and the two sinks must not disagree about it.
			// This is presentation only — the event keeps its Warning level, so MinimumLevel.Override
			// and other level-based filtering passed through `configure` behave as written.
			return new ExpressionTemplate("{@t:HH:mm:ss.fff} [{#if @l = 'Warning'}INF{#else}{@l:u3}{#end} {coalesce(log.name, SourceContext)}] {@m} {rest():j}\n{@x}", theme: theme);
		}

		private static TemplateTheme resolveConsoleTheme(bool? coloredConsoleOverride)
		{
			var useColor = coloredConsoleOverride ?? !System.Console.IsOutputRedirected;
			return useColor ? TemplateTheme.Code : null;
		}
	}
}
