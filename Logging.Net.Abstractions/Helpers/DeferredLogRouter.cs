using System;
using System.Collections.Generic;
using System.Threading;

namespace Logging.Net.Abstractions.Helpers
{
    /// <summary>
    /// A helper class for static log instances, which may be created before the logging has actually been initialized.
    /// This class allows such static log instances to be defined and have their properties set, while deferring the needed calls to the actual ILog implementation until later.
    /// </summary>
    /// <remarks>
    /// The name, level and properties are always retained rather than being handed to the implementation
    /// as they are configured. That is what allows a router to rebuild itself against a new implementation
    /// when logging is re-initialized: an instance that bound to the implementation of a previous
    /// initialization would otherwise keep writing into a logger that has since been disposed.
    /// </remarks>
    internal class DeferredLogRouter : ILog
    {
        // The resolved implementation, together with the accessor it was resolved from. The two are
        // published as one immutable object so that a concurrent re-initialization can never leave a
        // reader pairing a new accessor with an implementation resolved from the old one.
        private Resolved resolved;

        // The configuration of this router, replayed onto the implementation whenever it is resolved.
        private readonly int level;
        private readonly List<string> names;
        private readonly List<IProperty> properties;

        internal DeferredLogRouter()
        {
        }

        private DeferredLogRouter(DeferredLogRouter log, int level, List<string> names, List<IProperty> properties)
        {
            this.level = log.level + level;
            this.names = names ?? log.names;
            this.properties = properties ?? log.properties;
        }

        public ILog Level(int level)
        {
            return new DeferredLogRouter(this, level, null, null);
        }

        ILog ILog.Name(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                // A null or empty segment would otherwise render as a dangling "Parent/" separator.
                return this;
            }
            return new DeferredLogRouter(this, 0, Append(this.names, name), null);
        }

        public ILog With<T>(string key, T value, bool destructureObjects = false)
        {
            var property = (IProperty)new Property<T>(key, value, destructureObjects);
            return new DeferredLogRouter(this, 0, null, Append(this.properties, property));
        }

        void ILog.Log(int level, string message, Exception exception)
        {
            var accessor = LogFactory.LogImplementationAccessor;
            if (accessor == null)
            {
                // Logging is not configured yet. Hold on to the event so that it is not lost;
                // it is replayed once an implementation is configured.
                PreInitLogBuffer.Add(this, level, message, exception);
                return;
            }

            var current = Volatile.Read(ref this.resolved);
            if (current == null || !ReferenceEquals(current.Accessor, accessor))
            {
                current = Resolve(accessor, current);
            }

            current.Log.Log(level, message, exception);
        }

        private Resolved Resolve(Func<ILog> accessor, Resolved stale)
        {
            ILog implementation;
            try
            {
                implementation = accessor() ?? new VoidLog();
                implementation = implementation.Level(this.level);
                if (this.names != null)
                {
                    foreach (var name in this.names)
                    {
                        implementation = implementation.Name(name);
                    }
                }
                if (this.properties != null)
                {
                    foreach (var property in this.properties)
                    {
                        implementation = property.Apply(implementation);
                    }
                }
            }
            catch (Exception)
            {
                // Deferred configuration is replayed far from where it was written, so a bad property
                // key or a half-initialized accessor would otherwise throw out of an unrelated logging
                // statement — and keep doing so on every later call, since nothing would be cached.
                implementation = new VoidLog();
            }

            var candidate = new Resolved(accessor, implementation);
            var previous = Interlocked.CompareExchange(ref this.resolved, candidate, stale);
            return ReferenceEquals(previous, stale) ? candidate : (previous ?? candidate);
        }

        private static List<T> Append<T>(List<T> existing, T item)
        {
            if (existing == null)
            {
                return new List<T> { item };
            }
            var copy = new List<T>(existing.Count + 1);
            copy.AddRange(existing);
            copy.Add(item);
            return copy;
        }

        private sealed class Resolved
        {
            public readonly Func<ILog> Accessor;
            public readonly ILog Log;

            public Resolved(Func<ILog> accessor, ILog log)
            {
                Accessor = accessor;
                Log = log;
            }
        }
    }
}
