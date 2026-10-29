using System.Net;
using System.Text;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Helper;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DockerHubClient"/>
/// </summary>
[TestClass]
public partial class DockerHubClientTests
{
    #region Constants

    /// <summary>
    /// Bearer token the login response hands out
    /// </summary>
    private const string LoginToken = "header.eyJleHAiOjQxMDI0NDQ4MDB9.signature";

    /// <summary>
    /// Docker Hub login response
    /// </summary>
    private const string LoginResponseJson = $$"""{ "token": "{{LoginToken}}" }""";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify registry values with an empty URI host or a port are judged by the plain host check without throwing
    /// </summary>
    /// <param name="registry">Registry value</param>
    /// <param name="expected">Expected support result</param>
    [TestMethod]
    [DataRow("localhost:5000", false)]
    [DataRow("user:pw", false)]
    [DataRow("mailto:x", false)]
    [DataRow("docker.io", true)]
    [DataRow("https://index.docker.io", true)]
    [DataRow("", false)]
    public void DockerHubClientSupportsRegistryHostlessOrPortValueReturnsExpected(string registry, bool expected)
    {
        var result = DockerHubClient.SupportsRegistry(registry);

        Assert.AreEqual(expected, result, $"SupportsRegistry(\"{registry}\") must not throw and must return {expected}.");
    }

    /// <summary>
    /// Verify the current Docker Hub account can be resolved from the authenticated user endpoint
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetCurrentUserAsyncReturnsAuthenticatedAccountAsync()
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            httpClient.BaseAddress = new Uri("https://hub.docker.com/");
            handler.AddResponse("https://hub.docker.com/v2/users/login",
                                """
                                {
                                  "token": "header.eyJleHAiOjQxMDI0NDQ4MDB9.signature"
                                }
                                """);
            handler.AddResponse("https://hub.docker.com/v2/user/",
                                """
                                {
                                  "username": "acme"
                                }
                                """);

            var client = CreateClient(httpClient);

