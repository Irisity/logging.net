using System;
using System.IO;
using Logging.Net.Abstractions;
using SerilogLogging = Logging.Net.Serilog.Logging;
using SinkType = Logging.Net.Serilog.SinkType;

namespace Logging.Net.Tests;

/// <summary>
/// End-to-end smoke tests that exercise the Serilog implementation by writing to a temp file
/// and parsing the output. Covers level mapping (#14 regression) and basic LogContext propagation.
/// </summary>
public class SerilogIntegrationTests : IDisposable
{
    private readonly string logFile;

    public SerilogIntegrationTests()
    {
        logFile = Path.Combine(Path.GetTempPath(), $"logging-net-test-{Guid.NewGuid():N}.log");
    }

    public void Dispose()
    {
        SerilogLogging.Flush();
        LogFactory.LogImplementationAccessor = null;
        LogContext.LogContextAccessor = null;
        if (File.Exists(logFile))
        {
            try { File.Delete(logFile); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Init_ToFile_WritesAllLevels()
    {
        var log = SerilogLogging.Init(SinkType.File, filename: logFile);
        log.Name("T").Error("err-msg");
        log.Name("T").Info("info-msg");
        log.Name("T").Debug("debug-msg");
        log.Name("T").Verbose("verbose-msg");
        log.Name("T").Trace("trace-msg"); // regression for #14 — must not lose the event
        SerilogLogging.Flush();

        var contents = File.ReadAllText(logFile);
        Assert.Contains("err-msg", contents);
        Assert.Contains("info-msg", contents);
        Assert.Contains("debug-msg", contents);
        Assert.Contains("verbose-msg", contents);
        Assert.Contains("trace-msg", contents);
    }

    [Fact]
    public void LogContext_PropagatesPropertyIntoLogs()
    {
        var log = SerilogLogging.Init(SinkType.File, filename: logFile);
        using (LogContext.With("path", "/tmp/file.txt"))
        {
            log.Name("T").Info("read-file");
        }
        SerilogLogging.Flush();

        var contents = File.ReadAllText(logFile);
        Assert.Contains("/tmp/file.txt", contents);
    }

    [Fact]
    public void Init_CanBeCalledTwice_WithoutLeaking()
    {
        // Regression for #9 — second Init should dispose the prior logger, not leak it.
        SerilogLogging.Init(SinkType.File, filename: logFile);
        var second = Path.Combine(Path.GetTempPath(), $"logging-net-test-{Guid.NewGuid():N}.log");
        try
        {
            var log = SerilogLogging.Init(SinkType.File, filename: second);
            log.Name("T").Info("second-init");
            SerilogLogging.Flush();
            Assert.Contains("second-init", File.ReadAllText(second));
        }
        finally
        {
            if (File.Exists(second)) try { File.Delete(second); } catch { }
        }
    }
}
