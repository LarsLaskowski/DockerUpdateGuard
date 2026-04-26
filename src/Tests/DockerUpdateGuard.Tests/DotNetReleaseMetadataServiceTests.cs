using System.Net;
using System.Text;

using DockerUpdateGuard.Images;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DotNetReleaseMetadataService"/>
/// </summary>
[TestClass]
public class DotNetReleaseMetadataServiceTests
{
    #region Methods

    /// <summary>
    /// Verify the release index resolves the latest runtime for a channel
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DotNetReleaseMetadataServiceGetChannelReleaseAsyncReturnsLatestRuntimeAsync()
    {
        var handler = new StaticHttpMessageHandler("""
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
        var handler = new StaticHttpMessageHandler("""
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

    #endregion // Methods

    #region Helper types

    /// <summary>
    /// Fixed-response HTTP message handler for metadata tests
    /// </summary>
    private sealed class StaticHttpMessageHandler : HttpMessageHandler
    {
        #region Fields

        /// <summary>
        /// JSON payload
        /// </summary>
        private readonly string _jsonPayload;

        #endregion // Fields

        #region Constructors

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="jsonPayload">JSON payload</param>
        public StaticHttpMessageHandler(string jsonPayload)
        {
            _jsonPayload = jsonPayload;
        }

        #endregion // Constructors

        #region Methods

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                                   {
                                       Content = new StringContent(_jsonPayload,
                                                                   Encoding.UTF8,
                                                                   "application/json"),
                                   });
        }

        #endregion // Methods
    }

    #endregion // Helper types
}