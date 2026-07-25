using System;
using System.IO;
using Logging.Net.Abstractions;
using Logging.Net.Abstractions.Helpers;
using SerilogLogging = Logging.Net.Serilog.Logging;
using SinkType = Logging.Net.Serilog.SinkType;

namespace Logging.Net.Tests;

/// <summary>
/// End-to-end tests that exercise the Serilog implementation by writing to a temp file and parsing
/// the output. The output template renders the level as a three-letter token, so asserting on
/// "[LVL name]" is what actually pins the level mapping down; asserting only on the message text
/// would pass even if every level mapped to the same Serilog level.
/// </summary>
public class SerilogIntegrationTests : IDisposable
{
    private readonly string logFile;

    public SerilogIntegrationTests()
    {
        logFile = NewLogFile();
        PreInitLogBuffer.Clear();
    }

    public void Dispose()
    {
        SerilogLogging.Flush();
        LogFactory.LogImplementationAccessor = null;
        LogContext.LogContextAccessor = null;
        PreInitLogBuffer.Clear();
        Delete(logFile);
    }

    private static string NewLogFile() =>
        Path.Combine(Path.GetTempPath(), $"logging-net-test-{Guid.NewGuid():N}.log");

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Init_ToFile_WritesEveryLevel_WithTheExpectedSeverity()
    {
        var log = SerilogLogging.Init(SinkType.File, filename: logFile);
        log.Name("T").Error("err-msg");
        log.Name("T").Info("info-msg");
        log.Name("T").Debug("debug-msg");
        log.Name("T").Verbose("verbose-msg");
        log.Name("T").Trace("trace-msg");
        SerilogLogging.Flush();

        var contents = File.ReadAllText(logFile);
        Assert.Contains("[ERR T] err-msg", contents);
        Assert.Contains("[INF T] info-msg", contents);
        Assert.Contains("[DBG T] debug-msg", contents);
        // Serilog has no level below Verbose, so Trace shares it here. The numeric level is still
        // carried on the event in the structured "log.level" property (which this output template
        // does not render, but the BetterStack sink uploads), so the two remain distinguishable there.
        Assert.Contains("[VRB T] verbose-msg", contents);
        Assert.Contains("[VRB T] trace-msg", contents);
    }

    [Fact]
    public void Level_IsAdditive_AndDemotesErrorsAccordingly()
    {
        // The accumulated level is added before the severity is chosen, so an error raised on a log
        // configured as Verbose reports as Debug. Surprising, but deliberate — see ILog.Level.
        var log = SerilogLogging.Init(SinkType.File, filename: logFile);
        log.Name("T").Level(Level.Verbose).Error("demoted-error");
        log.Name("T").Error("plain-error");
        SerilogLogging.Flush();

        var contents = File.ReadAllText(logFile);
        Assert.Contains("[DBG T] demoted-error", contents);
        Assert.Contains("[ERR T] plain-error", contents);
    }

    [Fact]
    public void MaxLevel_FiltersOutMoreVerboseEntries()
    {
        var log = SerilogLogging.Init(SinkType.File, filename: logFile, maxLevel: 0);
        log.Name("T").Info("kept");
        log.Name("T").Debug("filtered");
        SerilogLogging.Flush();

        var contents = File.ReadAllText(logFile);
        Assert.Contains("kept", contents);
        Assert.DoesNotContain("filtered", contents);
    }

    [Fact]
    public void With_PropertiesReachTheOutput()
    {
        var log = SerilogLogging.Init(SinkType.File, filename: logFile);
        log.Name("T").With("orderId", 42).Info("with-props");
        SerilogLogging.Flush();

        var contents = File.ReadAllText(logFile);
        Assert.Contains("with-props", contents);
        Assert.Contains("\"orderId\":42", contents);
    }

    [Fact]
    public void With_UserPropertyNamedLog_IsPreservedAlongsideTheReservedOne()
    {
        // "log" is this library's own structured slot; a caller's value under that name is moved to
        // "user.log" rather than being silently overwritten.
        var log = SerilogLogging.Init(SinkType.File, filename: logFile);
        log.Name("T").With("log", "user-value").Info("collision");
        SerilogLogging.Flush();

        var contents = File.ReadAllText(logFile);
        Assert.Contains("[INF T] collision", contents);
        Assert.Contains("\"user.log\":\"user-value\"", contents);
    }

    [Fact]
    public void Message_WithBraces_IsWrittenVerbatim()
    {
        // Messages are literals, not message templates: braces must survive to the output rather
        // than being parsed as property tokens.
        var log = SerilogLogging.Init(SinkType.File, filename: logFile);
        log.Name("T").Info("regex a{2,3} and {Unfilled}");
        SerilogLogging.Flush();

        var contents = File.ReadAllText(logFile);
        Assert.Contains("regex a{2,3} and {Unfilled}", contents);
    }

