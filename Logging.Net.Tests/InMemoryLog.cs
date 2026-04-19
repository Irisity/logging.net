using System;
using System.Collections.Generic;
using Logging.Net.Abstractions;

namespace Logging.Net.Tests;

internal sealed record LogEntry(int Level, string Message, Exception? Exception, string? Name, IReadOnlyDictionary<string, object?> Properties);

internal sealed class InMemoryLog : ILog
{
    public readonly List<LogEntry> Entries;

    private readonly int accumulatedLevel;
    private readonly string? name;
    private readonly Dictionary<string, object?> properties;

    public InMemoryLog() : this(new List<LogEntry>(), 0, null, new Dictionary<string, object?>()) { }

    private InMemoryLog(List<LogEntry> entries, int accumulatedLevel, string? name, Dictionary<string, object?> properties)
    {
        this.Entries = entries;
        this.accumulatedLevel = accumulatedLevel;
        this.name = name;
        this.properties = properties;
    }

    public ILog Name(string name)
    {
        var newName = this.name == null ? name : this.name + "/" + name;
        return new InMemoryLog(Entries, accumulatedLevel, newName, new Dictionary<string, object?>(properties));
    }

    public ILog With<T>(string key, T value, bool destructureObjects = false)
    {
        var newProps = new Dictionary<string, object?>(properties) { [key] = value };
        return new InMemoryLog(Entries, accumulatedLevel, name, newProps);
    }

    public ILog Level(int level)
    {
        return new InMemoryLog(Entries, accumulatedLevel + level, name, new Dictionary<string, object?>(properties));
    }

    public void Log(int level, string message, Exception? exception = null)
    {
        Entries.Add(new LogEntry(accumulatedLevel + level, message, exception, name, new Dictionary<string, object?>(properties)));
    }
}
