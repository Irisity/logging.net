namespace Logging.Net.Abstractions.Helpers
{
    public class Property<T> : IProperty
    {
        public Property(string key, T value, bool destructureObject)
        {
            this.key = key;
            this.value = value;
            this.destructureObject = destructureObject;
        }

        private string key;
        private T value;
        private bool destructureObject;

        public ILog Apply(ILog logImplementation)
        {
            return logImplementation.With(key, value, destructureObject);
        }
    }
}