namespace Logging.Net.Abstractions.Helpers
{
    internal class Property<T> : IProperty
    {
        private readonly string key;
        private readonly T value;
        private readonly bool destructureObject;

        public Property(string key, T value, bool destructureObject)
        {
            this.key = key;
            this.value = value;
            this.destructureObject = destructureObject;
        }

        public ILog Apply(ILog logImplementation)
        {
            return logImplementation.With(key, value, destructureObject);
        }
    }
}
