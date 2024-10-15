namespace Logging.Net.Abstractions.Helpers;

/// <summary>
/// Helper class for Time method.
/// </summary>
internal class DisposableAction(Action action) : IDisposable
{
    public void Dispose()
    {
        action();
    }
}
