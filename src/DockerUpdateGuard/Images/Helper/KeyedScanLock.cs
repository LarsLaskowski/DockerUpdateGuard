using DockerUpdateGuard.Images.Data;

namespace DockerUpdateGuard.Images.Helper;

/// <summary>
/// Registry of per-key scan locks shared across scopes, each entry retained only while it is held or awaited
/// </summary>
internal sealed class KeyedScanLock
{
    #region Const fields

    /// <summary>
    /// Message of the skeleton bodies
    /// </summary>
    private const string SkeletonMessage = "Skeleton: not implemented";

    #endregion // Const fields

    // Skeleton: removed with the implementation
#pragma warning disable S1144, S2325, CA1822

    #region Fields

    /// <summary>
    /// Synchronization root guarding the entry bookkeeping
    /// </summary>
    private readonly Lock _sync = new();

    /// <summary>
    /// Entries of the keys currently held or awaited
    /// </summary>
    private readonly Dictionary<Guid, KeyedScanLockEntry> _entries = [];

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Number of keys held or awaited
    /// </summary>
    public int Count => throw new InvalidOperationException(SkeletonMessage);

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Get the number of holders plus waiters of a key
    /// </summary>
    /// <param name="key">Key</param>
    /// <returns>Reference count, 0 without an entry</returns>
    public int GetReferenceCount(Guid key)
    {
        throw new InvalidOperationException(SkeletonMessage);
    }

    /// <summary>
    /// Wait until the key is free and take it
    /// </summary>
    /// <param name="key">Key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    public Task WaitAsync(Guid key, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(SkeletonMessage);
    }

    /// <summary>
    /// Release the key
    /// </summary>
    /// <param name="key">Key</param>
    public void Release(Guid key)
    {
        throw new InvalidOperationException(SkeletonMessage);
    }

    #endregion // Methods

#pragma warning restore S1144, S2325, CA1822
}