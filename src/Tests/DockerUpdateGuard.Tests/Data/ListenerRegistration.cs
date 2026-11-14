namespace DockerUpdateGuard.Tests.Data;

/// <summary>
/// Monitor subscription that runs an action once when it is disposed
/// </summary>
internal sealed class ListenerRegistration : IDisposable
{
    #region Fields

    /// <summary>
    /// Action that removes the listener
    /// </summary>
    private readonly Action _onDispose;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="onDispose">Action that removes the listener</param>
    public ListenerRegistration(Action onDispose)
    {
        _onDispose = onDispose;
    }

    #endregion // Constructors

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        _onDispose();
    }

    #endregion // IDisposable
}