            var result = await client.GetCurrentUserAsync(CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Current-user lookup must succeed when Docker Hub returns a username");
            Assert.IsNotNull(result.Data, "Current-user lookup must return the authenticated account payload");
            Assert.AreEqual("acme",
                            result.Data.UserName,
                            "Current-user lookup must expose the authenticated Docker Hub username");
            Assert.Contains(request => request.RequestUri == "https://hub.docker.com/v2/user/"
                                       && request.AuthorizationScheme == "Bearer"
                                       && request.AuthorizationParameter == "header.eyJleHAiOjQxMDI0NDQ4MDB9.signature",
                            handler.Requests,
                            "Current-user lookup must authenticate with the exchanged Docker Hub bearer token");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify Docker Hub URL aliases are normalized to the API host and remain supported
    /// </summary>
    [TestMethod]
    public void DockerHubClientGetBaseUriDockerDotComRegistryReturnsDockerHubApiBaseUri()
    {
        var options = new DockerHubOptions
                      {
                          Registry = "https://docker.com/",
                      };

        var baseUri = DockerHubClient.GetBaseUri(options);

        Assert.AreEqual(new Uri("https://hub.docker.com/"),
                        baseUri,
                        "Docker Hub URL aliases must be normalized to the Docker Hub API base URI");
    }

    /// <summary>
    /// Verify a configured Docker Hub API base address overrides the built-in default
    /// </summary>
    [TestMethod]
    public void DockerHubClientGetBaseUriConfiguredApiBaseUrlReturnsConfiguredUri()
    {
        var options = new DockerHubOptions
                      {
                          Registry = "docker.io",
                          ApiBaseUrl = "https://hub.mirror.example.test/",
                      };

        var baseUri = DockerHubClient.GetBaseUri(options);

        Assert.AreEqual(new Uri("https://hub.mirror.example.test/"),
                        baseUri,
                        "A configured Docker Hub API base address must replace the built-in default");
    }

    /// <summary>
    /// Verify Docker Hub repository listing follows pagination links
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetRepositoriesAsyncReadsAllRepositoryPagesAsync()
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            httpClient.BaseAddress = new Uri("https://hub.docker.com/");
            handler.AddResponse("https://hub.docker.com/v2/users/login",
                                """
                                {
                                  "token": "header.eyJleHAiOjQxMDI0NDQ4MDB9.signature"
                                }
                                """);
            handler.AddResponse("https://hub.docker.com/v2/repositories/acme/?page_size=100",
                                """
                                {
                                  "next": "https://hub.docker.com/v2/repositories/acme/?page=2&page_size=100",
                                  "results": [
                                    {
                                      "namespace": "acme",
                                      "name": "api",
                                      "description": "API service",
                                      "last_updated": "2025-04-01T10:00:00Z"
                                    }
                                  ]
                                }
                                """);
            handler.AddResponse("https://hub.docker.com/v2/repositories/acme/?page=2&page_size=100",
                                """
                                {
                                  "next": null,
                                  "results": [
                                    {
                                      "namespace": "acme",
                                      "name": "web",
                                      "description": "Web frontend",
                                      "last_updated": "2025-04-02T10:00:00Z"
                                    }
                                  ]
                                }
                                """);

            var client = CreateClient(httpClient);

            var result = await client.GetRepositoriesAsync("acme", CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Repository listing must succeed when Docker Hub returns paged repository payloads");
            Assert.IsNotNull(result.Data, "Repository listing must return repository data");
            Assert.HasCount(2,
                            result.Data,
                            "Repository listing must include repositories from all result pages");
            Assert.Contains(entity => entity.Repository == "acme/api", result.Data, "Repository listing must normalize the namespace and repository name");
            Assert.Contains(entity => entity.Repository == "acme/web", result.Data, "Repository listing must follow the Docker Hub pagination link");
            Assert.ContainsSingle(request => request.RequestUri == "https://hub.docker.com/v2/users/login",
                                  handler.Requests,
                                  "Repository listing must request a Docker Hub bearer token once and reuse it for paged requests");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify Docker Hub tag listing follows pagination links
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetTagsAsyncReadsAllTagPagesAsync()
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            httpClient.BaseAddress = new Uri("https://hub.docker.com/");
            handler.AddResponse("https://hub.docker.com/v2/users/login",
                                """
                                {
                                  "token": "header.eyJleHAiOjQxMDI0NDQ4MDB9.signature"
                                }
                                """);
            handler.AddResponse("https://hub.docker.com/v2/namespaces/acme/repositories/api/tags?page_size=100",
                                """
                                {
                                  "next": "https://hub.docker.com/v2/namespaces/acme/repositories/api/tags?page=2&page_size=100",
                                  "results": [
                                    {
                                      "name": "latest",
                                      "digest": "sha256:latest",
                                      "last_pushed": "2025-04-02T10:00:00Z"
                                    }
                                  ]
                                }
                                """);
            handler.AddResponse("https://hub.docker.com/v2/namespaces/acme/repositories/api/tags?page=2&page_size=100",
                                """
                                {
                                  "next": null,
                                  "results": [
                                    {
                                      "name": "2.4.1",
                                      "digest": "sha256:241",
                                      "last_pushed": "2025-04-01T10:00:00Z"
                                    }
                                  ]
                                }
                                """);

            var client = CreateClient(httpClient);

            var result = await client.GetTagsAsync("docker.io", "acme/api", cancellationToken: CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Tag listing must succeed when Docker Hub returns paged tag payloads");
            Assert.IsNotNull(result.Data, "Tag listing must return tag metadata");
            Assert.AreSequenceEqual(["latest", "2.4.1"],
                                    result.Data.Select(entity => entity.Tag)
                                               .ToArray(),
                                    "Tag listing must include tags from every result page");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify Docker Hub tag listing stops once older and lower-version tags are no longer relevant
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetTagsAsyncStopsAfterBaselineAndSkipsLowerVersionsAsync()
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            httpClient.BaseAddress = new Uri("https://hub.docker.com/");
            handler.AddResponse("https://hub.docker.com/v2/users/login",
                                """
                                {
                                  "token": "header.eyJleHAiOjQxMDI0NDQ4MDB9.signature"
                                }
                                """);
            handler.AddResponse("https://hub.docker.com/v2/namespaces/acme/repositories/api/tags?page_size=100",
                                """
                                {
                                  "next": "https://hub.docker.com/v2/namespaces/acme/repositories/api/tags?page=2&page_size=100",
                                  "results": [
                                    {
                                      "name": "3.0.0",
                                      "digest": "sha256:300",
                                      "last_pushed": "2025-04-03T10:00:00Z"
                                    },
                                    {
                                      "name": "2.4.1",
                                      "digest": "sha256:241",
                                      "last_pushed": "2025-04-02T10:00:00Z"
                                    },
                                    {
                                      "name": "2.4.0",
                                      "digest": "sha256:240",
                                      "last_pushed": "2025-04-02T10:00:00Z"
                                    },
                                    {
                                      "name": "2.3.9",
                                      "digest": "sha256:239",
                                      "last_pushed": "2025-04-01T10:00:00Z"
                                    }
                                  ]
                                }
                                """);

            var client = CreateClient(httpClient);
            var result = await client.GetTagsAsync("docker.io",
                                                   "acme/api",
                                                   queryOptions: new RegistryTagQueryOptions
                                                                 {
                                                                     CurrentTag = "2.4.1",
                                                                     MaximumTags = 50,
                                                                     MinimumVersionTag = "2.4.1",
                                                                     PublishedSinceUtc = new DateTimeOffset(2025, 04, 02, 10, 00, 00, TimeSpan.Zero),
                                                                 },
                                                   cancellationToken: CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Bounded Docker Hub tag listing must still succeed");
            Assert.IsNotNull(result.Data, "Bounded Docker Hub tag listing must return tag data");
            Assert.AreSequenceEqual(["3.0.0", "2.4.1"],
                                    result.Data.Select(entity => entity.Tag)
                                               .ToArray(),
                                    "Bounded Docker Hub tag listing must skip lower versions and stop before older pages");
            Assert.DoesNotContain(request => request.RequestUri == "https://hub.docker.com/v2/namespaces/acme/repositories/api/tags?page=2&page_size=100",
                                  handler.Requests,
                                  "Bounded Docker Hub tag listing must not request later pages after the publish-time cutoff");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a Docker Hub tag listing that stops on its own condition ignores a foreign next-page link
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetTagsAsyncStopConditionIgnoresForeignNextLinkAsync()
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            httpClient.BaseAddress = new Uri("https://hub.docker.com/");
            handler.AddResponse("https://hub.docker.com/v2/users/login",
                                """
                                {
                                  "token": "header.eyJleHAiOjQxMDI0NDQ4MDB9.signature"
                                }
                                """);
            handler.AddResponse("https://hub.docker.com/v2/namespaces/acme/repositories/api/tags?page_size=100",
                                """
                                {
                                  "next": "https://attacker.example/collect?page=2&token=leak",
                                  "results": [
                                    {
                                      "name": "3.0.0",
                                      "digest": "sha256:300",
                                      "last_pushed": "2025-04-03T10:00:00Z"
                                    },
                                    {
                                      "name": "2.4.1",
                                      "digest": "sha256:241",
                                      "last_pushed": "2025-04-02T10:00:00Z"
                                    },
                                    {
                                      "name": "2.4.0",
                                      "digest": "sha256:240",
                                      "last_pushed": "2025-04-02T10:00:00Z"
                                    },
                                    {
                                      "name": "2.3.9",
                                      "digest": "sha256:239",
                                      "last_pushed": "2025-04-01T10:00:00Z"
                                    }
                                  ]
                                }
                                """);

            var client = CreateClient(httpClient);
            var result = await client.GetTagsAsync("docker.io",
                                                   "acme/api",
                                                   queryOptions: new RegistryTagQueryOptions
                                                                 {
                                                                     CurrentTag = "2.4.1",
                                                                     MaximumTags = 50,
                                                                     MinimumVersionTag = "2.4.1",
                                                                     PublishedSinceUtc = new DateTimeOffset(2025, 04, 02, 10, 00, 00, TimeSpan.Zero),
                                                                 },
                                                   cancellationToken: CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "A stop condition must end the listing before the foreign link is looked at");
            Assert.IsNotNull(result.Data, "Bounded Docker Hub tag listing must return tag data");
            Assert.AreSequenceEqual(["3.0.0", "2.4.1"],
                                    result.Data.Select(entity => entity.Tag)
                                               .ToArray(),
                                    "Bounded Docker Hub tag listing must skip lower versions and stop before older pages");
            Assert.DoesNotContain(request => request.RequestUri.StartsWith("https://attacker.example", StringComparison.Ordinal),
                                  handler.Requests,
                                  "Bounded Docker Hub tag listing must not request the foreign next-page link");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a repository page without a results array adds no repositories and does not stop the listing
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetRepositoriesAsyncPageWithoutResultsArraySkipsPageAsync()
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            httpClient.BaseAddress = new Uri("https://hub.docker.com/");
            handler.AddResponse("https://hub.docker.com/v2/users/login",
                                """
                                {
                                  "token": "header.eyJleHAiOjQxMDI0NDQ4MDB9.signature"
                                }
                                """);
            handler.AddResponse("https://hub.docker.com/v2/repositories/acme/?page_size=100",
                                """
                                {
                                  "next": "https://hub.docker.com/v2/repositories/acme/?page=2&page_size=100",
                                  "results": "unexpected"
                                }
                                """);
            handler.AddResponse("https://hub.docker.com/v2/repositories/acme/?page=2&page_size=100",
                                """
                                {
                                  "next": null,
                                  "results": [
                                    {
                                      "namespace": "acme",
                                      "name": "web",
                                      "description": "Web frontend",
                                      "last_updated": "2025-04-02T10:00:00Z"
                                    }
                                  ]
                                }
                                """);

            var client = CreateClient(httpClient);

            var result = await client.GetRepositoriesAsync("acme", CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "A page whose results are not an array must not fail the listing");
            Assert.IsNotNull(result.Data, "Repository listing must return repository data");
            Assert.AreSequenceEqual(["acme/web"],
                                    result.Data.Select(entity => entity.Repository)
                                               .ToArray(),
                                    "Only the repositories of pages with a results array must be listed");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify Docker Hub tag lookup uses the top-level Docker Hub digest
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetTagAsyncUsesTopLevelDigestAsync()
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            httpClient.BaseAddress = new Uri("https://hub.docker.com/");
            handler.AddResponse("https://hub.docker.com/v2/users/login",
                                """
                                {
                                  "token": "header.eyJleHAiOjQxMDI0NDQ4MDB9.signature"
                                }
                                """);
            handler.AddResponse("https://hub.docker.com/v2/namespaces/acme/repositories/api/tags/latest",
                                """
                                {
                                  "name": "latest",
                                  "digest": "sha256:index",
                                  "last_pushed": "2025-04-02T10:00:00Z",
                                  "images": [
                                    {
                                      "architecture": "amd64",
                                      "os": "linux",
                                      "digest": "sha256:amd64"
                                    },
                                    {
                                      "architecture": "arm64",
                                      "os": "linux",
                                      "digest": "sha256:arm64"
                                    }
                                  ]
                                }
                                """);

            var client = CreateClient(httpClient);
            var result = await client.GetTagAsync(new ImageReference
                                                  {
                                                      Registry = "docker.io",
                                                      Repository = "acme/api",
                                                      Tag = "latest",
                                                  },
                                                  "linux",
                                                  "arm64",
                                                  CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Tag lookup must succeed when Docker Hub returns tag details");
            Assert.IsNotNull(result.Data, "Tag lookup must return tag metadata");
            Assert.AreEqual("sha256:index",
                            result.Data.Digest,
                            "Tag lookup must use the top-level Docker Hub digest instead of a per-image entry digest");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify Docker Hub base-image resolution strips an embedded image reference from the base digest label
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientResolveBaseImagesAsyncNormalizesDigestLabelReferenceAsync()
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            httpClient.BaseAddress = new Uri("https://hub.docker.com/");
            handler.AddResponse("https://auth.docker.io/token?service=registry.docker.io&scope=repository:library/app:pull",
                                """
                                {
                                  "token": "registry-token"
                                }
                                """);
            handler.AddResponse("https://registry-1.docker.io/v2/library/app/manifests/latest",
                                """
                                {
                                  "schemaVersion": 2,
                                  "config": {
                                    "digest": "sha256:app-config"
                                  }
                                }
                                """);
            handler.AddResponse("https://registry-1.docker.io/v2/library/app/blobs/sha256%3Aapp-config",
                                """
                                {
                                  "config": {
                                    "Labels": {
                                      "org.opencontainers.image.base.name": "debian:12",
                                      "org.opencontainers.image.base.digest": "docker.io/library/debian@sha256:debian"
                                    }
                                  }
                                }
                                """);
            handler.AddResponse("https://auth.docker.io/token?service=registry.docker.io&scope=repository:library/debian:pull",
                                """
                                {
                                  "token": "base-registry-token"
                                }
                                """);
            handler.AddResponse("https://registry-1.docker.io/v2/library/debian/manifests/sha256%3Adebian",
                                """
                                {
                                  "schemaVersion": 2,
                                  "config": {
                                    "digest": "sha256:debian-config"
                                  }
                                }
                                """);
            handler.AddResponse("https://registry-1.docker.io/v2/library/debian/blobs/sha256%3Adebian-config",
                                """
                                {
                                  "config": {
                                    "Labels": {}
                                  }
                                }
                                """);

            var client = CreateClient(httpClient);
            var result = await client.ResolveBaseImagesAsync(new ImageReference
                                                             {
                                                                 Registry = "docker.io",
                                                                 Repository = "library/app",
                                                                 Tag = "latest",
                                                             },
                                                             CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Docker Hub base-image resolution must succeed when the manifest and config blob can be read");
            Assert.IsNotNull(result.Data, "Docker Hub base-image resolution must return resolved base images");
            Assert.HasCount(1,
                            result.Data,
                            "Docker Hub base-image resolution must return the discovered base image");
            Assert.AreEqual("sha256:debian",
                            result.Data[0].Digest,
                            "Docker Hub base-image resolution must strip the image reference prefix from the base digest label");
            Assert.Contains(entry => entry.RequestUri == "https://registry-1.docker.io/v2/library/debian/manifests/sha256%3Adebian",
                            handler.Requests,
                            "Docker Hub base-image recursion must use the normalized digest when resolving the parent image chain");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify Docker Hub base-image resolution selects a real platform manifest and skips attestation manifests
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientResolveBaseImagesAsyncSkipsAttestationManifestAsync()
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            httpClient.BaseAddress = new Uri("https://hub.docker.com/");
            handler.AddResponse("https://auth.docker.io/token?service=registry.docker.io&scope=repository:library/app:pull",
                                """
                                {
                                  "token": "registry-token"
                                }
                                """);
            handler.AddResponse("https://registry-1.docker.io/v2/library/app/manifests/latest",
                                """
                                {
                                  "schemaVersion": 2,
                                  "mediaType": "application/vnd.oci.image.index.v1+json",
                                  "manifests": [
                                    {
                                      "digest": "sha256:attestation",
                                      "platform": {
                                        "os": "unknown",
                                        "architecture": "unknown"
                                      }
                                    },
                                    {
                                      "digest": "sha256:amd64",
                                      "platform": {
                                        "os": "linux",
                                        "architecture": "amd64"
                                      }
                                    }
                                  ]
                                }
                                """,
                                "application/vnd.oci.image.index.v1+json");
            handler.AddResponse("https://registry-1.docker.io/v2/library/app/manifests/sha256%3Aamd64",
                                """
                                {
                                  "schemaVersion": 2,
                                  "config": {
                                    "digest": "sha256:app-config"
                                  }
                                }
                                """);
            handler.AddResponse("https://registry-1.docker.io/v2/library/app/blobs/sha256%3Aapp-config",
                                """
                                {
                                  "config": {
                                    "Labels": {
                                      "org.opencontainers.image.base.name": "debian:12"
                                    }
                                  }
                                }
                                """);
            handler.AddResponse("https://auth.docker.io/token?service=registry.docker.io&scope=repository:library/debian:pull",
                                """
                                {
                                  "token": "base-registry-token"
                                }
                                """);
            handler.AddResponse("https://registry-1.docker.io/v2/library/debian/manifests/12",
                                """
                                {
                                  "schemaVersion": 2,
                                  "config": {
                                    "digest": "sha256:debian-config"
                                  }
                                }
                                """);
            handler.AddResponse("https://registry-1.docker.io/v2/library/debian/blobs/sha256%3Adebian-config",
                                """
                                {
                                  "config": {
                                    "Labels": {}
                                  }
                                }
                                """);

            var client = CreateClient(httpClient);
            var result = await client.ResolveBaseImagesAsync(new ImageReference
                                                             {
                                                                 Registry = "docker.io",
                                                                 Repository = "library/app",
                                                                 Tag = "latest",
                                                             },
                                                             CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Docker Hub base-image resolution must succeed when a platform manifest can be selected from the manifest list");
            Assert.IsNotNull(result.Data, "Docker Hub base-image resolution must return resolved base images");
            Assert.HasCount(1,
                            result.Data,
                            "Docker Hub base-image resolution must return the discovered base image");
            Assert.AreEqual("debian",
                            result.Data[0].Repository.Split('/')[^1],
                            "Docker Hub base-image resolution must resolve the base image from the selected platform manifest config");
            Assert.Contains(entry => entry.RequestUri == "https://registry-1.docker.io/v2/library/app/manifests/sha256%3Aamd64",
                            handler.Requests,
                            "Docker Hub manifest-list selection must fetch the real linux/amd64 platform manifest");
            Assert.DoesNotContain(entry => entry.RequestUri == "https://registry-1.docker.io/v2/library/app/manifests/sha256%3Aattestation",
                                  handler.Requests,
                                  "Docker Hub manifest-list selection must skip attestation manifests with an unknown platform");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify outbound Docker Hub requests honor the configured parallelism limit
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientSendHonorsMaxParallelRequestsAsync()
    {
        var handler = new ConcurrencyTrackingHttpMessageHandler("""{ "username": "acme" }""", TimeSpan.FromMilliseconds(50));
        var httpClient = new HttpClient(handler);

        try
        {
            httpClient.BaseAddress = new Uri("https://hub.docker.com/");

            var options = new DockerUpdateGuardOptions
                          {
                              DockerHub = new DockerHubOptions
                                          {
                                              Registry = "docker.io",
                                              UserName = "acme",
                                              MaxParallelRequests = 2,
                                          },
                          };
            var client = new DockerHubClient(httpClient,
                                             new TestLogger<DockerHubClient>(),
                                             new TestOptionsMonitor<DockerUpdateGuardOptions>(options));
            var requests = Enumerable.Range(0, 8)
                                     .Select(_ => client.GetCurrentUserAsync(CancellationToken.None))
                                     .ToArray();

            await Task.WhenAll(requests).ConfigureAwait(false);

            Assert.IsTrue(requests.All(task => task.Result.Status == ExternalOperationStatus.Succeeded),
                          "All throttled Docker Hub requests must complete successfully");
            Assert.IsLessThanOrEqualTo(2,
                                       handler.MaxObservedConcurrency,
                                       $"Outbound Docker Hub parallelism must never exceed MaxParallelRequests; observed {handler.MaxObservedConcurrency}");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify Docker Hub tag listing refuses a next-page link on another origin and keeps the token at home
    /// </summary>
    /// <param name="nextLink">Next-page link returned by Docker Hub</param>
    /// <param name="expectedOrigin">Expected origin text of the rejected link</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("https://attacker.example/collect?page=2&token=leak", "https://attacker.example")]
    [DataRow("http://hub.docker.com/v2/namespaces/acme/repositories/api/tags?page=2&page_size=100", "http://hub.docker.com")]
    [DataRow("https://hub.docker.com:8443/v2/namespaces/acme/repositories/api/tags?page=2&page_size=100", "https://hub.docker.com:8443")]
    public async Task DockerHubClientGetTagsAsyncForeignNextLinkReturnsFailedAsync(string nextLink, string expectedOrigin)
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            httpClient.BaseAddress = new Uri("https://hub.docker.com/");
            handler.AddResponse("https://hub.docker.com/v2/users/login", LoginResponseJson);
            handler.AddResponse("https://hub.docker.com/v2/namespaces/acme/repositories/api/tags?page_size=100",
                                $$"""
                                  {
                                    "next": "{{nextLink}}",
                                    "results": [
                                      {
                                        "name": "latest",
                                        "digest": "sha256:latest",
                                        "last_pushed": "2025-04-02T10:00:00Z"
                                      }
                                    ]
                                  }
                                  """);

            var logger = new TestLogger<DockerHubClient>();
            var client = CreateClient(httpClient, logger: logger);

            var result = await client.GetTagsAsync("docker.io", "acme/api", cancellationToken: CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Failed,
                            result.Status,
                            "A next-page link on another origin must fail the tag listing");
            Assert.AreEqual($"Docker Hub tag listing for 'acme/api' stopped: the next-page link pointed to a different origin ({expectedOrigin})",
                            result.Message,
                            "The failure message must name the rejected origin only");
            AssertNothingSentToForeignOrigin(handler, expectedOrigin);
            AssertSingleRejectionWarning(logger, expectedOrigin);
            PaginationLeakAssert.DoesNotLeak(string.Join('\n', logger.Entries.Select(entry => entry.Message).Append(result.Message)),
                                             LoginToken);
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify Docker Hub repository listing refuses a next-page link on another origin and keeps the token at home
    /// </summary>
    /// <param name="nextLink">Next-page link returned by Docker Hub</param>
    /// <param name="expectedOrigin">Expected origin text of the rejected link</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("https://attacker.example/collect?page=2&token=leak", "https://attacker.example")]
    public async Task DockerHubClientGetRepositoriesAsyncForeignNextLinkReturnsFailedAsync(string nextLink, string expectedOrigin)
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            httpClient.BaseAddress = new Uri("https://hub.docker.com/");
            handler.AddResponse("https://hub.docker.com/v2/users/login", LoginResponseJson);
            handler.AddResponse("https://hub.docker.com/v2/repositories/acme/?page_size=100",
                                $$"""
                                  {
                                    "next": "{{nextLink}}",
                                    "results": [
                                      {
                                        "namespace": "acme",
                                        "name": "api",
                                        "description": "API service",
                                        "last_updated": "2025-04-01T10:00:00Z"
                                      }
                                    ]
                                  }
                                  """);

            var logger = new TestLogger<DockerHubClient>();
            var client = CreateClient(httpClient, logger: logger);

            var result = await client.GetRepositoriesAsync("acme", CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Failed,
                            result.Status,
                            "A next-page link on another origin must fail the repository listing");
            Assert.AreEqual($"Docker Hub repository listing for 'acme' stopped: the next-page link pointed to a different origin ({expectedOrigin})",
                            result.Message,
                            "The failure message must name the rejected origin only");
            AssertNothingSentToForeignOrigin(handler, expectedOrigin);
            AssertSingleRejectionWarning(logger, expectedOrigin);
            PaginationLeakAssert.DoesNotLeak(string.Join('\n', logger.Entries.Select(entry => entry.Message).Append(result.Message)),
                                             LoginToken);
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a transport failure during Docker Hub base-image resolution reports the fixed connection-failed reason
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientResolveBaseImagesAsyncWhenTransportFailsReportsConnectionFailedAsync()
    {
        var injected = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (auth.internal.test:443)");
        var logger = new TestLogger<DockerHubClient>();
        var imageReference = CreateTransportFailureReference();

        using var handler = new RetryProbeHttpMessageHandler([null], HttpStatusCode.OK)
                            {
                                FailureFactory = () => injected,
                            };
        using var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient, logger: logger);

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
    /// Verify a transport failure during an image configuration lookup reports the fixed connection-failed reason and logs the exception
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetImageConfigurationAsyncWhenTransportFailsReportsConnectionFailedAsync()
    {
        var injected = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (auth.internal.test:443)");
        var logger = new TestLogger<DockerHubClient>();
        var imageReference = CreateTransportFailureReference();

        using var handler = new RetryProbeHttpMessageHandler([null], HttpStatusCode.OK)
                            {
                                FailureFactory = () => injected,
                            };
        using var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetImageConfigurationAsync(imageReference, CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A transport failure must fail the configuration lookup");
        Assert.AreEqual($"Image configuration lookup failed for '{imageReference.FullReference}': connection failed",
                        result.Message,
                        "A transport failure must report the fixed connection-failed reason");
        Assert.IsNotNull(result.Message, "A failed result must carry a message");
        Assert.DoesNotContain("internal.test", result.Message, "The exception text must not reach the result message");
        Assert.Contains(entry => entry.EventId.Id == 3212 && ReferenceEquals(entry.Exception, injected),
                        logger.Entries,
                        "The transport failure must be logged with the exception");
    }

    /// <summary>
    /// Verify a Docker Hub tag lookup answered with a long error body reports an excerpt of the body
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetTagsAsyncWhenErrorBodyIsLongReturnsBoundedExcerptAsync()
    {
        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, CreateLongErrorResponse));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient);

        var result = await client.GetTagsAsync("docker.io", "acme/api", cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A Bad Request answer must fail the tag lookup");
        Assert.AreEqual($"Tag lookup failed for 'acme/api'. Status code: 400. Body: {OversizedRemoteText.CreateExpectedExcerpt()}",
                        result.Message,
                        "The failure message must keep its text and carry the first 500 characters of the body followed by the ellipsis");
    }

    /// <summary>
    /// Verify the tag lookup message of the longest stored repository stays within the assessment message limit
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetTagsAsyncWhenRepositoryIsAtStoredLimitKeepsMessageWithinAssessmentLimitAsync()
    {
        var repository = $"acme/{new string('r', 295)}";

        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, CreateLongErrorResponse));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient);

        var result = await client.GetTagsAsync("docker.io", repository, cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A Bad Request answer must fail the tag lookup");
        Assert.IsNotNull(result.Message, "A failed result must carry a message");
        Assert.StartsWith($"Tag lookup failed for '{repository}'", result.Message, "The failure message must name the repository");
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
    public async Task DockerHubClientGetTagsAsyncWhenForeignLinkHostIsOversizedKeepsMessageWithinAssessmentLimitAsync()
    {
        var repository = $"acme/{new string('r', 295)}";
        var nextLink = $"https://{OversizedRemoteText.CreateOversizedHost()}/collect?page=2";

        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateTagPageResponse(nextLink)));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient);

        var result = await client.GetTagsAsync("docker.io", repository, cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A next-page link on another origin must fail the tag lookup");
        Assert.IsNotNull(result.Message, "A failed result must carry a message");
        Assert.EndsWith("…)", result.Message, "The rejected origin must be cut and end with the ellipsis");
        Assert.IsLessThanOrEqualTo(OversizedRemoteText.AssessmentMessageLimit,
                                   result.Message.Length,
                                   "The message is stored in a 2000-character column and must not exceed it");
    }

    /// <summary>
    /// Verify the Docker Hub tag listing leaves out records whose name is not a valid tag
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetTagsAsyncOmitsInvalidTagNamesAsync()
    {
        var oversizedName = new string('a', 129);
        var json = $$"""
                     {
                       "next": null,
                       "results": [
                         { "name": "1.0", "digest": "sha256:aaa", "last_pushed": "2025-04-04T10:00:00Z" },
                         { "name": "{{oversizedName}}", "digest": "sha256:bbb", "last_pushed": "2025-04-03T10:00:00Z" },
                         { "name": "bad\u000A", "digest": "sha256:ccc", "last_pushed": "2025-04-02T10:00:00Z" },
                         { "name": "2.0", "digest": "sha256:ddd", "last_pushed": "2025-04-01T10:00:00Z" }
                       ]
                     }
                     """;

        Assert.Contains("\\u000A", json, "The test input must carry the escaped line feed");

        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateJsonResponse(json)));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient);

        var result = await client.GetTagsAsync("docker.io", "acme/api", cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The listing must succeed when some records have invalid names");
        Assert.IsNotNull(result.Data, "The listing must return the valid records");
        Assert.AreSequenceEqual(["1.0", "2.0"],
                                result.Data.Select(entity => entity.Tag)
                                           .ToArray(),
                                "The listing must leave out records with invalid names and keep the others in order");
    }

    /// <summary>
    /// Verify the Docker Hub tag listing keeps a tag whose digest is invalid and reports the digest as unknown
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetTagsAsyncInvalidDigestIsUnknownAsync()
    {
        var oversizedDigest = "sha256:" + new string('a', 10000);
        var json = $$"""
                     {
                       "next": null,
                       "results": [
                         { "name": "1.0", "digest": "{{oversizedDigest}}", "last_pushed": "2025-04-04T10:00:00Z" }
                       ]
                     }
                     """;

        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateJsonResponse(json)));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient);

        var result = await client.GetTagsAsync("docker.io", "acme/api", cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The listing must succeed when a digest is invalid");
        Assert.IsNotNull(result.Data, "The listing must return the record");

        var tag = Assert.ContainsSingle(result.Data, "The record with the invalid digest must be kept");

        Assert.AreEqual("1.0", tag.Tag, "The record must keep its tag");
        Assert.IsNull(tag.Digest, "An invalid digest must be reported as unknown");
    }

    /// <summary>
    /// Verify the Docker Hub tag lookup reports the requested tag and no digest for an oversized record
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetTagAsyncOversizedNameAndDigestReturnsRequestedTagWithoutDigestAsync()
    {
        var json = $$"""
                     {
                       "name": "{{new string('n', 10000)}}",
                       "digest": "sha256:{{new string('a', 10000)}}",
                       "last_pushed": "2025-04-02T10:00:00Z"
                     }
                     """;

        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateJsonResponse(json)));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient);

        var result = await client.GetTagAsync(new ImageReference
                                              {
                                                  Registry = "docker.io",
                                                  Repository = "acme/api",
                                                  Tag = "latest",
                                              },
                                              "linux",
                                              "arm64",
                                              CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The lookup must succeed for an oversized record");
        Assert.IsNotNull(result.Data, "The lookup must return tag data");
        Assert.AreEqual("latest", result.Data.Tag, "The lookup must report the requested tag, not the record's name");
        Assert.IsNull(result.Data.Digest, "An oversized digest must be reported as unknown");
    }

    /// <summary>
    /// Verify the Docker Hub tag lookup answers not found without a request or a log entry for an invalid requested tag
    /// </summary>
    /// <param name="tag">Requested tag</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("..")]
    [DataRow("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task DockerHubClientGetTagAsyncInvalidRequestedTagReturnsNotFoundWithoutRequestAsync(string tag)
    {
        using var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        Assert.IsGreaterThan(0, tag.Length, "The requested tag must not be empty");

        var result = await client.GetTagAsync(new ImageReference
                                              {
                                                  Registry = "docker.io",
                                                  Repository = "acme/api",
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
        Assert.IsEmpty(handler.Requests, "No HTTP request may be sent for an invalid requested tag");
        Assert.IsEmpty(logger.Entries, "Nothing may be logged for an invalid requested tag");
    }

    /// <summary>
    /// Verify the Docker Hub image configuration replaces control characters in environment variables, label values, operating system and architecture
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetImageConfigurationAsyncWhenFieldsHaveControlCharactersReplacesThemAsync()
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

        using var handler = new DelegateHttpMessageHandler(request => CreateBaseImageChainResponse(request, blobJson));
        using var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);

        var result = await client.GetImageConfigurationAsync(new ImageReference
                                                             {
                                                                 Registry = "docker.io",
                                                                 Repository = "library/app",
                                                                 Tag = "latest",
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
    /// Verify the Docker Hub base-image resolution replaces control characters in the base-image labels before it parses them
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientResolveBaseImagesAsyncWhenBaseImageLabelsHaveControlCharactersReplacesThemAsync()
    {
        var blobJson = """
                       {
                         "config": {
                           "Labels": {
                             "org.opencontainers.image.base.name": "\u001Bdocker.io/library/debian:12\u202E",
                             "org.opencontainers.image.base.digest": "\u200Bsha256:abcd"
                           }
                         }
                       }
                       """;

        Assert.Contains("\\u202E", blobJson, "The test input must carry the escaped format character");
        Assert.Contains("\\u001B", blobJson, "The test input must carry the escaped escape character");

        using var handler = new DelegateHttpMessageHandler(request => CreateBaseImageChainResponse(request, blobJson));
        using var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);

        var result = await client.ResolveBaseImagesAsync(new ImageReference
                                                         {
                                                             Registry = "docker.io",
                                                             Repository = "library/app",
                                                             Tag = "latest",
                                                         },
                                                         CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The base-image resolution must succeed for well-formed blobs");
        Assert.IsNotNull(result.Data, "The base-image resolution must return the base images");

        var baseImage = Assert.ContainsSingle(result.Data, "The base-image resolution must return the one base image of the chain");

        Assert.AreEqual("docker.io", baseImage.Registry, "The registry of the base image must be kept");
        Assert.AreEqual("library/debian", baseImage.Repository, "The replaced characters at the edges of the name must be trimmed off the repository");
        Assert.AreEqual("12", baseImage.Tag, "The replaced characters at the edges of the name must be trimmed off the tag");
        Assert.AreEqual("sha256:abcd", baseImage.Digest, "The replaced character at the edge of the digest must be trimmed off the digest");
    }

    /// <summary>
    /// Verify the Docker Hub repository listing replaces control characters in the repository name and the description
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetRepositoriesAsyncWhenFieldsHaveControlCharactersReplacesThemAsync()
    {
        var json = """
                   {
                     "next": null,
                     "results": [
                       {
                         "namespace": "\u001Bacme",
                         "name": "api\u202E",
                         "description": "De\u001Bsc\u200Bription",
                         "last_updated": "2025-04-01T10:00:00Z"
                       }
                     ]
                   }
                   """;

        Assert.Contains("\\u202E", json, "The test input must carry the escaped format character");
        Assert.Contains("\\u001B", json, "The test input must carry the escaped escape character");

        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateJsonResponse(json)));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient);

        var result = await client.GetRepositoriesAsync("acme", CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The listing must succeed for a well-formed answer");
        Assert.IsNotNull(result.Data, "The listing must return the repositories");

        var repository = Assert.ContainsSingle(result.Data, "The listing must return the one repository");

        Assert.AreEqual("acme/api", repository.Repository, "The replaced characters at the edges of namespace and name must be trimmed off the repository name");
        Assert.AreEqual("De sc ription", repository.Description, "The description must be cleaned");
    }

    /// <summary>
    /// Verify the Docker Hub base-image resolution ends with an empty chain at a label that is not a valid reference
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientResolveBaseImagesAsyncWhenBaseImageLabelIsInvalidReturnsEmptyChainAsync()
    {
        var blobJson = $$"""
                         { "config": { "Labels": { "org.opencontainers.image.base.name": "docker.io/library/{{new string('a', 256)}}:12" } } }
                         """;

        using var handler = new DelegateHttpMessageHandler(request => CreateBaseImageChainResponse(request, blobJson));
        using var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);

        var result = await client.ResolveBaseImagesAsync(new ImageReference
                                                         {
                                                             Registry = "docker.io",
                                                             Repository = "library/app",
                                                             Tag = "latest",
                                                         },
                                                         CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "An invalid label must end the chain, not fail the resolution");
        Assert.IsNotNull(result.Data, "The resolution must return the chain");
        Assert.IsEmpty(result.Data, "A base image with a repository over 255 characters must not be reported");
    }

    /// <summary>
    /// Verify a listed repository whose name is not a valid repository path is reported with an empty repository
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetRepositoriesAsyncWhenRepositoryNameIsInvalidReturnsEmptyRepositoryAsync()
    {
        var json = $$"""
                     {
                       "next": null,
                       "results": [
                         { "namespace": "acme", "name": "my app" },
                         { "namespace": "{{new string('a', 100)}}", "name": "{{new string('b', 155)}}" },
                         { "namespace": "..", "name": "x" },
                         { "namespace": "acme", "name": ".." },
                         { "namespace": "acme", "name": "ok" }
                       ]
                     }
                     """;

        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateJsonResponse(json)));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient);

        var result = await client.GetRepositoriesAsync("acme", CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "An invalid name must not fail the listing");
        Assert.IsNotNull(result.Data, "The listing must return the repositories");
        Assert.AreSequenceEqual(new[] { string.Empty, string.Empty, string.Empty, string.Empty, "acme/ok" },
                                result.Data.Select(repository => repository.Repository).ToArray(),
                                "A space, a total over 255 characters and the dot segments must give an empty repository, a valid name must stay");
    }

    /// <summary>
    /// Verify the Docker Hub repository lookup replaces control characters in the description
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetRepositoryAsyncWhenDescriptionHasControlCharactersReplacesThemAsync()
    {
        var json = """
                   { "description": "De\u001Bsc\u200Bription\u202Ex", "last_updated": "2025-04-01T10:00:00Z" }
                   """;

        Assert.Contains("\\u202E", json, "The test input must carry the escaped format character");
        Assert.Contains("\\u001B", json, "The test input must carry the escaped escape character");

        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateJsonResponse(json)));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient);

        var result = await client.GetRepositoryAsync(new ImageReference
                                                     {
                                                         Registry = "docker.io",
                                                         Repository = "acme/api",
                                                         Tag = "latest",
                                                     },
                                                     CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The lookup must succeed for a well-formed answer");
        Assert.IsNotNull(result.Data, "The lookup must return the repository");
        Assert.AreEqual("De sc ription x", result.Data.Description, "The description must be cleaned");
    }

    /// <summary>
    /// Verify the Docker Hub account lookup replaces control characters in the user name of the result and of the log entry
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetCurrentUserAsyncWhenUserNameHasControlCharactersReplacesThemInResultAndLogAsync()
    {
        var json = """
                   { "username": "ac\u000Am\u202Ee" }
                   """;

        Assert.Contains("\\u202E", json, "The test input must carry the escaped format character");
        Assert.Contains("\\u000A", json, "The test input must carry the escaped line feed");

        var logger = new TestLogger<DockerHubClient>();

        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateJsonResponse(json)));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetCurrentUserAsync(CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The lookup must succeed for a well-formed answer");
        Assert.IsNotNull(result.Data, "The lookup must return the account");
        Assert.AreEqual("ac m e", result.Data.UserName, "The user name of the result must be cleaned");

        var entry = Assert.ContainsSingle(logEntry => logEntry.EventId.Id == 3204, logger.Entries, "The resolved account must be logged once as event 3204");

        Assert.Contains("ac m e", entry.Message, "The log entry must name the cleaned user name");
        Assert.DoesNotContain("\n", entry.Message, "The log entry must carry no line feed from the remote user name");
        Assert.DoesNotContain("\u202E", entry.Message, "The log entry must carry no bidirectional control character from the remote user name");
    }

    /// <summary>
    /// Verify the Docker Hub tag lookup answers not found without a request or a log entry for a repository with a dot segment
    /// </summary>
    /// <param name="repository">Repository with a dot segment</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("../api")]
    [DataRow("./api")]
    [DataRow("acme/..")]
    [DataRow("acme/../../v2/x")]
    [DataRow("..")]
    public async Task DockerHubClientGetTagAsyncDotSegmentRepositoryReturnsNotFoundWithoutRequestAsync(string repository)
    {
        using var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        handler.AddResponse("https://hub.docker.com/v2/users/login", LoginResponseJson);
        handler.Response = new HttpResponseMessage(HttpStatusCode.NotFound);

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetTagAsync(new ImageReference
                                              {
                                                  Registry = "docker.io",
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
    /// Verify the Docker Hub repository lookup answers not found without a request or a log entry for a repository with a dot segment
    /// </summary>
    /// <param name="repository">Repository with a dot segment</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("../api")]
    [DataRow("./api")]
    [DataRow("acme/..")]
    [DataRow("acme/../../v2/x")]
    [DataRow("..")]
    public async Task DockerHubClientGetRepositoryAsyncDotSegmentRepositoryReturnsNotFoundWithoutRequestAsync(string repository)
    {
        using var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        handler.AddResponse("https://hub.docker.com/v2/users/login", LoginResponseJson);
        handler.Response = new HttpResponseMessage(HttpStatusCode.NotFound);

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetRepositoryAsync(new ImageReference
                                                     {
                                                         Registry = "docker.io",
                                                         Repository = repository,
                                                         Tag = "1.0",
                                                     },
                                                     CancellationToken.None)
                                 .ConfigureAwait(false);

        AssertDotSegmentRefusal(result.Status, result.Message, handler, logger);
    }

    /// <summary>
    /// Verify the Docker Hub tags listing answers not found without a request or a log entry for a repository with a dot segment
    /// </summary>
    /// <param name="repository">Repository with a dot segment</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("../api")]
    [DataRow("./api")]
    [DataRow("acme/..")]
    [DataRow("acme/../../v2/x")]
    [DataRow("..")]
    public async Task DockerHubClientGetTagsAsyncDotSegmentRepositoryReturnsNotFoundWithoutRequestAsync(string repository)
    {
        using var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        handler.AddResponse("https://hub.docker.com/v2/users/login", LoginResponseJson);
        handler.Response = new HttpResponseMessage(HttpStatusCode.NotFound);

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetTagsAsync("docker.io",
                                               repository,
                                               cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        AssertDotSegmentRefusal(result.Status, result.Message, handler, logger);
    }

    /// <summary>
    /// Verify the Docker Hub image configuration lookup answers not found without a request or a log entry for a reference with a dot segment
    /// </summary>
    /// <param name="repository">Repository</param>
    /// <param name="tag">Tag</param>
    /// <param name="digest">Digest or null</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("acme/..", "1.0", null)]
    [DataRow("acme/api", "..", null)]
    [DataRow("acme/api", "1.0", "..")]
    public async Task DockerHubClientGetImageConfigurationAsyncDotSegmentReferenceReturnsNotFoundWithoutRequestAsync(string repository, string tag, string? digest)
    {
        using var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetImageConfigurationAsync(new ImageReference
                                                             {
                                                                 Registry = "docker.io",
                                                                 Repository = repository,
                                                                 Tag = tag,
                                                                 Digest = digest,
                                                             },
                                                             CancellationToken.None)
                                 .ConfigureAwait(false);

        AssertDotSegmentRefusal(result.Status, result.Message, handler, logger);
    }

    /// <summary>
    /// Verify the Docker Hub base-image resolution ends the chain without a request for a repository with a dot segment
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientResolveBaseImagesAsyncDotSegmentRepositoryReturnsNoBaseImagesWithoutRequestAsync()
    {
        using var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.ResolveBaseImagesAsync(new ImageReference
                                                         {
                                                             Registry = "docker.io",
                                                             Repository = "acme/../../v2/x",
                                                             Tag = "1.0",
                                                         },
                                                         CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A repository with a dot segment must end the chain like a 404 and not fail the resolution");
        Assert.IsNotNull(result.Data, "The resolution must return a base-image list");
        Assert.IsEmpty(result.Data, "A repository with a dot segment must yield no base images");
        Assert.IsEmpty(handler.Requests, "No request, not even a token request, may be sent for a repository with a dot segment");
        Assert.IsEmpty(logger.Entries, "Nothing may be logged for a repository with a dot segment");
    }

    /// <summary>
    /// Verify the Docker Hub image configuration lookup sends no blob request for a configuration digest that is a dot segment
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetImageConfigurationAsyncDotSegmentConfigDigestSendsNoBlobRequestAsync()
    {
        var requests = new List<string>();

        using var handler = new DelegateHttpMessageHandler(request =>
                                                           {
                                                               var uri = request.RequestUri!;

                                                               requests.Add(uri.AbsoluteUri);

                                                               return string.Equals(uri.Host, "auth.docker.io", StringComparison.Ordinal)
                                                                          ? CreateJsonResponse("""{ "token": "registry-token" }""")
                                                                          : CreateJsonResponse("""{ "schemaVersion": 2, "config": { "digest": ".." } }""");
                                                           });
        using var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);

        var result = await client.GetImageConfigurationAsync(new ImageReference
                                                             {
                                                                 Registry = "docker.io",
                                                                 Repository = "acme/api",
                                                                 Tag = "1.0",
                                                             },
                                                             CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A configuration digest with a dot segment must fail like an unreadable blob");
        Assert.AreEqual("Image configuration blob could not be read for 'docker.io/acme/api:1.0'",
                        result.Message,
                        "The message must be the one of an unreadable configuration blob");
        Assert.HasCount(2, requests, "Only the token and the manifest may be requested, never the blob");
        Assert.DoesNotContain("/blobs/", requests[1], "The second request must be the manifest, not a blob");
    }

    /// <summary>
    /// Verify the Docker Hub image configuration lookup sends no platform manifest request for a manifest-list digest that is a dot segment
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetImageConfigurationAsyncDotSegmentManifestListDigestReturnsNotFoundAsync()
    {
        var requests = new List<string>();

        using var handler = new DelegateHttpMessageHandler(request =>
                                                           {
                                                               var uri = request.RequestUri!;

                                                               requests.Add(uri.AbsoluteUri);

                                                               if (string.Equals(uri.Host, "auth.docker.io", StringComparison.Ordinal))
                                                               {
                                                                   return CreateJsonResponse("""{ "token": "registry-token" }""");
                                                               }

                                                               return new HttpResponseMessage(HttpStatusCode.OK)
                                                                      {
                                                                          Content = new StringContent("""{ "schemaVersion": 2, "manifests": [ { "digest": "..", "platform": { "os": "linux", "architecture": "amd64" } } ] }""",
                                                                                                      Encoding.UTF8,
                                                                                                      "application/vnd.oci.image.index.v1+json"),
                                                                      };
                                                           });
        using var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);

        var result = await client.GetImageConfigurationAsync(new ImageReference
                                                             {
                                                                 Registry = "docker.io",
                                                                 Repository = "acme/api",
                                                                 Tag = "1.0",
                                                             },
                                                             CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.NotFound, result.Status, "A manifest-list digest with a dot segment must be answered like an unresolvable configuration digest");
        Assert.AreEqual("Config blob digest could not be resolved for 'docker.io/acme/api:1.0'",
                        result.Message,
                        "The message must be the one of an unresolvable configuration digest");
        Assert.HasCount(2, requests, "Only the token and the manifest list may be requested, never the platform manifest");
    }

    /// <summary>
    /// Verify the Docker Hub tag lookup keeps a repository segment that only contains dots
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetTagAsyncDotsInsideSegmentKeepsSegmentAsync()
    {
        const string tagUri = "https://hub.docker.com/v2/namespaces/acme/repositories/my.app/tags/1.0";

        using var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        handler.AddResponse("https://hub.docker.com/v2/users/login", LoginResponseJson);
        handler.AddResponse(tagUri,
                            """
                            {
                              "name": "1.0",
                              "digest": "sha256:index",
                              "last_pushed": "2025-04-02T10:00:00Z",
                              "images": [ { "architecture": "amd64", "os": "linux", "digest": "sha256:amd64" } ]
                            }
                            """);

        var client = CreateClient(httpClient);

        var result = await client.GetTagAsync(new ImageReference
                                              {
                                                  Registry = "docker.io",
                                                  Repository = "acme/my.app",
                                                  Tag = "1.0",
                                              },
                                              "linux",
                                              "amd64",
                                              CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A segment that is not exactly a dot segment must not be refused");
        Assert.Contains(request => string.Equals(request.RequestUri, tagUri, StringComparison.Ordinal),
                        handler.Requests,
                        "The tag must be requested with every segment kept");
    }

    /// <summary>
    /// Verify the Docker Hub base-image resolution sends no blob request for a configuration digest that is a dot segment
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientResolveBaseImagesAsyncDotSegmentConfigDigestEndsChainWithoutBlobRequestAsync()
    {
        var requests = new List<string>();

        using var handler = new DelegateHttpMessageHandler(request =>
                                                           {
                                                               var uri = request.RequestUri!;

                                                               requests.Add(uri.AbsoluteUri);

                                                               return string.Equals(uri.Host, "auth.docker.io", StringComparison.Ordinal)
                                                                          ? CreateJsonResponse("""{ "token": "registry-token" }""")
                                                                          : CreateJsonResponse("""{ "schemaVersion": 2, "config": { "digest": ".." } }""");
                                                           });
        using var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);

        var result = await client.ResolveBaseImagesAsync(new ImageReference
                                                         {
                                                             Registry = "docker.io",
                                                             Repository = "acme/api",
                                                             Tag = "1.0",
                                                         },
                                                         CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A configuration digest with a dot segment must end the chain like a 404");
        Assert.IsNotNull(result.Data, "The resolution must return a base-image list");
        Assert.IsEmpty(result.Data, "A configuration digest with a dot segment must yield no base images");
        Assert.HasCount(2, requests, "Only the token and the manifest may be requested, never the blob");
        Assert.DoesNotContain("/blobs/", requests[1], "The second request must be the manifest, not a blob");
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
                                                StubHttpMessageHandler handler,
                                                TestLogger<DockerHubClient> logger)
    {
        Assert.AreEqual(ExternalOperationStatus.NotFound, status, "A path with a dot segment must be answered as not found");
        Assert.AreEqual(RegistryReferenceValidator.DotSegmentMessage, message, "The message must be the fixed dot-segment text");
        Assert.IsEmpty(handler.Requests, "No request, not even a login or token request, may be sent for a path with a dot segment");
        Assert.IsEmpty(logger.Entries, "Nothing may be logged for a path with a dot segment");
    }

    /// <summary>
    /// Assert no request went to the rejected origin and no credential left Docker Hub
    /// </summary>
    /// <param name="handler">Observing handler</param>
    /// <param name="rejectedOrigin">Origin of the rejected link</param>
    private static void AssertNothingSentToForeignOrigin(StubHttpMessageHandler handler, string rejectedOrigin)
    {
        Assert.DoesNotContain(request => request.RequestUri.StartsWith($"{rejectedOrigin}/", StringComparison.OrdinalIgnoreCase),
                              handler.Requests,
                              "No request may be sent to the origin of a rejected next-page link");
        Assert.DoesNotContain(request => request.AuthorizationScheme is not null
                                         && request.RequestUri.StartsWith("https://hub.docker.com/", StringComparison.OrdinalIgnoreCase) == false,
                              handler.Requests,
                              "A request carrying an Authorization header must never go anywhere but Docker Hub");
    }

    /// <summary>
    /// Create a JSON response
    /// </summary>
    /// <param name="json">JSON content</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
               {
                   Content = new StringContent(json, Encoding.UTF8, "application/json"),
               };
    }

    /// <summary>
    /// Assert exactly one pagination warning naming the rejected origin was logged
    /// </summary>
    /// <param name="logger">Captured logger</param>
    /// <param name="rejectedOrigin">Origin of the rejected link</param>
    private static void AssertSingleRejectionWarning(TestLogger<DockerHubClient> logger, string rejectedOrigin)
    {
        var warning = Assert.ContainsSingle(entry => entry.EventId.Id == 3211 && entry.LogLevel == LogLevel.Warning,
                                            logger.Entries,
                                            "A rejected next-page link must be logged once as warning 3211");

        Assert.Contains(rejectedOrigin,
                        warning.Message,
                        "The warning must name the rejected origin");
    }

    /// <summary>
    /// Create the Docker Hub image reference used by the transport-failure tests
    /// </summary>
    /// <returns>Image reference</returns>
    private static ImageReference CreateTransportFailureReference()
    {
        return new ImageReference
               {
                   Registry = "docker.io",
                   Repository = "acme/app",
                   Tag = "1.0",
               };
    }

    /// <summary>
    /// Create a Docker Hub client for tests
    /// </summary>
    /// <param name="httpClient">Configured HTTP client</param>
    /// <param name="registry">Configured Docker Hub registry value</param>
    /// <param name="userName">Configured Docker Hub user name</param>
    /// <param name="logger">Logger that captures the client's entries; null creates a new one</param>
    /// <returns>Docker Hub client</returns>
    private static DockerHubClient CreateClient(HttpClient httpClient,
                                                string registry = "docker.io",
                                                string? userName = "acme",
                                                TestLogger<DockerHubClient>? logger = null)
    {
        var options = new DockerUpdateGuardOptions
                      {
                          DockerHub = new DockerHubOptions
                                      {
                                          Registry = registry,
                                          UserName = userName,
                                          Pat = "test-pat",
                                          RequestTimeoutSeconds = 30,
                                      },
                      };

        return new DockerHubClient(httpClient,
                                   logger ?? new TestLogger<DockerHubClient>(),
                                   new TestOptionsMonitor<DockerUpdateGuardOptions>(options));
    }

    /// <summary>
    /// Answer the Docker Hub login with a token and every other request with the given response
    /// </summary>
    /// <param name="request">Request</param>
    /// <param name="otherResponse">Factory of the response to every other request</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateHubResponse(HttpRequestMessage request, Func<HttpResponseMessage> otherResponse)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/users/login", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
                   {
                       Content = new StringContent(LoginResponseJson, Encoding.UTF8, "application/json"),
                   };
        }

        return otherResponse();
    }

    /// <summary>
    /// Create a Bad Request response with an oversized body
    /// </summary>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateLongErrorResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
               {
                   Content = new StringContent(new string('x', OversizedRemoteText.BodyLength)),
               };
    }

    /// <summary>
    /// Create a tag page response with a next-page link
    /// </summary>
    /// <param name="nextLink">Next-page link</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateTagPageResponse(string nextLink)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
               {
                   Content = new StringContent($$"""{ "next": "{{nextLink}}", "results": [ { "name": "latest", "digest": "sha256:latest", "last_pushed": "2025-04-02T10:00:00Z" } ] }""",
                                               Encoding.UTF8,
                                               "application/json"),
               };
    }

    /// <summary>
    /// Answer the requests of a registry lookup: the first image carries the given config blob, every further image has no labels
    /// </summary>
    /// <param name="request">Request</param>
    /// <param name="firstBlobJson">Config blob of library/app</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateBaseImageChainResponse(HttpRequestMessage request, string firstBlobJson)
    {
        var uri = request.RequestUri!;

        if (string.Equals(uri.Host, "auth.docker.io", StringComparison.Ordinal))
        {
            return CreateJsonResponse("""{ "token": "registry-token" }""");
        }

        if (uri.AbsolutePath.Contains("/manifests/", StringComparison.Ordinal))
        {
            return CreateJsonResponse("""{ "schemaVersion": 2, "config": { "digest": "sha256:config" } }""");
        }

        if (uri.AbsolutePath.Contains("/blobs/", StringComparison.Ordinal))
        {
            return CreateJsonResponse(uri.AbsolutePath.Contains("/library/app/", StringComparison.Ordinal)
                                          ? firstBlobJson
                                          : """{ "config": { "Labels": {} } }""");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    #endregion // Methods
}