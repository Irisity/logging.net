namespace Logging.Net.Abstractions;

public interface ILog
{
    /// <summary>
    /// Returns an ILog with the name set to the combination of an existing name (if any) and the passed-in name, separated by a slash character.
    /// </summary>
    ILog Name(string name);

    /// <summary>
    /// Returns an ILog that will include the specified key-value property in all logs.
    /// </summary>
    /// <param name="key">The key of the property</param>
    /// <param name="value">The value of the property</param>
    /// <param name="destructureObjects">Controls how the value will be logged. By default, values will be converted to strings using their ToString method. If destructureObjects is true, the value passed in will be serialized instead, effectively making each public field/property in it a value of its own.</param>
    ILog With<T>(string key, T value, bool destructureObjects = false);

    /// <summary>
    /// Returns an ILog that has a pre-configured level.
    /// </summary>
    /// <remarks>Note that levels are additive. So if an ILog have a Level set to 1, and Log method is called with level 2, the actual resulting level will be 3.</remarks>
    ILog Level(int level);

    /// <summary>
    /// Logs a message with the specified level.
    /// </summary>
    /// <param name="level">The level to add to the configured level of this ILog</param>
    /// <param name="exception">An optional exception</param>
    void Log(int level, string message, Exception? exception = null);
}
