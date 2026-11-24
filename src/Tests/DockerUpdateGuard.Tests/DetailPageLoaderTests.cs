using DockerUpdateGuard.Components;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DetailPageLoader"/>
/// </summary>
[TestClass]
public class DetailPageLoaderTests
{
    #region Methods

    /// <summary>
    /// Verify a completed load returns no message and logs nothing
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    public async Task DetailPageLoaderCompletedLoadReturnsNullWithoutLog()
    {
        var logger = new TestLogger<DetailPageLoaderTests>();
        var loadCount = 0;

        var message = await DetailPageLoader.LoadAsync(() =>
                                                       {
                                                           loadCount++;

                                                           return Task.CompletedTask;
                                                       },
                                                       logger,
                                                       "LoadSomething",
                                                       "Loading something failed")
                                            .ConfigureAwait(false);

        Assert.IsNull(message, "A completed load must return no message");
        Assert.AreEqual(1, loadCount, "The load must run once");
        Assert.IsEmpty(logger.Entries, "A completed load must log nothing");
    }

    /// <summary>
    /// Verify a failed load returns the prefix with the fixed reason, never the exception text, and logs the failure once
    /// </summary>
    /// <param name="exceptionKind">Kind of the thrown exception</param>
    /// <param name="expectedReason">Expected fixed reason</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    [DataRow("InvalidOperation", "unexpected error")]
    [DataRow("Http", "connection failed")]
    public async Task DetailPageLoaderFailedLoadReturnsPrefixedFixedReasonAndLogs(string exceptionKind, string expectedReason)
    {
        Exception exception = exceptionKind == "Http"
                                  ? new HttpRequestException("secret text from host.internal.test")
                                  : new InvalidOperationException("secret text from host.internal.test");
        var logger = new TestLogger<DetailPageLoaderTests>();

        var message = await DetailPageLoader.LoadAsync(() => Task.FromException(exception),
                                                       logger,
                                                       "LoadSomething",
                                                       "Loading something failed")
                                            .ConfigureAwait(false);

        Assert.AreEqual($"Loading something failed: {expectedReason}", message, "A failed load must return the prefix with the fixed reason");
        Assert.DoesNotContain("secret text", message!, "The message must not carry the exception's own text");
        UserActionFailureAssert.AssertLogged(logger, "LoadSomething", exception);
    }

    #endregion // Methods
}