namespace Logging.Net.Abstractions.Helpers
{
    public interface IProperty
    {
        ILog Apply(ILog logImplementation);
    }
}