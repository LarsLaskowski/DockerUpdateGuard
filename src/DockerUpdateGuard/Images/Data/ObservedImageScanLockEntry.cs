namespace DockerUpdateGuard.Images.Data;

/// <summary>
/// Reference-counted scan lock entry for an observed image
/// </summary>
internal sealed class ObservedImageScanLockEntry
{
    #region Properties

    /// <summary>
    /// Shared scan lock
    /// </summary>
    public required SemaphoreSlim Semaphore { get; init; }

    /// <summary>
    /// Number of callers currently holding or waiting for the lock
    /// </summary>
    public int ReferenceCount { get; set; }

    #endregion // Properties
}