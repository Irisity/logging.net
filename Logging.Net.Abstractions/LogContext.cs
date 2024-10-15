namespace Logging.Net.Abstractions;

/// <summary>
/// Static accessor class for logging contexts
/// </summary>
public static class LogContext
{
    /// <summary>
    /// The actual accessor for logging context. This must be configured at the start of the program for context properties to be usable.
    /// </summary>
    public static Func<ILogContext>? LogContextAccessor;

    /// <summary>
    /// Add a key-value property to the logging context, to be included in all logging that will happen in the current async control flow, until the returned IDisposable is disposed.
    /// </summary>
    /// <remarks>Use the log context to ensure that properties are logged also when an execption is thrown and later caught further up in the call stack.</remarks>
    /// <param name="key">The key of the property</param>
    /// <param name="value">The value of the property</param>
    /// <param name="destructureObjects">Controls how the value will be logged. By default, values will be converted to strings using their ToString method. If destructureObjects is true, the value passed in will be serialized instead, effectively making each public field/property in it a value of its own.</param>
    /// <example>
    /// using (LogContext.With("path", filePath))  // Add the property to logging context so it's included also if there is an exception thrown
    /// {
    ///     ...
    ///     this.log.Info("Done something with file"); // This will include the "path" property
    ///     ... 
    ///     // If an exception is thrown out of this using block, and that exception is logged by a calling method, it will also include the "path" property.
    /// }
    /// </example>
    public static IDisposable With<T>(string key, T value, bool destructureObjects = false)
    {
        return LogContextAccessor?.Invoke().With(key, value, destructureObjects) 
            ?? new Helpers.DisposableAction(() => { });
    }
}

/// <summary>
/// LogContext implementation abstraction
/// </summary>
public interface ILogContext
{
    /// <summary>
    /// Add a key-value property to the logging context, to be included in all logging that will happen in the current async control flow, until the returned IDisposable is disposed.
    /// </summary>
    /// <param name="key">The key of the property</param>
    /// <param name="value">The value of the property</param>
    /// <param name="destructureObjects">Controls how the value will be logged. By default, values will be converted to strings using their ToString method. If destructureObjects is true, the value passed in will be serialized instead, effectively making each public field/property in it a value of its own.</param>
    IDisposable With<T>(string key, T value, bool destructureObjects);
}
