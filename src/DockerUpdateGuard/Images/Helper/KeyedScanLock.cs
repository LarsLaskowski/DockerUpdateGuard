using DockerUpdateGuard.Images.Data;

namespace DockerUpdateGuard.Images.Helper;

/// <summary>
/// Registry of per-key scan locks shared across scopes, each entry retained only while it is held or awaited
/// </summary>
internal sealed class KeyedScanLock
{
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
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Get the number of holders plus waiters of a key
    /// </summary>
    /// <param name="key">Key</param>
    /// <returns>Reference count, 0 without an entry</returns>
    public int GetReferenceCount(Guid key)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(key, out var entry) ? entry.ReferenceCount : 0;
        }
    }

    /// <summary>
    /// Wait until the key is free and take it
    /// </summary>
    /// <param name="key">Key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    public async Task WaitAsync(Guid key, CancellationToken cancellationToken)
    {
        var semaphore = AcquireReference(key);

        try
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            DropReference(key);

            throw;
        }
    }

    /// <summary>
    /// Release the key
    /// </summary>
    /// <param name="key">Key</param>
    public void Release(Guid key)
    {
        SemaphoreSlim semaphore;

        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var entry) == false)
            {
                throw new InvalidOperationException("The scan lock key is not held");
            }

            semaphore = entry.Semaphore;
        }

        semaphore.Release();

        DropReference(key);
    }

    /// <summary>
    /// Take a reference to the entry of a key, creating it on first use
    /// </summary>
    /// <param name="key">Key</param>
    /// <returns>Semaphore of the key</returns>
    private SemaphoreSlim AcquireReference(Guid key)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var entry) == false)
            {
                entry = new KeyedScanLockEntry
                        {
                            Semaphore = new SemaphoreSlim(1, 1),
                        };

                _entries[key] = entry;
            }

            entry.ReferenceCount++;

            return entry.Semaphore;
        }
    }

    /// <summary>
    /// Drop a reference to the entry of a key, removing and disposing it at zero
    /// </summary>
    /// <param name="key">Key</param>
    private void DropReference(Guid key)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var entry) == false)
            {
                return;
            }

            entry.ReferenceCount--;

            if (entry.ReferenceCount <= 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    #endregion // Methods
}