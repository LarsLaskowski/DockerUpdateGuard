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

    #endregion // Methods
    #region Helper types

    #endregion // Helper types
}