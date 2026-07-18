namespace DockerUpdateGuard.Tests.Data;

/// <summary>
/// Empty disposable for monitor subscriptions
/// </summary>
internal sealed class NullDisposable : IDisposable
{
    #region Properties

    /// <summary>
    /// Shared instance
    /// </summary>
    public static NullDisposable Instance { get; } = new();

    #endregion // Properties

    #region IDisposable

    /// <summary>
    /// Release resources
    /// </summary>
    public void Dispose()
    {
    }

    #endregion // IDisposable
}