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

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    #endregion // IDisposable
}