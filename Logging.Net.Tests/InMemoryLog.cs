using System;
using System.Collections.Generic;
using System.Linq;
using Logging.Net.Abstractions;

namespace Logging.Net.Tests;

internal sealed record LogEntry(int Level, string Message, Exception? Exception, string? Name, IReadOnlyDictionary<string, object?> Properties);

/// <summary>
/// An ILog that records what was logged. Derived instances share the recording list, which is
/// guarded by a lock because the concurrency tests write to it from many threads at once —
/// an unsynchronized List drops entries under contention and can throw while resizing.
/// </summary>
internal sealed class InMemoryLog : ILog
{
    private readonly List<LogEntry> entries;
    private readonly object gate;

    private readonly int accumulatedLevel;
    private readonly string? name;
    private readonly Dictionary<string, object?> properties;

    public InMemoryLog() : this(new List<LogEntry>(), new object(), 0, null, new Dictionary<string, object?>()) { }

    private InMemoryLog(List<LogEntry> entries, object gate, int accumulatedLevel, string? name, Dictionary<string, object?> properties)
    {
        this.entries = entries;
        this.gate = gate;
        this.accumulatedLevel = accumulatedLevel;
        this.name = name;
        this.properties = properties;
    }

    public IReadOnlyList<LogEntry> Entries
    {
        get { lock (gate) { return entries.ToList(); } }
    }

    public ILog Name(string name)
    {
        var newName = this.name == null ? name : this.name + "/" + name;
        return new InMemoryLog(entries, gate, accumulatedLevel, newName, new Dictionary<string, object?>(properties));
    }

    public ILog With<T>(string key, T value, bool destructureObjects = false)
    {
        var newProps = new Dictionary<string, object?>(properties) { [key] = value };
        return new InMemoryLog(entries, gate, accumulatedLevel, name, newProps);
    }

    public ILog Level(int level)
    {
        return new InMemoryLog(entries, gate, accumulatedLevel + level, name, new Dictionary<string, object?>(properties));
    }

    public void Log(int level, string message, Exception? exception = null)
    {
        var entry = new LogEntry(accumulatedLevel + level, message, exception, name, new Dictionary<string, object?>(properties));
        lock (gate)
        {
            entries.Add(entry);
        }
    }
}
