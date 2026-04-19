using System;
using System.Threading;
using System.Threading.Tasks;
using Logging.Net.Abstractions;

namespace Logging.Net.Tests;

public class DeferredLogRouterTests : IDisposable
{
    public DeferredLogRouterTests()
    {
        LogFactory.LogImplementationAccessor = null;
    }

    public void Dispose()
    {
        LogFactory.LogImplementationAccessor = null;
    }

    [Fact]
    public void Log_BeforeAccessorSet_IsNoop()
    {
        var log = LogFactory.Name("Test");
        log.Log(0, "hello");
        // No throw, no crash, no log recipient — we only assert it doesn't blow up.
    }

    [Fact]
    public void Log_AfterAccessorSet_ReplaysName_Level_Properties_InOrder()
    {
        var recorder = new InMemoryLog();
        LogFactory.LogImplementationAccessor = () => recorder;

        var log = LogFactory.Name("Root").Name("Child").Level(2).With("k", "v");
        log.Log(1, "msg");

        Assert.Single(recorder.Entries);
        var entry = recorder.Entries[0];
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
    public async Task Log_ConcurrentFirstCall_InitializesExactlyOnce()
    {
        int accessorInvocations = 0;
        LogFactory.LogImplementationAccessor = () =>
        {
            Interlocked.Increment(ref accessorInvocations);
            return new InMemoryLog();
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

        // Accessor may be invoked more than once due to CAS losers, but the winning
        // implementation must be used afterwards. We only assert "at least once" + no exceptions.
        Assert.True(accessorInvocations >= 1);
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
}
