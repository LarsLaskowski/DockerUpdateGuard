using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Tests.Data;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DockerHubRequestLimiter"/>
/// </summary>
[TestClass]
public class DockerHubRequestLimiterTests
{
    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify concurrent waits are admitted only up to the limit and one more per release
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task DockerHubRequestLimiterConcurrentWaitsNeverExceedLimitAsync()
    {
        var monitor = CreateMonitor(2);

        using var limiter = new DockerHubRequestLimiter(monitor);

        var waits = Enumerable.Range(0, 5)
                              .Select(_ => limiter.WaitAsync(TestContext.CancellationToken))
                              .ToArray();

        Assert.AreEqual(2, waits.Count(wait => wait.IsCompleted), "Exactly the limit of waits must be admitted at once");

        limiter.Release();

        Assert.AreEqual(3, waits.Count(wait => wait.IsCompleted), "One release must admit exactly one more wait");

        await Task.WhenAll(waits.Where(wait => wait.IsCompleted)).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify raising the limit through a reload admits the waiting requests up to the new limit without a release
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task DockerHubRequestLimiterLimitRaisedOnReloadAdmitsWaitingRequestsAsync()
    {
        var monitor = CreateMonitor(1);

        using var limiter = new DockerHubRequestLimiter(monitor);

        var held = limiter.WaitAsync(TestContext.CancellationToken);
        var firstWaiting = limiter.WaitAsync(TestContext.CancellationToken);
        var secondWaiting = limiter.WaitAsync(TestContext.CancellationToken);

        Assert.IsTrue(held.IsCompleted, "The first request takes the only slot");
        Assert.IsFalse(firstWaiting.IsCompleted || secondWaiting.IsCompleted, "The requests beyond the limit must wait");

        monitor.Reload(CreateOptions(3));

        Assert.IsTrue(firstWaiting.IsCompleted, "The first waiting request must be admitted by the raised limit without a release");
        Assert.IsTrue(secondWaiting.IsCompleted, "The second waiting request must be admitted by the raised limit without a release");

        await Task.WhenAll(held, firstWaiting, secondWaiting).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify lowering the limit through a reload admits nothing until the in-flight count is below the new limit
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task DockerHubRequestLimiterLimitLoweredOnReloadAdmitsNoNewRequestUntilBelowLimitAsync()
    {
        var monitor = CreateMonitor(2);

        using var limiter = new DockerHubRequestLimiter(monitor);

        var firstHeld = limiter.WaitAsync(TestContext.CancellationToken);
        var secondHeld = limiter.WaitAsync(TestContext.CancellationToken);
        var waiting = limiter.WaitAsync(TestContext.CancellationToken);

        monitor.Reload(CreateOptions(1));

        Assert.IsTrue(firstHeld.IsCompleted && secondHeld.IsCompleted, "The requests in flight keep their slots after the limit is lowered");
        Assert.IsFalse(waiting.IsCompleted, "A lowered limit must not admit a waiting request");

        limiter.Release();

        Assert.IsFalse(waiting.IsCompleted, "One request still in flight equals the lowered limit, so nothing may be admitted yet");

        limiter.Release();

        Assert.IsTrue(waiting.IsCompleted, "The waiting request must be admitted once the count is below the lowered limit");

        await Task.WhenAll(firstHeld, secondHeld, waiting).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify a queued wait that is cancelled throws and takes no slot
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task DockerHubRequestLimiterCancelledWaitLeaksNoSlotAsync()
    {
        using var limiter = new DockerHubRequestLimiter(CreateMonitor(1));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

        var held = limiter.WaitAsync(TestContext.CancellationToken);
        var queued = limiter.WaitAsync(cancellation.Token);

        Assert.IsTrue(held.IsCompleted, "The first request takes the only slot");
        Assert.IsFalse(queued.IsCompleted, "The second request must queue behind the held slot");

        await cancellation.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAsync<OperationCanceledException>(() => queued, "A cancelled queued wait must throw OperationCanceledException")
                    .ConfigureAwait(false);

        limiter.Release();

        var next = limiter.WaitAsync(TestContext.CancellationToken);
        var afterNext = limiter.WaitAsync(TestContext.CancellationToken);

        Assert.IsTrue(next.IsCompleted, "The cancelled wait must not have taken the released slot");
        Assert.IsFalse(afterNext.IsCompleted, "Only one slot exists, so the following wait must queue");
    }

    /// <summary>
    /// Verify a wait with an already cancelled token throws even when a slot is free and takes nothing
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task DockerHubRequestLimiterPreCancelledWaitTakesNoFreeSlotAsync()
    {
        using var limiter = new DockerHubRequestLimiter(CreateMonitor(1));
        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAsync<OperationCanceledException>(() => limiter.WaitAsync(cancellation.Token), "An already cancelled token must throw OperationCanceledException")
                    .ConfigureAwait(false);

        var next = limiter.WaitAsync(TestContext.CancellationToken);

        Assert.IsTrue(next.IsCompleted, "The free slot must still be free after the cancelled wait");
    }

    /// <summary>
    /// Verify a queued wait that was granted keeps its slot when its token is cancelled afterwards
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task DockerHubRequestLimiterTokenCancelledAfterGrantKeepsSlotAsync()
    {
        using var limiter = new DockerHubRequestLimiter(CreateMonitor(1));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

        var held = limiter.WaitAsync(TestContext.CancellationToken);
        var queued = limiter.WaitAsync(cancellation.Token);

        limiter.Release();

        Assert.IsTrue(held.IsCompleted, "The first request takes the only slot");
        Assert.IsTrue(queued.IsCompletedSuccessfully, "Releasing the held slot must grant the queued wait");

        await cancellation.CancelAsync().ConfigureAwait(false);

        var next = limiter.WaitAsync(TestContext.CancellationToken);

        Assert.IsFalse(next.IsCompleted, "A granted wait keeps its slot when its token is cancelled afterwards");

        limiter.Release();

        Assert.IsTrue(next.IsCompleted, "Releasing the kept slot must admit the next wait");
    }

    /// <summary>
    /// Verify a release without a taken slot is rejected and leaves the slot count unchanged
    /// </summary>
    [TestMethod]
    public void DockerHubRequestLimiterReleaseWithoutTakenSlotThrows()
    {
        using var limiter = new DockerHubRequestLimiter(CreateMonitor(1));

        Assert.ThrowsExactly<InvalidOperationException>(limiter.Release, "A release with no slot taken must be rejected");

        var first = limiter.WaitAsync(TestContext.CancellationToken);
        var second = limiter.WaitAsync(TestContext.CancellationToken);

        Assert.IsTrue(first.IsCompleted, "The rejected release must not have created a free slot");
        Assert.IsFalse(second.IsCompleted, "The rejected release must not have raised the slot count above the limit");
    }

    /// <summary>
    /// Verify a disposed limiter no longer follows configuration reloads
    /// </summary>
    [TestMethod]
    public void DockerHubRequestLimiterDisposedLimiterIgnoresReload()
    {
        var monitor = CreateMonitor(1);
        var limiter = new DockerHubRequestLimiter(monitor);
        var held = limiter.WaitAsync(TestContext.CancellationToken);
        var waiting = limiter.WaitAsync(TestContext.CancellationToken);

        limiter.Dispose();
        monitor.Reload(CreateOptions(3));

        Assert.IsTrue(held.IsCompleted, "The first request takes the only slot");
        Assert.IsFalse(waiting.IsCompleted, "A disposed limiter must not react to a reload");
    }

    /// <summary>
    /// Create the options for a limit
    /// </summary>
    /// <param name="limit">Value of MaxParallelRequests</param>
    /// <returns>Options</returns>
    private static DockerUpdateGuardOptions CreateOptions(int limit)
    {
        return new DockerUpdateGuardOptions
               {
                   DockerHub = new DockerHubOptions
                               {
                                   MaxParallelRequests = limit,
                               },
               };
    }

    /// <summary>
    /// Create an options monitor for a limit
    /// </summary>
    /// <param name="limit">Value of MaxParallelRequests</param>
    /// <returns>Options monitor</returns>
    private static TestOptionsMonitor<DockerUpdateGuardOptions> CreateMonitor(int limit)
    {
        return new TestOptionsMonitor<DockerUpdateGuardOptions>(CreateOptions(limit));
    }

    #endregion // Methods
}