namespace DockerUpdateGuard.Tests.Data;

/// <summary>
/// Empty logging scope
/// </summary>
internal sealed class NullScope : IDisposable
{
    #region Properties

    /// <summary>
    /// Shared instance
    /// </summary>
    public static NullScope Instance { get; } = new();

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