    [Fact]
    public void Exception_IsRenderedIntoTheOutput()
    {
        var log = SerilogLogging.Init(SinkType.File, filename: logFile);
        log.Name("T").Error("boom", new InvalidOperationException("the-inner-detail"));
        SerilogLogging.Flush();

        var contents = File.ReadAllText(logFile);
        Assert.Contains("[ERR T] boom", contents);
        Assert.Contains("the-inner-detail", contents);
        Assert.Contains(nameof(InvalidOperationException), contents);
    }

    [Fact]
    public void Enrichers_UseTheDocumentedPropertyNames()
    {
        var log = SerilogLogging.Init(SinkType.File, filename: logFile,
            systemName: "the-system", application: "the-app", logHostname: true);
        log.Name("T").Info("enriched");
        SerilogLogging.Flush();

        var contents = File.ReadAllText(logFile);
        Assert.Contains("\"run.system\":\"the-system\"", contents);
        Assert.Contains("\"run.app\":\"the-app\"", contents);
        Assert.Contains("\"run.hostname\"", contents);
    }

    [Fact]
    public void LogContext_PropagatesPropertyIntoLogs_AndStopsAtTheEndOfTheScope()
    {
        var log = SerilogLogging.Init(SinkType.File, filename: logFile);
        using (LogContext.With("path", "/tmp/file.txt"))
        {
            log.Name("T").Info("inside-scope");
        }
        log.Name("T").Info("outside-scope");
        SerilogLogging.Flush();

        var lines = File.ReadAllLines(logFile);
        var inside = Assert.Single(lines, l => l.Contains("inside-scope"));
        var outside = Assert.Single(lines, l => l.Contains("outside-scope"));
        Assert.Contains("/tmp/file.txt", inside);
        Assert.DoesNotContain("/tmp/file.txt", outside);
    }

    [Fact]
    public void Init_Again_RebindsLogsCreatedBeforeIt()
    {
        // Regression test: an ILog that already logged must follow the re-initialization instead of
        // writing into the logger the previous Init created and then disposed.
        var log = SerilogLogging.Init(SinkType.File, filename: logFile).Name("T").With("k", "v");
        log.Info("before-reinit");

        var second = NewLogFile();
        try
        {
            SerilogLogging.Init(SinkType.File, filename: second);
            log.Info("after-reinit");
            SerilogLogging.Flush();

            var first = File.ReadAllText(logFile);
            var contents = File.ReadAllText(second);
            Assert.Contains("before-reinit", first);
            Assert.DoesNotContain("after-reinit", first);
            Assert.Contains("[INF T] after-reinit", contents);
            Assert.Contains("\"k\":\"v\"", contents); // the configuration is rebuilt, not just the message
        }
        finally
        {
            Delete(second);
        }
    }

    [Fact]
    public void StaticLog_CreatedBeforeInit_RebindsAcrossReinitialization()
    {
        var staticLog = LogFactory.Name("Static");
        SerilogLogging.Init(SinkType.File, filename: logFile);
        staticLog.Info("first-init");

        var second = NewLogFile();
        try
        {
            SerilogLogging.Init(SinkType.File, filename: second);
            staticLog.Info("second-init");
            SerilogLogging.Flush();

            Assert.Contains("first-init", File.ReadAllText(logFile));
            Assert.Contains("[INF Static] second-init", File.ReadAllText(second));
        }
        finally
        {
            Delete(second);
        }
    }

    [Fact]
    public void Flush_BeforeInit_DoesNotThrow()
    {
        // A shutdown hook running after a failed Init must not throw over the original failure.
        SerilogLogging.Flush();
        SerilogLogging.Flush();
    }

    [Fact]
    public void Log_AfterFlush_IsDiscardedWithoutThrowing()
    {
        var log = SerilogLogging.Init(SinkType.File, filename: logFile);
        log.Name("T").Info("before-flush");
        SerilogLogging.Flush();

        log.Name("T").Info("after-flush");

        var contents = File.ReadAllText(logFile);
        Assert.Contains("before-flush", contents);
        Assert.DoesNotContain("after-flush", contents);
    }

    [Fact]
    public void PreInitLogs_AreReplayedIntoTheFirstInitializedSink()
    {
        var staticLog = LogFactory.Name("Boot");
        staticLog.Error("config-file-missing");

        SerilogLogging.Init(SinkType.File, filename: logFile);
        SerilogLogging.Flush();

        Assert.Contains("[ERR Boot] config-file-missing", File.ReadAllText(logFile));
    }
}
