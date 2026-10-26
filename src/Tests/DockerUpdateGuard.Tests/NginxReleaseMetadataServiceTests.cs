using System.Net;

using DockerUpdateGuard.Images;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="NginxReleaseMetadataService"/>
/// </summary>
[TestClass]
public partial class NginxReleaseMetadataServiceTests
{
    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify the download listing resolves the latest release for a channel
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task NginxReleaseMetadataServiceGetChannelReleaseAsyncReturnsLatestChannelVersionAsync()
    {
        var handler = new StaticHttpMessageHandler("""
                                                   <html>
                                                   <body>
                                                   <a href="nginx-1.29.1.tar.gz">nginx-1.29.1.tar.gz</a>
                                                   <a href="nginx-1.29.8.tar.gz">nginx-1.29.8.tar.gz</a>
                                                   <a href="nginx-1.30.0.tar.gz">nginx-1.30.0.tar.gz</a>
                                                   </body>
                                                   </html>
                                                   """);
        var httpClient = new HttpClient(handler)
                         {
                             BaseAddress = new Uri("https://nginx.example.test/"),
                         };

        try
        {
            var service = new NginxReleaseMetadataService(httpClient,
                                                          new TestLogger<NginxReleaseMetadataService>());

            var result = await service.GetChannelReleaseAsync("1.29", CancellationToken.None)
                                      .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "The NGINX release metadata service must succeed when the download listing is available");
            Assert.IsNotNull(result.Data, "The NGINX release metadata service must return normalized channel metadata");
            Assert.AreEqual(new Version(1, 29, 8),
                            result.Data.LatestVersion,
                            "The NGINX release metadata service must return the latest version in the requested channel");
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
    public async Task NginxReleaseMetadataServiceGetChannelReleaseAsyncForMissingChannelReturnsNotFoundAsync()
    {
        var handler = new StaticHttpMessageHandler("""
                                                   <html>
                                                   <body>
                                                   <a href="nginx-1.29.8.tar.gz">nginx-1.29.8.tar.gz</a>
                                                   </body>
                                                   </html>
                                                   """);
        var httpClient = new HttpClient(handler)
                         {
                             BaseAddress = new Uri("https://nginx.example.test/"),
                         };

        try
        {
            var service = new NginxReleaseMetadataService(httpClient,
                                                          new TestLogger<NginxReleaseMetadataService>());

            var result = await service.GetChannelReleaseAsync("1.30", CancellationToken.None)
                                      .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.NotFound,
                            result.Status,
                            "Missing NGINX channels must return a not-found result instead of a false positive");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify an unreachable download listing reports the fixed connection-failed reason
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task NginxReleaseMetadataServiceGetChannelReleaseAsyncWhenTransportFailsReportsConnectionFailedAsync()
    {
        var injected = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (nginx.internal.test:443)");
        var logger = new TestLogger<NginxReleaseMetadataService>();

        using var handler = new RetryProbeHttpMessageHandler([null], HttpStatusCode.OK)
                            {
                                FailureFactory = () => injected,
                            };
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://nginx.example.test/"),
                               };

        var service = new NginxReleaseMetadataService(httpClient, logger);

        var result = await service.GetChannelReleaseAsync("1.29", CancellationToken.None)
                                  .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "An unreachable download listing must fail the lookup");
        Assert.AreEqual("Loading NGINX release metadata failed: connection failed", result.Message, "A transport failure must report the fixed connection-failed reason");
        Assert.IsNotNull(result.Message, "A failed result must carry a message");
        Assert.DoesNotContain("internal.test", result.Message, "The exception text must not reach the result message");
        Assert.Contains(entry => ReferenceEquals(entry.Exception, injected),
                        logger.Entries,
                        "The transport failure must still be logged with the exception");
    }

    /// <summary>
    /// Verify a failed release feed answer with a long body reports an excerpt of the body
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task NginxReleaseMetadataServiceGetChannelReleaseAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync()
    {
        using var handler = new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
                                                                {
                                                                    Content = new StringContent(new string('x', OversizedRemoteText.BodyLength)),
                                                                });
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://feed.example.test/"),
                               };

        var service = new NginxReleaseMetadataService(httpClient, new TestLogger<NginxReleaseMetadataService>());

        var result = await service.GetChannelReleaseAsync("1.29", TestContext.CancellationToken)
                                  .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A Bad Request answer must fail the lookup");
        Assert.AreEqual($"The NGINX download listing returned 400: {OversizedRemoteText.CreateExpectedExcerpt()}",
                        result.Message,
                        "The failure message must keep its text and carry the first 500 characters of the body followed by the ellipsis");
    }

    #endregion // Methods
}