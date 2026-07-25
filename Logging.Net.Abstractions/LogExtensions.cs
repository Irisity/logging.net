using System;
using System.Diagnostics;

namespace Logging.Net.Abstractions
{
	public static class LogExtensions
	{
	    /// <summary>
	    /// Log a message with the Error level.
	    /// </summary>
	    public static void Error(this ILog log, string message, Exception e = null)
	    {
	        log.Log((int)Abstractions.Level.Error, message, e);
	    }

	    /// <summary>
	    /// Log a message with the Info level.
	    /// </summary>
	    public static void Info(this ILog log, string message, Exception e = null)
	    {
	        log.Log((int)Abstractions.Level.Info, message, e);
	    }

	    /// <summary>
	    /// Log a message with the Debug level.
	    /// </summary>
	    public static void Debug(this ILog log, string message, Exception e = null)
	    {
	        log.Log((int)Abstractions.Level.Debug, message, e);
	    }

	    /// <summary>
	    /// Log a message with the Verbose level.
	    /// </summary>
	    public static void Verbose(this ILog log, string message, Exception e = null)
	    {
	        log.Log((int)Abstractions.Level.Verbose, message, e);
	    }

	    /// <summary>
	    /// Log a message with the Trace level.
	    /// </summary>
	    public static void Trace(this ILog log, string message, Exception e = null)
	    {
	        log.Log((int)Abstractions.Level.Trace, message, e);
	    }

	    /// <summary>
	    /// Returns an ILog with a pre-configured Level.
	    /// </summary>
	    public static ILog Level(this ILog log, Level level)
	    {
	        return log.Level((int)level);
	    }

	    /// <summary>
	    /// Returns an ILog with the name set to the simple (namespace-less) name of the type.
	    /// </summary>
	    /// <remarks>
	    /// The namespace is deliberately left out to keep log names short; combine names with
	    /// <see cref="ILog.Name"/> when two types with the same simple name need to be told apart.
	    /// For generic types the arity suffix is stripped, so List&lt;T&gt; is named "List" rather than "List`1".
	    /// </remarks>
	    public static ILog NameOf<T>(this ILog log)
	    {
	        var name = typeof(T).Name;
	        var arity = name.IndexOf('`');
	        return log.Name(arity < 0 ? name : name.Substring(0, arity));
	    }

	    /// <summary>
	    /// Time an operation and log the elapsed time in seconds using "elapsed" property.
	    /// </summary>
	    /// <param name="log">Which log to append to.</param>
	    /// <param name="message">The message to append.</param>
	    /// <returns>An IDisposable which causes the timing to end and logging to happen when its Dispose it called.</returns>
	    /// <remarks>The logging level will be 0 (=Info), meaning that you can use the Level method to set an actual level before calling this extension method.</remarks>
	    /// <example>
	    /// using (LogContext.With("path", filePath))               // Add the property to logging context so it's included also if there is an exception thrown
	    /// using (this.log.Level(Level.Verbose).Time("Read file")) // Start timing the file reading, output "Read file" at Verbose level when done.
	    /// {
	    ///     ... Do the actual file reading
	    /// }
	    /// </example>
	    public static IDisposable Time(this ILog log, string message)
	    {
	        var sw = new Stopwatch();
	        sw.Start();
	        return new Helpers.DisposableAction(() =>
	        {
	            sw.Stop();
	            log.With("elapsed", sw.Elapsed.TotalSeconds).Log(0, message);
	        });
	    }

		public static TResult TimeFunc<TResult>(this ILog log, string message, Func<TResult> func)
		{
			var sw = Stopwatch.StartNew();
			try
			{
				return func();
			}
			finally
			{
				sw.Stop();
				log.With("elapsed", sw.Elapsed.TotalSeconds).Log(0, message);
			}
		}
	}
}
