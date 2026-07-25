using System;
using Logging.Net.Abstractions;
using Logging.Net.Abstractions.Helpers;

namespace Logging.Net.Abstractions
{
    /// <summary>A static accessor to the default logging implementation. This allows programs to not have to pass down ILog along all code paths.</summary>
    /// <remarks>Passing down the ILog implementation is still recommended; it is required to be able to unit test logging aspects.</remarks>
    public static class LogFactory
    {
        // Volatile so that the accessor written by the initializing thread is visible to logging
        // threads without further synchronization, and so that log instances can detect a
        // re-initialization by comparing the delegate reference they resolved against.
        private static volatile Func<ILog> logImplementationAccessor;

        /// <summary>
        /// The actual accessor for logging implementation. This must be configured at the start of the program, which is usually done automatically when initializing an implementation (e.g. Logging.Net.Serilog).
        /// </summary>
        /// <remarks>
        /// Assigning a new accessor invalidates the implementation cached by every existing ILog instance;
        /// they re-resolve against the new accessor on their next log call. Implementations must therefore
        /// hand out a distinct delegate instance on every (re-)initialization.
        /// Assigning a non-null accessor also replays anything that was logged before logging was configured.
        /// </remarks>
        public static Func<ILog> LogImplementationAccessor
        {
            get { return logImplementationAccessor; }
            set
            {
                logImplementationAccessor = value;
                if (value != null)
                {
                    PreInitLogBuffer.Replay();
                }
            }
        }

        /// <summary>
        /// Creates a new ILog object with the specified name.
        /// </summary>
        public static ILog Name(string name)
        {
            return ((ILog)new DeferredLogRouter()).Name(name);
        }

		/// <summary>
		/// Creates a new ILog object with the name taken from the specified type.
		/// </summary>
		public static ILog NameOf<T>()
        {
            return new DeferredLogRouter().NameOf<T>();
        }
    }
}
