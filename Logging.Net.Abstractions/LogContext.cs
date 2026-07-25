using System;

namespace Logging.Net.Abstractions
{
	/// <summary>
	/// Static accessor class for logging contexts
	/// </summary>
	public static class LogContext
	{
	    /// <summary>
	    /// The actual accessor for logging context. This must be configured at the start of the program for context properties to be usable, which is usually done automatically when initializing an implementation (e.g. Logging.Net.Serilog).
	    /// </summary>
	    public static Func<ILogContext> LogContextAccessor
	    {
	        get { return logContextAccessor; }
	        set { logContextAccessor = value; }
	    }

	    // Volatile so the accessor written by the initializing thread is visible to other threads.
	    private static volatile Func<ILogContext> logContextAccessor;

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
	        // Note that both the accessor and its result are checked: unlike a dropped log entry, an
	        // exception here would abort the caller's own work inside the using block.
	        var accessor = LogContextAccessor;
	        var context = accessor == null ? null : accessor();
	        return context == null
	            ? new Helpers.DisposableAction(() => { })
	            : context.With(key, value, destructureObjects);
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
}
