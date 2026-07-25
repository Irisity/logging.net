using System;
using System.Threading;
using Logging.Net.Abstractions;

namespace Logging.Net.Tests;

public class LogExtensionsTests
{
    [Fact]
    public void Time_LogsElapsed_OnNormalExit()
    {
        var recorder = new InMemoryLog();
        using (recorder.Time("op")) { Thread.Sleep(1); }

        Assert.Single(recorder.Entries);
        Assert.Equal("op", recorder.Entries[0].Message);
        Assert.True(recorder.Entries[0].Properties.ContainsKey("elapsed"));
    }

    [Fact]
    public void Time_LogsElapsed_OnException()
    {
        var recorder = new InMemoryLog();
        Action act = () =>
        {
            using (recorder.Time("op"))
            {
                throw new InvalidOperationException();
            }
        };
        Assert.Throws<InvalidOperationException>(act);

        Assert.Single(recorder.Entries);
        Assert.True(recorder.Entries[0].Properties.ContainsKey("elapsed"));
    }

    [Fact]
    public void TimeFunc_ReturnsValue_AndLogsElapsed()
    {
        var recorder = new InMemoryLog();
        var result = recorder.TimeFunc("op", () => 42);

        Assert.Equal(42, result);
        Assert.Single(recorder.Entries);
        Assert.True(recorder.Entries[0].Properties.ContainsKey("elapsed"));
    }

    [Fact]
    public void TimeFunc_LogsElapsed_WhenFuncThrows()
    {
        // Regression test for #6.
        var recorder = new InMemoryLog();
        Func<int> throwing = () => throw new InvalidOperationException();
        Assert.Throws<InvalidOperationException>(() => recorder.TimeFunc("op", throwing));

        Assert.Single(recorder.Entries);
        Assert.Equal("op", recorder.Entries[0].Message);
        Assert.True(recorder.Entries[0].Properties.ContainsKey("elapsed"));
    }

    [Fact]
    public void LevelShortcutsMap_ToCorrectNumericLevels()
    {
        var recorder = new InMemoryLog();
        recorder.Error("e");
        recorder.Info("i");
        recorder.Debug("d");
        recorder.Verbose("v");
        recorder.Trace("t");

        Assert.Equal((int)Level.Error, recorder.Entries[0].Level);
        Assert.Equal((int)Level.Info, recorder.Entries[1].Level);
        Assert.Equal((int)Level.Debug, recorder.Entries[2].Level);
        Assert.Equal((int)Level.Verbose, recorder.Entries[3].Level);
        Assert.Equal((int)Level.Trace, recorder.Entries[4].Level);
    }

    [Fact]
    public void NameOf_UsesTypeName()
    {
        var recorder = new InMemoryLog();
        recorder.NameOf<DeferredLogRouterTests>().Log(0, "m");

        Assert.Equal(nameof(DeferredLogRouterTests), recorder.Entries[0].Name);
    }
}
