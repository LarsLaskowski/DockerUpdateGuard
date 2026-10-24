using System.Net;
using System.Net.Http.Headers;
using System.Text;

using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Data;
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

    #endregion // Methods

    #region Static methods

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

    #endregion // Static methods
}