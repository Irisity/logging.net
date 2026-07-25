namespace Logging.Net.Abstractions.Helpers
{
    internal interface IProperty
    {
        ILog Apply(ILog logImplementation);
    }
}
