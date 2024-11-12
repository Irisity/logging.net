using System;
using System.Collections.Generic;

namespace Logging.Net.Abstractions.Helpers
{
    /// <summary>
    /// A helper class for static log instances, which may be created before the logging has actually been initialized.
    /// This class allows such static log instances to be defined and have their properties set, while deferring the needed calls to the actual ILog implementation until later.
    /// </summary>
    internal class DeferredLogRouter : ILog
    {
        // The actual log implementation.
        // The initalization of this field is delayed to allow this class to be created before an implementation has been selected by setting Log.LogImplementationAccessor;
        private ILog logImplementation;

        // In case the log implementation has not been initialized yet, we store the details until it becomes available
        private int level;
        private List<string> names;
        private List<IProperty> properties;

        internal DeferredLogRouter()
        {
        }

        private DeferredLogRouter(DeferredLogRouter log)
        {
            level = log.level;
            names = log.names;
            properties = log.properties;
            logImplementation = log.logImplementation;
        }

        public ILog Level(int level)
        {
            var copy = new DeferredLogRouter(this);
            if (copy.logImplementation != null)
            {
                copy.logImplementation = copy.logImplementation.Level(level);
            }
            else
            {
                copy.level += level;
            }
            return copy;
        }

        ILog ILog.Name(string name)
        {
            var copy = new DeferredLogRouter(this);
            if (copy.logImplementation != null)
            {
                copy.logImplementation = copy.logImplementation.Name(name);
            }
            else
            {
                if (copy.names == null)
                {
                    copy.names = new List<string> { name };
                }
                else
                {
                    copy.names = new List<string>(names.Count + 1);
                    copy.names.AddRange(names);
                    copy.names.Add(name);
                }
            }
            return copy;
        }

        public ILog With<T>(string key, T value, bool destructureObjects = false)
        {
            var copy = new DeferredLogRouter(this);
            if (copy.logImplementation != null)
            {
                copy.logImplementation = copy.logImplementation.With(key, value, destructureObjects);
            }
            else
            {
                var property = new Property<T>(key, value, destructureObjects);

                if (copy.properties == null)
                {
                    copy.properties = new List<IProperty> { property };
                }
                else
                {
                    copy.properties = new List<IProperty>(properties.Count + 1);
                    copy.properties.AddRange(properties);
                    copy.properties.Add(property);
                }
            }
            return copy;
        }

        void ILog.Log(int level, string message, Exception exception)
        {
            if (logImplementation == null)
            {
                if (LogFactory.LogImplementationAccessor != null)
                {
                    logImplementation = LogFactory.LogImplementationAccessor();
                    if (this.level != 0)
                    {
                        logImplementation = logImplementation.Level(level);
                    }
                    if (names != null)
                    {
                        foreach (var name in names)
                        {
                            logImplementation = logImplementation.Name(name);
                        }
                    }
                    if (properties != null)
                    {
                        foreach (var property in properties)
                        {
                            logImplementation = property.Apply(logImplementation);
                        }
                    }
                }
            }

            if (logImplementation != null)
            {
                logImplementation.Log(level, message, exception);
            }
        }
    }
}
