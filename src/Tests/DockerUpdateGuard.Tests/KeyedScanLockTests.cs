using DockerUpdateGuard.Images.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="KeyedScanLock"/>
/// </summary>
[TestClass]
public class KeyedScanLockTests
{
    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify releasing the last reference of a key removes its entry
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    public async Task KeyedScanLockReleaseOfLastReferenceRemovesEntryAsync()
    {
        var scanLock = new KeyedScanLock();
        var key = Guid.NewGuid();

        await scanLock.WaitAsync(key, TestContext.CancellationToken)
                      .ConfigureAwait(false);

        Assert.AreEqual(1, scanLock.GetReferenceCount(key), "A held key must have exactly one reference");
        Assert.AreEqual(1, scanLock.Count, "A held key must have an entry");

        scanLock.Release(key);

        Assert.AreEqual(0, scanLock.GetReferenceCount(key), "A released key must have no reference");
        Assert.AreEqual(0, scanLock.Count, "The entry must be removed once its last reference is released");
    }

    /// <summary>
    /// Verify a cancelled wait drops its reference and leaves the holder's entry in place
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    public async Task KeyedScanLockCancelledWaitRemovesEntryAsync()
    {
        var scanLock = new KeyedScanLock();
        var key = Guid.NewGuid();

        await scanLock.WaitAsync(key, TestContext.CancellationToken)
                      .ConfigureAwait(false);

        using (var waitCancellation = new CancellationTokenSource())
        {
            var waiter = scanLock.WaitAsync(key, waitCancellation.Token);

            Assert.AreEqual(2, scanLock.GetReferenceCount(key), "A waiter must hold a reference next to the holder");

            await waitCancellation.CancelAsync()
                                  .ConfigureAwait(false);
            await Assert.ThrowsAsync<OperationCanceledException>(() => waiter, "A cancelled wait must end with an OperationCanceledException");
        }

        Assert.AreEqual(1, scanLock.GetReferenceCount(key), "A cancelled wait must drop its own reference only");

        scanLock.Release(key);

        Assert.AreEqual(0, scanLock.GetReferenceCount(key), "The key must have no reference after the holder released it");
        Assert.AreEqual(0, scanLock.Count, "No entry may remain after a cancelled wait and the release of the holder");
    }

    /// <summary>
    /// Verify a second waiter of one key waits until the holder releases it
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    public async Task KeyedScanLockWaitAsyncSecondWaiterWaitsForReleaseAsync()
    {
        var scanLock = new KeyedScanLock();
        var key = Guid.NewGuid();

        await scanLock.WaitAsync(key, TestContext.CancellationToken)
                      .ConfigureAwait(false);

        var waiter = scanLock.WaitAsync(key, TestContext.CancellationToken);

        Assert.IsFalse(waiter.IsCompleted, "A second waiter must not enter while the key is held");
        Assert.AreEqual(2, scanLock.GetReferenceCount(key), "The holder and the waiter must each hold a reference");

        scanLock.Release(key);

        await waiter.WaitAsync(TestContext.CancellationToken)
                    .ConfigureAwait(false);

        Assert.AreEqual(1, scanLock.GetReferenceCount(key), "The waiter that entered must be the only reference left");

        scanLock.Release(key);

        Assert.AreEqual(0, scanLock.Count, "No entry may remain once the last waiter released the key");
    }

    /// <summary>
    /// Verify two different keys do not wait for each other
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC7")]
    public async Task KeyedScanLockWaitAsyncDifferentKeysDoNotWaitAsync()
    {
        var scanLock = new KeyedScanLock();
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();

        await scanLock.WaitAsync(firstKey, TestContext.CancellationToken)
                      .ConfigureAwait(false);

        var secondWait = scanLock.WaitAsync(secondKey, TestContext.CancellationToken);

        await secondWait.WaitAsync(TestContext.CancellationToken)
                        .ConfigureAwait(false);

        Assert.AreEqual(2, scanLock.Count, "Each held key must have its own entry");

        scanLock.Release(firstKey);
        scanLock.Release(secondKey);

        Assert.AreEqual(0, scanLock.Count, "No entry may remain once both keys are released");
    }

    /// <summary>
    /// Verify releasing a key without an entry is rejected
    /// </summary>
    [TestMethod]
    [TestCategory("AC7")]
    public void KeyedScanLockReleaseWithoutEntryThrowsInvalidOperationException()
    {
        var scanLock = new KeyedScanLock();

        Assert.ThrowsExactly<InvalidOperationException>(() => scanLock.Release(Guid.NewGuid()), "Releasing a key that is neither held nor awaited must be rejected");
        Assert.AreEqual(0, scanLock.Count, "A rejected release must not create an entry");
    }

    #endregion // Methods
}