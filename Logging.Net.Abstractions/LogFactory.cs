using System;
using Logging.Net.Abstractions;
using Logging.Net.Abstractions.Helpers;

namespace Logging.Net.Abstractions
{
    /// <summary>A static accessor to the default logging implementation. This allows programs to not have to pass down ILog along all code paths.</summary>
    /// <remarks>Passing down the ILog implementation is still recommended; it is required to be able to unit test logging aspects.</remarks>
    public class LogFactory
    {
        /// <summary>
        /// The actual accessor for logging implementation. This must be configured at the start of the program, which is usually done automatically when initializing an implementation (e.g. Logging.Net.Serilog).
        /// </summary>
        public static Func<ILog> LogImplementationAccessor;

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
