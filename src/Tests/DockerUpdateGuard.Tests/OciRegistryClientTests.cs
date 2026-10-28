using System.Net;
using System.Net.Http.Headers;
using System.Text;

using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Helper;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="OciRegistryClient"/>
/// </summary>
[TestClass]
public partial class OciRegistryClientTests
{
    #region Constants

    /// <summary>
    /// Registry that matches the host grammar and has 201 characters, one over the limit
    /// </summary>
    private const string OverlongRegistry = "abcd.abcd.abcd.abcd.abcd.abcd.abcd.abcd.abcd.abcd."
                                            + "abcd.abcd.abcd.abcd.abcd.abcd.abcd.abcd.abcd.abcd."
                                            + "abcd.abcd.abcd.abcd.abcd.abcd.abcd.abcd.abcd.abcd."
                                            + "abcd.abcd.abcd.abcd.abcd.abcd.abcd.abcd.abcd.abcd."
                                            + "a";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify OCI registries can authenticate and return tag metadata
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncReadsTagsFromMicrosoftRegistryAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                CreateUnauthorizedResponse("https://mcr.microsoft.com/oauth2/token",
                                                           "mcr.microsoft.com",
                                                           "repository:mssql/server:pull"));
            handler.AddJsonResponse("https://mcr.microsoft.com/oauth2/token?service=mcr.microsoft.com&scope=repository%3Amssql%2Fserver%3Apull",
                                    """
                                    {
                                      "access_token": "mcr-token"
                                    }
                                    """);
            handler.AddJsonResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                    """
                                    {
                                      "name": "mssql/server",
                                      "tags": [
                                        "2019-CU32-GDR7-ubuntu-20.04"
                                      ]
                                    }
                                    """);
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/manifests/2019-CU32-GDR7-ubuntu-20.04",
                                CreateManifestResponse("sha256:mcr-tag",
                                                       """
                                                       {
                                                         "schemaVersion": 2,
                                                         "config": {
                                                           "digest": "sha256:config"
                                                         }
                                                       }
                                                       """));

            var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

            var result = await client.GetTagsAsync("mcr.microsoft.com",
                                                   "mssql/server",
                                                   cancellationToken: CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "OCI registry tag lookup must succeed when the registry challenge and manifest requests complete");
            Assert.IsNotNull(result.Data, "OCI registry tag lookup must return tag data");
            Assert.HasCount(1,
                            result.Data,
                            "OCI registry tag lookup must return all discovered tags");
            Assert.AreEqual("2019-CU32-GDR7-ubuntu-20.04",
                            result.Data[0].Tag,
                            "OCI registry tag lookup must keep the returned tag name");
            Assert.AreEqual("sha256:mcr-tag",
                            result.Data[0].Digest,
                            "OCI registry tag lookup must expose the manifest digest");
            Assert.Contains(request => request.RequestUri == "https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100"
                                       && request.AuthorizationScheme == "Bearer"
                                       && request.AuthorizationParameter == "mcr-token",
                            handler.Requests,
                            "OCI registry tag lookup must retry the tags endpoint with the resolved bearer token");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify OCI tag listing skips lower version tags before requesting metadata
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncSkipsLowerVersionTagsBeforeMetadataRequestsAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                CreateUnauthorizedResponse("https://mcr.microsoft.com/oauth2/token",
                                                           "mcr.microsoft.com",
                                                           "repository:mssql/server:pull"));
            handler.AddJsonResponse("https://mcr.microsoft.com/oauth2/token?service=mcr.microsoft.com&scope=repository%3Amssql%2Fserver%3Apull",
                                    """
                                    {
                                      "access_token": "mcr-token"
                                    }
                                    """);
            handler.AddJsonResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                    """
                                    {
                                      "name": "mssql/server",
                                      "tags": [
                                        "2.4.0",
                                        "2.4.1",
                                        "2.5.0"
                                      ]
                                    }
                                    """);
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/manifests/2.4.1",
                                CreateManifestResponse("sha256:241",
                                                       """
                                                       {
                                                         "schemaVersion": 2,
                                                         "config": {
                                                           "digest": "sha256:config241"
                                                         }
                                                       }
                                                       """));
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/manifests/2.5.0",
                                CreateManifestResponse("sha256:250",
                                                       """
                                                       {
                                                         "schemaVersion": 2,
                                                         "config": {
                                                           "digest": "sha256:config250"
                                                         }
                                                       }
                                                       """));

            var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());
            var result = await client.GetTagsAsync("mcr.microsoft.com",
                                                   "mssql/server",
                                                   queryOptions: new RegistryTagQueryOptions
                                                                 {
                                                                     CurrentTag = "2.4.1",
                                                                     MaximumTags = 10,
                                                                     MinimumVersionTag = "2.4.1",
                                                                 },
                                                   cancellationToken: CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Bounded OCI tag listing must still succeed");
            Assert.IsNotNull(result.Data, "Bounded OCI tag listing must return tag data");
            Assert.AreSequenceEqual(["2.4.1", "2.5.0"],
                                    result.Data.Select(entity => entity.Tag)
                                               .ToArray(),
                                    "Bounded OCI tag listing must skip lower version tags before metadata lookups");
            Assert.DoesNotContain(request => request.RequestUri == "https://mcr.microsoft.com/v2/mssql/server/manifests/2.4.0",
                                  handler.Requests,
                                  "Bounded OCI tag listing must avoid manifest requests for lower version tags");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify OCI tag lookup resolves the digest for the requested runtime architecture from a manifest list
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagAsyncUsesRequestedArchitectureDigestAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/manifests/latest",
                                CreateUnauthorizedResponse("https://mcr.microsoft.com/oauth2/token",
                                                           "mcr.microsoft.com",
                                                           "repository:mssql/server:pull"));
            handler.AddJsonResponse("https://mcr.microsoft.com/oauth2/token?service=mcr.microsoft.com&scope=repository%3Amssql%2Fserver%3Apull",
                                    """
                                    {
                                      "access_token": "mcr-token"
                                    }
                                    """);
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/manifests/latest",
                                CreateManifestListResponse("sha256:index",
                                                           """
                                                           {
                                                             "schemaVersion": 2,
                                                             "manifests": [
                                                               {
                                                                 "digest": "sha256:amd64",
                                                                 "platform": {
                                                                   "os": "linux",
                                                                   "architecture": "amd64"
                                                                 }
                                                               },
                                                               {
                                                                 "digest": "sha256:arm64",
                                                                 "platform": {
                                                                   "os": "linux",
                                                                   "architecture": "arm64"
                                                                 }
                                                               }
                                                             ]
                                                           }
                                                           """));
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/manifests/sha256%3Aarm64",
                                CreateManifestResponse("sha256:arm64",
                                                       """
                                                       {
                                                         "schemaVersion": 2,
                                                         "config": {
                                                           "digest": "sha256:config-arm64"
                                                         }
                                                       }
                                                       """));

            var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());
            var result = await client.GetTagAsync(new ImageReference
                                                  {
                                                      Registry = "mcr.microsoft.com",
                                                      Repository = "mssql/server",
                                                      Tag = "latest",
                                                  },
                                                  "linux",
                                                  "arm64",
                                                  CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "OCI tag lookup must succeed when the registry serves a multi-architecture manifest list");
            Assert.IsNotNull(result.Data, "OCI tag lookup must return tag data");
            Assert.AreEqual("sha256:index",
                            result.Data.Digest,
                            "OCI tag lookup must keep the top-level tag digest after resolving the requested platform manifest");
            Assert.Contains(request => request.RequestUri == "https://mcr.microsoft.com/v2/mssql/server/manifests/sha256%3Aarm64",
                            handler.Requests,
                            "OCI tag lookup must follow the manifest digest that matches the requested runtime architecture");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify OCI tag listing reuses a cached bearer token across the per-tag manifest fan-out
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncReusesCachedBearerTokenAcrossTagsAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                CreateUnauthorizedResponse("https://mcr.microsoft.com/oauth2/token",
                                                           "mcr.microsoft.com",
                                                           "repository:mssql/server:pull"));
            handler.AddJsonResponse("https://mcr.microsoft.com/oauth2/token?service=mcr.microsoft.com&scope=repository%3Amssql%2Fserver%3Apull",
                                    """
                                    {
                                      "access_token": "mcr-token",
                                      "expires_in": 300
                                    }
                                    """);
            handler.AddJsonResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                    """
                                    {
                                      "name": "mssql/server",
                                      "tags": [
                                        "2019-CU32-GDR7-ubuntu-20.04",
                                        "2022-CU12-ubuntu-20.04"
                                      ]
                                    }
                                    """);
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/manifests/2019-CU32-GDR7-ubuntu-20.04",
                                CreateManifestResponse("sha256:mcr-tag",
                                                       """
                                                       {
                                                         "schemaVersion": 2,
                                                         "config": {
                                                           "digest": "sha256:config"
                                                         }
                                                       }
                                                       """));
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/manifests/2022-CU12-ubuntu-20.04",
                                CreateManifestResponse("sha256:mcr-tag2",
                                                       """
                                                       {
                                                         "schemaVersion": 2,
                                                         "config": {
                                                           "digest": "sha256:config2"
                                                         }
                                                       }
                                                       """));

            var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

            var result = await client.GetTagsAsync("mcr.microsoft.com",
                                                   "mssql/server",
                                                   cancellationToken: CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "OCI registry tag lookup must succeed across the per-tag fan-out");
            Assert.IsNotNull(result.Data, "OCI registry tag lookup must return tag data");
            Assert.HasCount(2,
                            result.Data,
                            "OCI registry tag lookup must return all discovered tags");
            Assert.HasCount(1,
                            handler.Requests.Where(request => request.RequestUri == "https://mcr.microsoft.com/oauth2/token?service=mcr.microsoft.com&scope=repository%3Amssql%2Fserver%3Apull"),
                            "OCI registry tag lookup must request the bearer token only once and reuse it across the per-tag manifest fan-out");
            Assert.Contains(request => request.RequestUri == "https://mcr.microsoft.com/v2/mssql/server/manifests/2022-CU12-ubuntu-20.04"
                                       && request.AuthorizationScheme == "Bearer"
                                       && request.AuthorizationParameter == "mcr-token",
                            handler.Requests,
                            "OCI registry tag lookup must send the cached bearer token directly for subsequent manifest requests");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify OCI base-image resolution strips an embedded image reference from the base digest label
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientResolveBaseImagesAsyncNormalizesDigestLabelReferenceAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            handler.AddResponse("https://mcr.microsoft.com/v2/dotnet/aspnet/manifests/8.0",
                                CreateUnauthorizedResponse("https://mcr.microsoft.com/oauth2/token",
                                                           "mcr.microsoft.com",
                                                           "repository:dotnet/aspnet:pull"));
            handler.AddJsonResponse("https://mcr.microsoft.com/oauth2/token?service=mcr.microsoft.com&scope=repository%3Adotnet%2Faspnet%3Apull",
                                    """
                                    {
                                      "access_token": "mcr-token"
                                    }
                                    """);
            handler.AddResponse("https://mcr.microsoft.com/v2/dotnet/aspnet/manifests/8.0",
                                CreateManifestResponse("sha256:aspnet",
                                                       """
                                                       {
                                                         "schemaVersion": 2,
                                                         "config": {
                                                           "digest": "sha256:aspnet-config"
                                                         }
                                                       }
                                                       """));
            handler.AddJsonResponse("https://mcr.microsoft.com/v2/dotnet/aspnet/blobs/sha256%3Aaspnet-config",
                                    """
                                    {
                                      "config": {
                                        "Labels": {
                                          "org.opencontainers.image.base.name": "mcr.microsoft.com/dotnet/runtime:8.0",
                                          "org.opencontainers.image.base.digest": "mcr.microsoft.com/dotnet/runtime@sha256:runtime"
                                        }
                                      }
                                    }
                                    """);

            var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());
            var result = await client.ResolveBaseImagesAsync(new ImageReference
                                                             {
                                                                 Registry = "mcr.microsoft.com",
                                                                 Repository = "dotnet/aspnet",
                                                                 Tag = "8.0",
                                                             },
                                                             CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "OCI base-image resolution must succeed when the manifest and config blob can be read");
            Assert.IsNotNull(result.Data, "OCI base-image resolution must return resolved base images");
            Assert.HasCount(1,
                            result.Data,
                            "OCI base-image resolution must return the discovered base image");
            Assert.AreEqual("sha256:runtime",
                            result.Data[0].Digest,
                            "OCI base-image resolution must strip the image reference prefix from the base digest label");
            Assert.Contains(request => request.RequestUri == "https://mcr.microsoft.com/v2/dotnet/runtime/manifests/sha256%3Aruntime",
                            handler.Requests,
                            "OCI base-image recursion must use the normalized digest when resolving the parent image chain");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a transport failure during OCI base-image resolution reports the fixed connection-failed reason
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientResolveBaseImagesAsyncWhenTransportFailsReportsConnectionFailedAsync()
    {
        var injected = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (registry.internal.test:443)");
        var logger = new TestLogger<OciRegistryClient>();
        var imageReference = new ImageReference
                             {
                                 Registry = "mcr.microsoft.com",
                                 Repository = "dotnet/aspnet",
                                 Tag = "8.0",
                             };

        using var handler = new RetryProbeHttpMessageHandler([null], HttpStatusCode.OK)
                            {
                                FailureFactory = () => injected,
                            };
        using var httpClient = new HttpClient(handler);

        var client = new OciRegistryClient(httpClient, logger);

        var result = await client.ResolveBaseImagesAsync(imageReference, CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A transport failure must fail the base-image resolution");
        Assert.AreEqual($"Base image chain resolution failed for '{imageReference.FullReference}': connection failed",
                        result.Message,
                        "A transport failure must report the fixed connection-failed reason");
        Assert.IsNotNull(result.Message, "A failed result must carry a message");
        Assert.DoesNotContain("internal.test", result.Message, "The exception text must not reach the result message");
        Assert.Contains(entry => ReferenceEquals(entry.Exception, injected),
                        logger.Entries,
                        "The transport failure must still be logged with the exception");
    }

    /// <summary>
    /// Verify OCI tag listing refuses a Link header on another origin
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncForeignLinkHeaderReturnsFailedAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                CreateTagListResponse("""{ "name": "mssql/server", "tags": [] }""",
                                                      "<https://attacker.example/collect?last=1.0.0&n=100&token=leak>; rel=\"next\""));

            var logger = new TestLogger<OciRegistryClient>();
            var client = new OciRegistryClient(httpClient, logger);

            var result = await client.GetTagsAsync("mcr.microsoft.com",
                                                   "mssql/server",
                                                   cancellationToken: CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Failed,
                            result.Status,
                            "A Link header on another origin must fail the tag listing");
            Assert.AreEqual("Registry tag listing for 'mcr.microsoft.com/mssql/server' stopped: the next-page link pointed to a different origin (https://attacker.example)",
                            result.Message,
                            "The failure message must name the rejected origin only");
            Assert.DoesNotContain(request => request.RequestUri.Contains("attacker.example", StringComparison.OrdinalIgnoreCase),
                                  handler.Requests,
                                  "No request may be sent to the origin of a rejected Link target");

            var warning = Assert.ContainsSingle(entry => entry.EventId.Id == 2099 && entry.LogLevel == LogLevel.Warning,
                                                logger.Entries,
                                                "A rejected Link target must be logged once as warning 2099");

            Assert.Contains("https://attacker.example",
                            warning.Message,
                            "The warning must name the rejected origin");
            PaginationLeakAssert.DoesNotLeak(string.Join('\n', logger.Entries.Select(entry => entry.Message).Append(result.Message)),
                                             "mcr-token");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify OCI tag listing follows a root-relative Link target, the form ghcr.io sends
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncRelativeLinkHeaderFollowsLinkAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                CreateTagListResponse("""{ "name": "mssql/server", "tags": [] }""",
                                                      "</v2/mssql/server/tags/list?last=1.0.0&n=100>; rel=\"next\""));
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?last=1.0.0&n=100",
                                CreateTagListResponse("""{ "name": "mssql/server", "tags": [ "2.0.0" ] }""", null));
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/manifests/2.0.0",
                                CreateManifestResponse("sha256:200",
                                                       """
                                                       {
                                                         "schemaVersion": 2,
                                                         "config": {
                                                           "digest": "sha256:config200"
                                                         }
                                                       }
                                                       """));

            var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

            var result = await client.GetTagsAsync("mcr.microsoft.com",
                                                   "mssql/server",
                                                   cancellationToken: CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "A root-relative Link target must be followed on the registry origin");
            Assert.IsNotNull(result.Data, "OCI registry tag lookup must return tag data");
            Assert.AreSequenceEqual(["2.0.0"],
                                    result.Data.Select(entity => entity.Tag)
                                               .ToArray(),
                                    "The tags of the page a relative Link target points to must be listed");
            Assert.ContainsSingle(handler.Requests.Where(request => request.RequestUri == "https://mcr.microsoft.com/v2/mssql/server/tags/list?last=1.0.0&n=100"),
                                  "The second page must be requested at the resolved absolute address");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify OCI tag listing follows an absolute Link target on the registry origin
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncSameOriginLinkHeaderFollowsLinkAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                CreateTagListResponse("""{ "name": "mssql/server", "tags": [] }""",
                                                      "<https://mcr.microsoft.com/v2/mssql/server/tags/list?last=1.0.0&n=100>; rel=\"next\""));
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?last=1.0.0&n=100",
                                CreateTagListResponse("""{ "name": "mssql/server", "tags": [ "2.0.0" ] }""", null));
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/manifests/2.0.0",
                                CreateManifestResponse("sha256:200",
                                                       """
                                                       {
                                                         "schemaVersion": 2,
                                                         "config": {
                                                           "digest": "sha256:config200"
                                                         }
                                                       }
                                                       """));

            var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

            var result = await client.GetTagsAsync("mcr.microsoft.com",
                                                   "mssql/server",
                                                   cancellationToken: CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "An absolute Link target on the registry origin must be followed");
            Assert.IsNotNull(result.Data, "OCI registry tag lookup must return tag data");
            Assert.AreSequenceEqual(["2.0.0"],
                                    result.Data.Select(entity => entity.Tag)
                                               .ToArray(),
                                    "The tags of the page a same-origin Link target points to must be listed");
            Assert.ContainsSingle(handler.Requests.Where(request => request.RequestUri == "https://mcr.microsoft.com/v2/mssql/server/tags/list?last=1.0.0&n=100"),
                                  "The second page must be requested at the Link target");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify OCI tag listing ends at a Link header whose target is not enclosed in angle brackets
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncMalformedLinkHeaderEndsListingAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                CreateTagListResponse("""{ "name": "mssql/server", "tags": [] }""",
                                                      "/v2/mssql/server/tags/list?last=1.0.0&n=100; rel=\"next\""));

            var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

            var result = await client.GetTagsAsync("mcr.microsoft.com",
                                                   "mssql/server",
                                                   cancellationToken: CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "A Link header without a bracketed target must end the listing without failing");
            Assert.HasCount(1,
                            handler.Requests,
                            "No further page may be requested for a Link header without a bracketed target");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify OCI tag listing ends at a Link header without a next relation
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncLinkHeaderWithoutNextRelationEndsListingAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                CreateTagListResponse("""{ "name": "mssql/server", "tags": [] }""",
                                                      "</v2/mssql/server/tags/list?last=0.1.0&n=100>; rel=\"prev\""));

            var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

            var result = await client.GetTagsAsync("mcr.microsoft.com",
                                                   "mssql/server",
                                                   cancellationToken: CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "A Link header without a next relation must end the listing without failing");
            Assert.HasCount(1,
                            handler.Requests,
                            "No further page may be requested for a Link header without a next relation");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify an OCI tag lookup answered with a long error body reports an excerpt of the body
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync()
    {
        using var handler = new DelegateHttpMessageHandler(_ => CreateLongErrorResponse(reasonPhrase: null));
        using var httpClient = new HttpClient(handler);

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagsAsync("mcr.microsoft.com", "mssql/server", cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A Bad Request answer must fail the tag lookup");
        Assert.AreEqual($"Tag lookup failed for 'mcr.microsoft.com/mssql/server': 400 Bad Request - {OversizedRemoteText.CreateExpectedExcerpt()}",
                        result.Message,
                        "The failure message must keep its text and carry the first 500 characters of the body followed by the ellipsis");
    }

    /// <summary>
    /// Verify an OCI tag lookup answered with a long reason phrase reports a bounded reason phrase
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncWhenReasonPhraseIsLongReturnsBoundedReasonAsync()
    {
        using var handler = new DelegateHttpMessageHandler(_ => CreateLongErrorResponse(new string('r', OversizedRemoteText.ReasonPhraseLength)));
        using var httpClient = new HttpClient(handler);

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagsAsync("mcr.microsoft.com", "mssql/server", cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A Bad Request answer must fail the tag lookup");
        Assert.AreEqual($"Tag lookup failed for 'mcr.microsoft.com/mssql/server': 400 {new string('r', 100)}\u2026 - {OversizedRemoteText.CreateExpectedExcerpt()}",
                        result.Message,
                        "The failure message must carry the first 100 characters of the reason phrase followed by the ellipsis");
    }

    /// <summary>
    /// Verify the tag lookup message of the longest stored registry and repository stays within the assessment message limit
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncWhenNamesAreAtStoredLimitsKeepsMessageWithinAssessmentLimitAsync()
    {
        var registry = OversizedRemoteText.CreateRegistryOfStoredLimit();
        var repository = new string('r', 300);

        using var handler = new DelegateHttpMessageHandler(_ => CreateLongErrorResponse(new string('r', OversizedRemoteText.ReasonPhraseLength)));
        using var httpClient = new HttpClient(handler);

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagsAsync(registry, repository, cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A Bad Request answer must fail the tag lookup");
        Assert.IsNotNull(result.Message, "A failed result must carry a message");
        Assert.StartsWith($"Tag lookup failed for '{registry}/{repository}'", result.Message, "The failure message must name the registry and repository");
        Assert.EndsWith(OversizedRemoteText.CreateExpectedExcerpt(), result.Message, "The failure message must end with the bounded body excerpt");
        Assert.IsLessThanOrEqualTo(OversizedRemoteText.AssessmentMessageLimit,
                                   result.Message.Length,
                                   "The message is stored in a 2000-character column and must not exceed it");
    }

    /// <summary>
    /// Verify the rejection message for an oversized foreign link host stays within the assessment message limit
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncWhenForeignLinkHostIsOversizedKeepsMessageWithinAssessmentLimitAsync()
    {
        var registry = OversizedRemoteText.CreateRegistryOfStoredLimit();
        var repository = new string('r', 300);
        var foreignLink = $"<https://{OversizedRemoteText.CreateOversizedHost()}/collect?last=1.0.0>; rel=\"next\"";

        using var handler = new DelegateHttpMessageHandler(_ => CreateTagListResponse("""{ "name": "app", "tags": [] }""", foreignLink));
        using var httpClient = new HttpClient(handler);

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagsAsync(registry, repository, cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A next-page link on another origin must fail the tag lookup");
        Assert.IsNotNull(result.Message, "A failed result must carry a message");
        Assert.EndsWith("\u2026)", result.Message, "The rejected origin must be cut and end with the ellipsis");
        Assert.IsLessThanOrEqualTo(OversizedRemoteText.AssessmentMessageLimit,
                                   result.Message.Length,
                                   "The message is stored in a 2000-character column and must not exceed it");
    }

    /// <summary>
    /// Verify an OCI tag lookup replaces control characters of the reason phrase and the body
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncWhenReasonAndBodyHaveControlCharactersReplacesThemAsync()
    {
        var escape = char.ConvertFromUtf32(0x001B);
        var lineSeparator = char.ConvertFromUtf32(0x2028);
        var reasonPhrase = $"Server{escape}Error";
        var body = $"oops{lineSeparator}forged";

        Assert.Contains(escape, reasonPhrase, "The reason phrase must contain the escape character");
        Assert.Contains(lineSeparator, body, "The body must contain the line separator");

        using var handler = new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
                                                                {
                                                                    ReasonPhrase = reasonPhrase,
                                                                    Content = new StringContent(body),
                                                                });
        using var httpClient = new HttpClient(handler);

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagsAsync("mcr.microsoft.com", "mssql/server", cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A Bad Request answer must fail the tag lookup");
        Assert.AreEqual("Tag lookup failed for 'mcr.microsoft.com/mssql/server': 400 Server Error - oops forged",
                        result.Message,
                        "The control characters of the reason phrase and the body must be replaced by spaces");
    }

    /// <summary>
    /// Verify the OCI tag listing neither lists nor inspects names that are not valid tags
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncSkipsInvalidTagNamesAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        var oversizedName = new string('a', 129);

        handler.AddJsonResponse("https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                $$"""
                                  {
                                    "name": "mssql/server",
                                    "tags": [ "1.0", "{{oversizedName}}", "..", "2.0" ]
                                  }
                                  """);
        handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/manifests/1.0",
                            CreateManifestResponse("sha256:100", """{ "schemaVersion": 2, "config": { "digest": "sha256:config100" } }"""));
        handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/manifests/2.0",
                            CreateManifestResponse("sha256:200", """{ "schemaVersion": 2, "config": { "digest": "sha256:config200" } }"""));

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());
        var result = await client.GetTagsAsync("mcr.microsoft.com", "mssql/server", cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The listing must succeed when some names are invalid");
        Assert.IsNotNull(result.Data, "The listing must return the valid tags");
        Assert.AreSequenceEqual(["1.0", "2.0"],
                                result.Data.Select(entity => entity.Tag)
                                           .ToArray(),
                                "The listing must contain only the valid names of the page");
        Assert.AreSequenceEqual([
                                    "https://mcr.microsoft.com/v2/mssql/server/tags/list?n=100",
                                    "https://mcr.microsoft.com/v2/mssql/server/manifests/1.0",
                                    "https://mcr.microsoft.com/v2/mssql/server/manifests/2.0"
                                ],
                                handler.Requests.Select(request => request.RequestUri)
                                                .ToArray(),
                                "No manifest request may be sent for a name that is not a valid tag");
    }

    /// <summary>
    /// Verify the OCI tag lookup reports no digest for an oversized digest header
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagAsyncOversizedDigestHeaderReturnsNoDigestAsync()
    {
        var digest = "sha256:" + new string('a', 249);

        Assert.AreEqual(256, digest.Length, "The header must be one character over the maximum");

        var result = await LookUpTagWithDigestHeaderAsync(digest).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The lookup must succeed when the digest header is oversized");
        Assert.IsNotNull(result.Data, "The lookup must return tag data");
        Assert.IsNull(result.Data.Digest, "An oversized digest header must be reported as unknown");
    }

    /// <summary>
    /// Verify the OCI tag lookup keeps a digest header of the maximum length
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagAsyncMaximumLengthDigestHeaderIsKeptAsync()
    {
        var digest = "sha256:" + new string('a', 248);

        Assert.AreEqual(255, digest.Length, "The header must have exactly the maximum length");

        var result = await LookUpTagWithDigestHeaderAsync(digest).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The lookup must succeed for a digest of the maximum length");
        Assert.IsNotNull(result.Data, "The lookup must return tag data");
        Assert.AreEqual(digest, result.Data.Digest, "A valid digest of the maximum length must be kept");
    }

    /// <summary>
    /// Verify the OCI tag lookup answers not found without a request or a log entry for an invalid requested tag
    /// </summary>
    /// <param name="tag">Requested tag</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("..")]
    [DataRow("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task OciRegistryClientGetTagAsyncInvalidRequestedTagReturnsNotFoundWithoutRequestAsync(string tag)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        var logger = new TestLogger<OciRegistryClient>();
        var client = new OciRegistryClient(httpClient, logger);

        var result = await client.GetTagAsync(new ImageReference
                                              {
                                                  Registry = "mcr.microsoft.com",
                                                  Repository = "mssql/server",
                                                  Tag = tag,
                                              },
                                              "linux",
                                              "arm64",
                                              CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.NotFound, result.Status, "An invalid requested tag must be answered as not found");
        Assert.AreEqual("Requested tag is not a valid registry tag",
                        result.Message,
                        "The message must be the fixed text and quote nothing of the tag");
        Assert.IsEmpty(handler.Requests, "No token or manifest request may be sent for an invalid requested tag");
        Assert.IsEmpty(logger.Entries, "Nothing may be logged for an invalid requested tag");
    }

    /// <summary>
    /// Verify the OCI image configuration replaces control characters in environment variables, label values, operating system and architecture
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetImageConfigurationAsyncWhenFieldsHaveControlCharactersReplacesThemAsync()
    {
        var blobJson = """
                       {
                         "os": "li\u000Anux\u202Ex",
                         "architecture": "am\u000Ad\u202E64",
                         "config": {
                           "Env": ["A=1\u000A2\u202E3", "\u001B"],
                           "Labels": {
                             "org.example.ver\u202Esion": "1\u000A.0\u200B.2",
                             "plain": "unchanged"
                           }
                         }
                       }
                       """;

        Assert.Contains("\\u202E", blobJson, "The test input must carry the escaped format character");
        Assert.Contains("\\u000A", blobJson, "The test input must carry the escaped line feed");

        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse("https://mcr.microsoft.com/v2/dotnet/aspnet/manifests/8.0",
                            CreateManifestResponse("sha256:aspnet",
                                                   """
                                                   { "schemaVersion": 2, "config": { "digest": "sha256:aspnet-config" } }
                                                   """));
        handler.AddJsonResponse("https://mcr.microsoft.com/v2/dotnet/aspnet/blobs/sha256%3Aaspnet-config", blobJson);

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetImageConfigurationAsync(new ImageReference
                                                             {
                                                                 Registry = "mcr.microsoft.com",
                                                                 Repository = "dotnet/aspnet",
                                                                 Tag = "8.0",
                                                             },
                                                             CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The configuration lookup must succeed for a well-formed blob");
        Assert.IsNotNull(result.Data, "The configuration lookup must return the configuration");
        Assert.AreEqual("li nux x", result.Data.OperatingSystem, "The operating system must be cleaned");
        Assert.AreEqual("am d 64", result.Data.Architecture, "The architecture must be cleaned");
        Assert.AreSequenceEqual(["A=1 2 3"], result.Data.EnvironmentVariables.ToArray(), "The environment variables must be cleaned and one that is only replaced characters must be dropped like a blank one");
        Assert.AreEqual("1 .0 .2", result.Data.Labels["org.example.ver\u202Esion"], "The label value must be cleaned while the label name stays as received");
        Assert.AreEqual("unchanged", result.Data.Labels["plain"], "A label value without such a character must stay unchanged");
    }

    /// <summary>
    /// Verify the OCI base-image resolution replaces control characters in the base-image labels before it parses them
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientResolveBaseImagesAsyncWhenBaseImageLabelsHaveControlCharactersReplacesThemAsync()
    {
        var blobJson = """
                       {
                         "config": {
                           "Labels": {
                             "org.opencontainers.image.base.name": "\u001Bmcr.microsoft.com/dotnet/runtime:8.0\u202E",
                             "org.opencontainers.image.base.digest": "\u200Bsha256:abcd"
                           }
                         }
                       }
                       """;

        Assert.Contains("\\u202E", blobJson, "The test input must carry the escaped format character");
        Assert.Contains("\\u001B", blobJson, "The test input must carry the escaped escape character");

        using var handler = new DelegateHttpMessageHandler(request => CreateBaseImageChainResponse(request, blobJson));
        using var httpClient = new HttpClient(handler);

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.ResolveBaseImagesAsync(new ImageReference
                                                         {
                                                             Registry = "mcr.microsoft.com",
                                                             Repository = "dotnet/aspnet",
                                                             Tag = "8.0",
                                                         },
                                                         CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The base-image resolution must succeed for well-formed blobs");
        Assert.IsNotNull(result.Data, "The base-image resolution must return the base images");

        var baseImage = Assert.ContainsSingle(result.Data, "The base-image resolution must return the one base image of the chain");

        Assert.AreEqual("mcr.microsoft.com", baseImage.Registry, "The registry of the base image must be kept");
        Assert.AreEqual("dotnet/runtime", baseImage.Repository, "The replaced characters at the edges of the name must be trimmed off the repository");
        Assert.AreEqual("8.0", baseImage.Tag, "The replaced characters at the edges of the name must be trimmed off the tag");
        Assert.AreEqual("sha256:abcd", baseImage.Digest, "The replaced character at the edge of the digest must be trimmed off the digest");
    }

    /// <summary>
    /// Verify the OCI base-image resolution ends with an empty chain at a label that is not a valid reference
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientResolveBaseImagesAsyncWhenBaseImageLabelIsInvalidReturnsEmptyChainAsync()
    {
        var blobJson = $$"""
                         { "config": { "Labels": { "org.opencontainers.image.base.name": "mcr.microsoft.com/{{new string('a', 256)}}:8.0" } } }
                         """;

        using var handler = new DelegateHttpMessageHandler(request => CreateBaseImageChainResponse(request, blobJson));
        using var httpClient = new HttpClient(handler);

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.ResolveBaseImagesAsync(new ImageReference
                                                         {
                                                             Registry = "mcr.microsoft.com",
                                                             Repository = "dotnet/aspnet",
                                                             Tag = "8.0",
                                                         },
                                                         CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "An invalid label must end the chain, not fail the resolution");
        Assert.IsNotNull(result.Data, "The resolution must return the chain");
        Assert.IsEmpty(result.Data, "A base image with a repository over 255 characters must not be reported");
    }

    /// <summary>
    /// Verify the OCI tag lookup answers not found without a request or a log entry for a repository with a dot segment
    /// </summary>
    /// <param name="repository">Repository with a dot segment</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("corp/../../../v2/_catalog")]
    [DataRow("./corp/api")]
    [DataRow("corp/.")]
    [DataRow("..")]
    public async Task OciRegistryClientGetTagAsyncDotSegmentRepositoryReturnsNotFoundWithoutRequestAsync(string repository)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        var logger = new TestLogger<OciRegistryClient>();
        var client = new OciRegistryClient(httpClient, logger);

        var result = await client.GetTagAsync(new ImageReference
                                              {
                                                  Registry = "mcr.microsoft.com",
                                                  Repository = repository,
                                                  Tag = "1.0",
                                              },
                                              "linux",
                                              "amd64",
                                              CancellationToken.None)
                                 .ConfigureAwait(false);

        AssertDotSegmentRefusal(result.Status, result.Message, handler, logger);
    }

    /// <summary>
    /// Verify the OCI image configuration lookup answers not found without a request or a log entry for a reference with a dot segment
    /// </summary>
    /// <param name="repository">Repository</param>
    /// <param name="tag">Tag</param>
    /// <param name="digest">Digest or null</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("corp/..", "1.0", null)]
    [DataRow("corp/api", "..", null)]
    [DataRow("corp/api", ".", null)]
    [DataRow("corp/api", "1.0", "..")]
    public async Task OciRegistryClientGetImageConfigurationAsyncDotSegmentReferenceReturnsNotFoundWithoutRequestAsync(string repository, string tag, string? digest)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        var logger = new TestLogger<OciRegistryClient>();
        var client = new OciRegistryClient(httpClient, logger);

        var result = await client.GetImageConfigurationAsync(new ImageReference
                                                             {
                                                                 Registry = "mcr.microsoft.com",
                                                                 Repository = repository,
                                                                 Tag = tag,
                                                                 Digest = digest,
                                                             },
                                                             CancellationToken.None)
                                 .ConfigureAwait(false);

        AssertDotSegmentRefusal(result.Status, result.Message, handler, logger);
    }

    /// <summary>
    /// Verify the OCI base-image resolution ends the chain without a request for a repository with a dot segment
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientResolveBaseImagesAsyncDotSegmentRepositoryReturnsNoBaseImagesWithoutRequestAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        var logger = new TestLogger<OciRegistryClient>();
        var client = new OciRegistryClient(httpClient, logger);

        var result = await client.ResolveBaseImagesAsync(new ImageReference
                                                         {
                                                             Registry = "mcr.microsoft.com",
                                                             Repository = "corp/../../v2/x",
                                                             Tag = "1.0",
                                                         },
                                                         CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A repository with a dot segment must end the chain like a 404 and not fail the resolution");
        Assert.IsNotNull(result.Data, "The resolution must return a base-image list");
        Assert.IsEmpty(result.Data, "A repository with a dot segment must yield no base images");
        Assert.IsEmpty(handler.Requests, "No request may be sent for a repository with a dot segment");
        Assert.IsEmpty(logger.Entries, "Nothing may be logged for a repository with a dot segment");
    }

    /// <summary>
    /// Verify the OCI tags listing answers not found without a request or a log entry for a repository with a dot segment
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncDotSegmentRepositoryReturnsNotFoundWithoutRequestAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        var logger = new TestLogger<OciRegistryClient>();
        var client = new OciRegistryClient(httpClient, logger);

        var result = await client.GetTagsAsync("mcr.microsoft.com",
                                               "corp/../../../v2/_catalog",
                                               cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        AssertDotSegmentRefusal(result.Status, result.Message, handler, logger);
    }

    /// <summary>
    /// Verify the OCI image configuration lookup does not request a configuration blob whose digest is a dot segment
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetImageConfigurationAsyncDotSegmentConfigDigestReturnsNotFoundWithoutBlobRequestAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse("https://mcr.microsoft.com/v2/corp/api/manifests/1.0",
                            CreateManifestResponse("sha256:manifest",
                                                   """
                                                   { "schemaVersion": 2, "config": { "digest": ".." } }
                                                   """));

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetImageConfigurationAsync(new ImageReference
                                                             {
                                                                 Registry = "mcr.microsoft.com",
                                                                 Repository = "corp/api",
                                                                 Tag = "1.0",
                                                             },
                                                             CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.NotFound, result.Status, "A configuration digest with a dot segment must be answered as not found");
        Assert.AreEqual(RegistryReferenceValidator.DotSegmentMessage, result.Message, "The message must be the fixed dot-segment text");
        Assert.HasCount(1, handler.Requests, "Only the manifest may be requested, never the blob");
        Assert.AreEqual("https://mcr.microsoft.com/v2/corp/api/manifests/1.0",
                        handler.Requests[0].RequestUri,
                        "The one request must be the manifest");
    }

    /// <summary>
    /// Verify the OCI tag lookup does not request a platform manifest whose digest in the manifest list is a dot segment
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagAsyncDotSegmentManifestListDigestReturnsNotFoundAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse("https://mcr.microsoft.com/v2/corp/api/manifests/1.0",
                            CreateManifestListResponse("sha256:index",
                                                       """
                                                       {
                                                         "schemaVersion": 2,
                                                         "manifests": [
                                                           { "digest": "..", "platform": { "os": "linux", "architecture": "amd64" } }
                                                         ]
                                                       }
                                                       """));

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagAsync(new ImageReference
                                              {
                                                  Registry = "mcr.microsoft.com",
                                                  Repository = "corp/api",
                                                  Tag = "1.0",
                                              },
                                              "linux",
                                              "amd64",
                                              CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.NotFound, result.Status, "A manifest-list digest with a dot segment must be answered as not found");
        Assert.AreEqual(RegistryReferenceValidator.DotSegmentMessage, result.Message, "The message must be the fixed dot-segment text");
        Assert.HasCount(1, handler.Requests, "Only the manifest list may be requested, never the platform manifest");
        Assert.AreEqual("https://mcr.microsoft.com/v2/corp/api/manifests/1.0",
                        handler.Requests[0].RequestUri,
                        "The one request must be the manifest list");
    }

    /// <summary>
    /// Verify the OCI tag lookup keeps a repository segment that only contains dots or percent text
    /// </summary>
    /// <param name="repository">Repository</param>
    /// <param name="expectedUri">Expected manifest request URI</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("corp/my.app", "https://mcr.microsoft.com/v2/corp/my.app/manifests/1.0")]
    [DataRow("corp/.../api", "https://mcr.microsoft.com/v2/corp/.../api/manifests/1.0")]
    [DataRow("corp/%2E%2E/api", "https://mcr.microsoft.com/v2/corp/%252E%252E/api/manifests/1.0")]
    public async Task OciRegistryClientGetTagAsyncDotsInsideSegmentKeepsSegmentAsync(string repository, string expectedUri)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse(expectedUri,
                            CreateManifestResponse("sha256:manifest",
                                                   """
                                                   { "schemaVersion": 2, "config": { "digest": "sha256:config" } }
                                                   """));

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagAsync(new ImageReference
                                              {
                                                  Registry = "mcr.microsoft.com",
                                                  Repository = repository,
                                                  Tag = "1.0",
                                              },
                                              "linux",
                                              "amd64",
                                              CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A segment that is not exactly a dot segment must not be refused");
        Assert.HasCount(1, handler.Requests, "Exactly the manifest request must be sent");
        Assert.AreEqual(expectedUri, handler.Requests[0].RequestUri, "Every segment must be requested as written");
    }

    /// <summary>
    /// Verify the OCI base-image resolution sends no blob request for a configuration digest that is a dot segment
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientResolveBaseImagesAsyncDotSegmentConfigDigestEndsChainWithoutBlobRequestAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse("https://mcr.microsoft.com/v2/corp/api/manifests/1.0",
                            CreateManifestResponse("sha256:manifest",
                                                   """
                                                   { "schemaVersion": 2, "config": { "digest": ".." } }
                                                   """));

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.ResolveBaseImagesAsync(new ImageReference
                                                         {
                                                             Registry = "mcr.microsoft.com",
                                                             Repository = "corp/api",
                                                             Tag = "1.0",
                                                         },
                                                         CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A configuration digest with a dot segment must end the chain like a 404");
        Assert.IsNotNull(result.Data, "The resolution must return a base-image list");
        Assert.IsEmpty(result.Data, "A configuration digest with a dot segment must yield no base images");
        Assert.HasCount(1, handler.Requests, "Only the manifest may be requested, never the blob");
    }

    /// <summary>
    /// Verify the OCI tag lookup answers not found without a request or a log entry for a registry that is not a host with an optional port
    /// </summary>
    /// <param name="registry">Registry that is not a valid host with an optional port</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow(".")]
    [DataRow("..")]
    [DataRow("...")]
    [DataRow(".a")]
    [DataRow("a..b")]
    [DataRow("a.")]
    [DataRow("-a.com")]
    [DataRow("a_b.com")]
    [DataRow("http:")]
    [DataRow("http:evil.com")]
    [DataRow("mailto:x")]
    [DataRow("user:pw")]
    [DataRow("localhost:")]
    [DataRow("localhost:abc")]
    [DataRow("localhost:99999")]
    [DataRow("evil.com:5000:6000")]
    [DataRow("evil.com?x")]
    [DataRow("evil.com#x")]
    [DataRow("evil.com\\x")]
    [DataRow("evil.com%2Fx")]
    [DataRow("a b.com")]
    [DataRow("ex\xE4mple.com")]
    [DataRow("[::1")]
    [DataRow("[a]")]
    [DataRow("https://ghcr.io")]
    [DataRow(OverlongRegistry)]
    public async Task OciRegistryClientGetTagAsyncInvalidRegistryReturnsNotFoundWithoutRequestAsync(string registry)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        var logger = new TestLogger<OciRegistryClient>();
        var client = new OciRegistryClient(httpClient, logger);

        var result = await client.GetTagAsync(new ImageReference
                                              {
                                                  Registry = registry,
                                                  Repository = "corp/api",
                                                  Tag = "1.0",
                                              },
                                              "linux",
                                              "amd64",
                                              CancellationToken.None)
                                 .ConfigureAwait(false);

        AssertInvalidRegistryRefusal(result.Status, result.Message, handler, logger);
    }

    /// <summary>
    /// Verify the OCI tags listing answers not found without a request or a log entry for a registry that is not a host with an optional port
    /// </summary>
    /// <param name="registry">Registry that is not a valid host with an optional port</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow(".")]
    [DataRow("..")]
    [DataRow("evil.com?x")]
    [DataRow("localhost:99999")]
    public async Task OciRegistryClientGetTagsAsyncInvalidRegistryReturnsNotFoundWithoutRequestAsync(string registry)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        var logger = new TestLogger<OciRegistryClient>();
        var client = new OciRegistryClient(httpClient, logger);

        var result = await client.GetTagsAsync(registry,
                                               "corp/api",
                                               cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        AssertInvalidRegistryRefusal(result.Status, result.Message, handler, logger);
    }

    /// <summary>
    /// Verify the OCI image configuration lookup answers not found without a request or a log entry for a registry that is not a host with an optional port
    /// </summary>
    /// <param name="registry">Registry that is not a valid host with an optional port</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow(".")]
    [DataRow("..")]
    [DataRow("evil.com?x")]
    [DataRow("localhost:99999")]
    public async Task OciRegistryClientGetImageConfigurationAsyncInvalidRegistryReturnsNotFoundWithoutRequestAsync(string registry)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        var logger = new TestLogger<OciRegistryClient>();
        var client = new OciRegistryClient(httpClient, logger);

        var result = await client.GetImageConfigurationAsync(new ImageReference
                                                             {
                                                                 Registry = registry,
                                                                 Repository = "corp/api",
                                                                 Tag = "1.0",
                                                             },
                                                             CancellationToken.None)
                                 .ConfigureAwait(false);

        AssertInvalidRegistryRefusal(result.Status, result.Message, handler, logger);
    }

    /// <summary>
    /// Verify the OCI base-image resolution ends the chain without a request or a log entry for a registry that is not a host with an optional port
    /// </summary>
    /// <param name="registry">Registry that is not a valid host with an optional port</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow(".")]
    [DataRow("..")]
    [DataRow("evil.com?x")]
    [DataRow("localhost:99999")]
    public async Task OciRegistryClientResolveBaseImagesAsyncInvalidRegistryReturnsNoBaseImagesWithoutRequestAsync(string registry)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        var logger = new TestLogger<OciRegistryClient>();
        var client = new OciRegistryClient(httpClient, logger);

        var result = await client.ResolveBaseImagesAsync(new ImageReference
                                                         {
                                                             Registry = registry,
                                                             Repository = "corp/api",
                                                             Tag = "1.0",
                                                         },
                                                         CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A registry that is not a host must end the chain like a 404 and not fail the resolution");
        Assert.IsNotNull(result.Data, "The resolution must return a base-image list");
        Assert.IsEmpty(result.Data, "A registry that is not a host must yield no base images");
        Assert.IsEmpty(handler.Requests, "No request may be sent for a registry that is not a host");
        Assert.IsEmpty(logger.Entries, "Nothing may be logged for a registry that is not a host");
    }

    /// <summary>
    /// Verify the OCI tag lookup requests the host and the port of a registry with a port
    /// </summary>
    /// <param name="registry">Registry with a port</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("localhost:5000")]
    [DataRow("registry.example.com:5000")]
    [DataRow("127.0.0.1:5000")]
    [DataRow("[::1]:5000")]
    public async Task OciRegistryClientGetTagAsyncRegistryWithPortRequestsHostAndPortAsync(string registry)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        var manifestUri = $"https://{registry}/v2/corp/api/manifests/1.0";

        handler.AddResponse(manifestUri,
                            CreateManifestResponse("sha256:port-tag", """{ "schemaVersion": 2, "config": { "digest": "sha256:config" } }"""));

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagAsync(new ImageReference
                                              {
                                                  Registry = registry,
                                                  Repository = "corp/api",
                                                  Tag = "1.0",
                                              },
                                              "linux",
                                              "amd64",
                                              CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A registry with a port must be addressed and the lookup must succeed");
        Assert.IsNotNull(result.Data, "The lookup must return tag data");
        Assert.AreEqual("sha256:port-tag", result.Data.Digest, "The digest must come from the manifest of the registry with the port");
        Assert.AreSequenceEqual([new Uri(manifestUri).AbsoluteUri],
                                handler.Requests.Select(request => request.RequestUri)
                                                .ToArray(),
                                "Exactly one request must go to the host and the port of the registry");
    }

    /// <summary>
    /// Verify the OCI tags listing keeps the host and the port of a registry with a port
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task OciRegistryClientGetTagsAsyncRegistryWithPortKeepsPortAsync()
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        var tagsUri = "https://registry.example.com:5000/v2/corp/api/tags/list?n=100";

        handler.AddJsonResponse(tagsUri, """{"name":"corp/api","tags":[]}""");

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        var result = await client.GetTagsAsync("Registry.Example.com:5000",
                                               "corp/api",
                                               cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The tag listing of a registry with a port must succeed");
        Assert.AreSequenceEqual([tagsUri],
                                handler.Requests.Select(request => request.RequestUri)
                                                .ToArray(),
                                "The tag listing must request the lower-cased host with its port");
    }

    /// <summary>
    /// Answer the requests of a base-image chain: the first image carries the given config blob, every further image has no labels
    /// </summary>
    /// <param name="request">Request</param>
    /// <param name="firstBlobJson">Config blob of dotnet/aspnet</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateBaseImageChainResponse(HttpRequestMessage request, string firstBlobJson)
    {
        var path = request.RequestUri!.AbsolutePath;
        var isFirstImage = path.Contains("/dotnet/aspnet/", StringComparison.Ordinal);

        if (path.Contains("/manifests/", StringComparison.Ordinal))
        {
            return CreateManifestResponse("sha256:manifest",
                                          """
                                          { "schemaVersion": 2, "config": { "digest": "sha256:config" } }
                                          """);
        }

        if (path.Contains("/blobs/", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
                   {
                       Content = new StringContent(isFirstImage ? firstBlobJson : """{ "config": { "Labels": {} } }""",
                                                   Encoding.UTF8,
                                                   "application/json"),
                   };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    #endregion // Methods

    #region Static methods

    /// <summary>
    /// Assert a lookup was refused for a registry that is not a host with an optional port: not found with the fixed message, no request and no log entry
    /// </summary>
    /// <param name="status">Result status</param>
    /// <param name="message">Result message</param>
    /// <param name="handler">Handler that observed the requests</param>
    /// <param name="logger">Logger that captured the entries</param>
    private static void AssertInvalidRegistryRefusal(ExternalOperationStatus status,
                                                     string? message,
                                                     SequenceHttpMessageHandler handler,
                                                     TestLogger<OciRegistryClient> logger)
    {
        Assert.AreEqual(ExternalOperationStatus.NotFound, status, "A registry that is not a host must be answered as not found");
        Assert.AreEqual(RegistryReferenceValidator.InvalidRegistryMessage, message, "The message must be the fixed invalid-registry text");
        Assert.IsEmpty(handler.Requests, "No request may be sent for a registry that is not a host");
        Assert.IsEmpty(logger.Entries, "Nothing may be logged for a registry that is not a host");
    }

    /// <summary>
    /// Assert a lookup was refused for a dot segment: not found with the fixed message, no request and no log entry
    /// </summary>
    /// <param name="status">Result status</param>
    /// <param name="message">Result message</param>
    /// <param name="handler">Handler that observed the requests</param>
    /// <param name="logger">Logger that captured the entries</param>
    private static void AssertDotSegmentRefusal(ExternalOperationStatus status,
                                                string? message,
                                                SequenceHttpMessageHandler handler,
                                                TestLogger<OciRegistryClient> logger)
    {
        Assert.AreEqual(ExternalOperationStatus.NotFound, status, "A path with a dot segment must be answered as not found");
        Assert.AreEqual(RegistryReferenceValidator.DotSegmentMessage, message, "The message must be the fixed dot-segment text");
        Assert.IsEmpty(handler.Requests, "No request may be sent for a path with a dot segment");
        Assert.IsEmpty(logger.Entries, "Nothing may be logged for a path with a dot segment");
    }

    /// <summary>
    /// Look up the tag <c>latest</c> of a registry whose manifest answer carries the given digest header
    /// </summary>
    /// <param name="digestHeader">Value of the Docker-Content-Digest header</param>
    /// <returns>Lookup result</returns>
    private static async Task<ExternalOperationResult<DockerHubTagData>> LookUpTagWithDigestHeaderAsync(string digestHeader)
    {
        using var handler = new SequenceHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        handler.AddResponse("https://mcr.microsoft.com/v2/mssql/server/manifests/latest",
                            CreateManifestResponse(digestHeader, """{ "schemaVersion": 2, "config": { "digest": "sha256:config" } }"""));

        var client = new OciRegistryClient(httpClient, new TestLogger<OciRegistryClient>());

        return await client.GetTagAsync(new ImageReference
                                        {
                                            Registry = "mcr.microsoft.com",
                                            Repository = "mssql/server",
                                            Tag = "latest",
                                        },
                                        "linux",
                                        "arm64",
                                        CancellationToken.None)
                           .ConfigureAwait(false);
    }

    /// <summary>
    /// Create a tag list response with an optional Link header
    /// </summary>
    /// <param name="jsonContent">Tag list JSON</param>
    /// <param name="linkHeader">Link header value or null</param>
    /// <returns>Tag list response</returns>
    private static HttpResponseMessage CreateTagListResponse(string jsonContent, string? linkHeader)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
                       {
                           Content = new StringContent(jsonContent,
                                                       Encoding.UTF8,
                                                       "application/json"),
                       };

        if (linkHeader is not null)
        {
            response.Headers.TryAddWithoutValidation("Link", linkHeader);
        }

        return response;
    }

    /// <summary>
    /// Create an unauthorized response with a bearer challenge
    /// </summary>
    /// <param name="realm">Token realm</param>
    /// <param name="service">Registry service</param>
    /// <param name="scope">Repository scope</param>
    /// <returns>Unauthorized response</returns>
    private static HttpResponseMessage CreateUnauthorizedResponse(string realm, string service, string scope)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer",
                                                                           $"realm=\"{realm}\",service=\"{service}\",scope=\"{scope}\""));

        return response;
    }

    /// <summary>
    /// Create a manifest response with a digest header
    /// </summary>
    /// <param name="digest">Manifest digest</param>
    /// <param name="jsonContent">Manifest JSON</param>
    /// <returns>Manifest response</returns>
    private static HttpResponseMessage CreateManifestResponse(string digest, string jsonContent)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
                       {
                           Content = new StringContent(jsonContent,
                                                       Encoding.UTF8,
                                                       "application/vnd.oci.image.manifest.v1+json"),
                       };

        response.Headers.TryAddWithoutValidation("Docker-Content-Digest", digest);

        return response;
    }

    /// <summary>
    /// Create a manifest-list response with a digest header
    /// </summary>
    /// <param name="digest">Manifest-list digest</param>
    /// <param name="jsonContent">Manifest-list JSON</param>
    /// <returns>Manifest-list response</returns>
    private static HttpResponseMessage CreateManifestListResponse(string digest, string jsonContent)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
                       {
                           Content = new StringContent(jsonContent,
                                                       Encoding.UTF8,
                                                       "application/vnd.oci.image.index.v1+json"),
                       };

        response.Headers.TryAddWithoutValidation("Docker-Content-Digest", digest);

        return response;
    }

    /// <summary>
    /// Create a Bad Request response with an oversized body
    /// </summary>
    /// <param name="reasonPhrase">Reason phrase, null for the default one</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateLongErrorResponse(string? reasonPhrase)
    {
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
               {
                   ReasonPhrase = reasonPhrase,
                   Content = new StringContent(new string('x', OversizedRemoteText.BodyLength)),
               };
    }

    #endregion // Static methods
}