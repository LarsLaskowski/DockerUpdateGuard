namespace DockerUpdateGuard.Images.Data;

/// <summary>
/// Reference-counted entry of a keyed scan lock
/// </summary>
internal sealed class KeyedScanLockEntry
{
    #region Properties

    /// <summary>
    /// Shared semaphore of the key
    /// </summary>
    public required SemaphoreSlim Semaphore { get; init; }

    /// <summary>
    /// Number of callers currently holding or waiting for the key
    /// </summary>
    public int ReferenceCount { get; set; }

    #endregion // Properties
}