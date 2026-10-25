using System.Net;

using DockerUpdateGuard.Images;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DotNetReleaseMetadataService"/>
/// </summary>
[TestClass]
public partial class DotNetReleaseMetadataServiceTests
{
    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify the release index resolves the latest runtime for a channel
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DotNetReleaseMetadataServiceGetChannelReleaseAsyncReturnsLatestRuntimeAsync()
    {
        var handler = new StaticHttpJsonMessageHandler("""
                                                       {
                                                         "releases-index": [
                                                           {
                                                             "channel-version": "9.0",
                                                             "latest-runtime": "9.0.15",
                                                             "latest-release-date": "2025-06-10",
                                                             "security": true,
                                                             "support-phase": "active",
                                                             "eol-date": "2026-05-12",
                                                             "releases.json": "https://example.test/9.0/releases.json"
                                                           }
                                                         ]
                                                       }
                                                       """);
        var httpClient = new HttpClient(handler)
                         {
                             BaseAddress = new Uri("https://dotnet.example.test/"),
                         };

        try
        {
            var service = new DotNetReleaseMetadataService(httpClient,
                                                           new TestLogger<DotNetReleaseMetadataService>());

            var result = await service.GetChannelReleaseAsync("9.0", CancellationToken.None)
                                      .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "The release metadata service must succeed when the releases index is available");
            Assert.IsNotNull(result.Data, "The release metadata service must return normalized channel metadata");
            Assert.AreEqual(new Version(9, 0, 15),
                            result.Data.LatestRuntimeVersion,
                            "The release metadata service must parse the latest runtime version");
            Assert.IsTrue(result.Data.IsSecurityRelease,
                          "The release metadata service must expose the security flag from the feed");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify missing channels produce a not-found result
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DotNetReleaseMetadataServiceGetChannelReleaseAsyncForMissingChannelReturnsNotFoundAsync()
    {
        var handler = new StaticHttpJsonMessageHandler("""
                                                       {
                                                         "releases-index": []
                                                       }
                                                       """);

        var httpClient = new HttpClient(handler)
                         {
                             BaseAddress = new Uri("https://dotnet.example.test/"),
                         };

        try
        {
            var service = new DotNetReleaseMetadataService(httpClient,
                                                           new TestLogger<DotNetReleaseMetadataService>());

            var result = await service.GetChannelReleaseAsync("9.0", CancellationToken.None)
                                      .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.NotFound,
                            result.Status,
                            "Missing channels must return a not-found result instead of a false positive");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify an unreachable release feed reports the fixed connection-failed reason
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DotNetReleaseMetadataServiceGetChannelReleaseAsyncWhenTransportFailsReportsConnectionFailedAsync()
    {
        var injected = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (dotnet.internal.test:443)");
        var logger = new TestLogger<DotNetReleaseMetadataService>();

        using var handler = new RetryProbeHttpMessageHandler([null], HttpStatusCode.OK)
                            {
                                FailureFactory = () => injected,
                            };
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://dotnet.example.test/"),
                               };

        var service = new DotNetReleaseMetadataService(httpClient, logger);

        var result = await service.GetChannelReleaseAsync("9.0", CancellationToken.None)
                                  .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "An unreachable release feed must fail the lookup");
        Assert.AreEqual("Loading .NET release metadata failed: connection failed", result.Message, "A transport failure must report the fixed connection-failed reason");
        Assert.IsNotNull(result.Message, "A failed result must carry a message");
        Assert.DoesNotContain("internal.test", result.Message, "The exception text must not reach the result message");
        Assert.Contains(entry => ReferenceEquals(entry.Exception, injected),
                        logger.Entries,
                        "The transport failure must still be logged with the exception");
    }

    /// <summary>
    /// Verify a malformed release index reports the fixed invalid-response reason
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DotNetReleaseMetadataServiceGetChannelReleaseAsyncWhenIndexIsMalformedReportsInvalidResponseAsync()
    {
        using var handler = new StaticHttpJsonMessageHandler("{ \"releases-index\": [ internal.test");
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://dotnet.example.test/"),
                               };

        var service = new DotNetReleaseMetadataService(httpClient, new TestLogger<DotNetReleaseMetadataService>());

        var result = await service.GetChannelReleaseAsync("9.0", CancellationToken.None)
                                  .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A malformed release index must fail the lookup");
        Assert.AreEqual("Loading .NET release metadata failed: invalid response", result.Message, "A malformed index must report the fixed invalid-response reason");
        Assert.IsNotNull(result.Message, "A failed result must carry a message");
        Assert.DoesNotContain("internal.test", result.Message, "The parser text must not reach the result message");
    }

    /// <summary>
    /// Verify a cancelled caller token is reported as cancelled without throwing
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DotNetReleaseMetadataServiceGetChannelReleaseAsyncWhenCallerCancelsReportsCancelledAsync()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

        await cts.CancelAsync().ConfigureAwait(false);

        var token = cts.Token;

        using var handler = new RetryProbeHttpMessageHandler([null], HttpStatusCode.OK)
                            {
                                FailureFactory = () => new TaskCanceledException("A task was canceled. internal.test", null, token),
                            };
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://dotnet.example.test/"),
                               };

        var service = new DotNetReleaseMetadataService(httpClient, new TestLogger<DotNetReleaseMetadataService>());

        var result = await service.GetChannelReleaseAsync("9.0", token)
                                  .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A cancelled lookup must end as failed instead of throwing");
        Assert.AreEqual("Loading .NET release metadata failed: cancelled", result.Message, "A cancelled caller token must report the fixed cancelled reason");
        Assert.IsNotNull(result.Message, "A failed result must carry a message");
        Assert.DoesNotContain("internal.test", result.Message, "The exception text must not reach the result message");
    }

    #endregion // Methods
}