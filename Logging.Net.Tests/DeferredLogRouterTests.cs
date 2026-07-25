using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Logging.Net.Abstractions;
using Logging.Net.Abstractions.Helpers;

namespace Logging.Net.Tests;

public class DeferredLogRouterTests : IDisposable
{
    public DeferredLogRouterTests()
    {
        Reset();
    }

    public void Dispose()
    {
        Reset();
    }

    private static void Reset()
    {
        LogFactory.LogImplementationAccessor = null;
        // Anything buffered before an accessor was set would otherwise replay into the next test.
        PreInitLogBuffer.Clear();
    }

    [Fact]
    public void Log_BeforeAccessorSet_IsBufferedAndReplayedOnInitialization()
    {
        var log = LogFactory.Name("Test").With("k", "v");
        log.Log(0, "logged-before-init");

        var recorder = new InMemoryLog();
        LogFactory.LogImplementationAccessor = () => recorder;

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal("logged-before-init", entry.Message);
        Assert.Equal("Test", entry.Name);
        Assert.Equal("v", entry.Properties["k"]);
    }

    [Fact]
    public void Log_BeforeAccessorSet_DropsOldestBeyondCapacity()
    {
        var log = LogFactory.Name("Test");
        for (int i = 0; i < PreInitLogBuffer.Capacity + 10; i++)
        {
            log.Log(0, "m" + i);
        }

        var recorder = new InMemoryLog();
        LogFactory.LogImplementationAccessor = () => recorder;

        var entries = recorder.Entries;
        Assert.Equal(PreInitLogBuffer.Capacity, entries.Count);
        Assert.Equal("m10", entries[0].Message); // the oldest ten were dropped
    }

    [Fact]
    public void Log_AfterAccessorSet_ReplaysName_Level_Properties_InOrder()
    {
        var recorder = new InMemoryLog();
        LogFactory.LogImplementationAccessor = () => recorder;

        var log = LogFactory.Name("Root").Name("Child").Level(2).With("k", "v");
        log.Log(1, "msg");

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal("Root/Child", entry.Name);
        Assert.Equal(3, entry.Level); // preset 2 + message 1 — NOT 1+1=2 (regression test for #1)
        Assert.True(entry.Properties.ContainsKey("k"));
        Assert.Equal("v", entry.Properties["k"]);
    }

    [Fact]
    public void Log_AppliesPresetLevel_Only_Once()
    {
        // Regression test for #1: the preset Level(2) must be applied once when the
        // deferred state is flushed, not once on flush AND once in the message call.
        var recorder = new InMemoryLog();
        LogFactory.LogImplementationAccessor = () => recorder;

        var log = LogFactory.Name("X").Level(5);
        log.Log(1, "one");

        Assert.Equal(6, recorder.Entries[0].Level); // 5 + 1, not 5 + 1 + 5.
    }

    [Fact]
    public async Task Log_ConcurrentFirstCall_AllCallersUseTheSameImplementation()
    {
        var created = new System.Collections.Concurrent.ConcurrentBag<InMemoryLog>();
        LogFactory.LogImplementationAccessor = () =>
        {
            var recorder = new InMemoryLog();
            created.Add(recorder);
            return recorder;
        };

        var log = LogFactory.Name("Race").Level(1);

        var tasks = new Task[32];
        using var barrier = new Barrier(tasks.Length);
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                barrier.SignalAndWait();
                log.Log(0, "go");
            });
        }
        await Task.WhenAll(tasks);

        // Losers of the race may each build their own implementation, but every caller must log
        // through the one that won the publish; otherwise entries scatter across instances that are
        // then discarded. So exactly one recorder holds anything, and it holds all 32 entries.
        var used = created.Where(r => r.Entries.Count > 0).ToList();
        Assert.Single(used);
        Assert.Equal(tasks.Length, used[0].Entries.Count);
    }

    [Fact]
    public void Log_AfterAccessorReplaced_ReresolvesAgainstTheNewImplementation()
    {
        // Regression test for the re-initialization bug: an ILog that already logged must not keep
        // writing into the implementation of a previous initialization.
        var first = new InMemoryLog();
        LogFactory.LogImplementationAccessor = () => first;

        var log = LogFactory.Name("Static").Level(1).With("k", "v");
        log.Log(0, "before");

        var second = new InMemoryLog();
        LogFactory.LogImplementationAccessor = () => second;
        log.Log(0, "after");

        Assert.Equal("before", Assert.Single(first.Entries).Message);
        var entry = Assert.Single(second.Entries);
        Assert.Equal("after", entry.Message);
        // The full configuration must be replayed onto the new implementation, not just the message.
        Assert.Equal("Static", entry.Name);
        Assert.Equal(1, entry.Level);
        Assert.Equal("v", entry.Properties["k"]);
    }

    [Fact]
    public void Log_WhenAccessorThrows_DoesNotThrowOutOfTheLoggingCall()
    {
        LogFactory.LogImplementationAccessor = () => throw new InvalidOperationException("half-initialized container");

        var log = LogFactory.Name("X");

        log.Log(0, "first");  // must not throw...
        log.Log(0, "second"); // ...and must not throw on every later call either
    }

    [Fact]
    public void Log_WhenAccessorReturnsNull_DoesNotThrow()
    {
        LogFactory.LogImplementationAccessor = () => null!;

        LogFactory.Name("X").Log(0, "m");
    }

    [Fact]
    public void Level_WithoutImplementation_Accumulates()
    {
        var recorder = new InMemoryLog();
        LogFactory.LogImplementationAccessor = () => recorder;

        var log = LogFactory.Name("X").Level(2).Level(3);
        log.Log(0, "m");

        Assert.Equal(5, recorder.Entries[0].Level);
    }

    [Fact]
    public void Name_AppendsHierarchically()
    {
        var recorder = new InMemoryLog();
        LogFactory.LogImplementationAccessor = () => recorder;

        var log = LogFactory.Name("A").Name("B").Name("C");
        log.Log(0, "m");

        Assert.Equal("A/B/C", recorder.Entries[0].Name);
    }

    [Fact]
    public void Name_IgnoresNullAndEmptySegments()
    {
        var recorder = new InMemoryLog();
        LogFactory.LogImplementationAccessor = () => recorder;

        var log = LogFactory.Name("A").Name(null!).Name("").Name("B");
        log.Log(0, "m");

        Assert.Equal("A/B", recorder.Entries[0].Name);
    }

    [Fact]
    public void With_DoesNotLeakPropertiesIntoSiblingLogs()
    {
        var recorder = new InMemoryLog();
        LogFactory.LogImplementationAccessor = () => recorder;

        var parent = LogFactory.Name("P");
        var a = parent.With("only-on-a", 1);
        var b = parent.With("only-on-b", 2);

        a.Log(0, "a");
        b.Log(0, "b");
        parent.Log(0, "p");

        var entries = recorder.Entries;
        Assert.True(entries.Single(e => e.Message == "a").Properties.ContainsKey("only-on-a"));
        Assert.False(entries.Single(e => e.Message == "a").Properties.ContainsKey("only-on-b"));
        Assert.True(entries.Single(e => e.Message == "b").Properties.ContainsKey("only-on-b"));
        Assert.Empty(entries.Single(e => e.Message == "p").Properties);
    }
}
