using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DockerHubClient"/>
/// </summary>
[TestClass]
public partial class DockerHubClientTests
{
    #region Methods

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

            var result = await client.GetTagsAsync("docker.io", "acme/api", CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Tag listing must succeed when Docker Hub returns paged tag payloads");
            Assert.IsNotNull(result.Data, "Tag listing must return tag metadata");
            CollectionAssert.AreEqual(new[] { "latest", "2.4.1" },
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
                                                   CancellationToken.None,
                                                   queryOptions: new RegistryTagQueryOptions
                                                                 {
                                                                     CurrentTag = "2.4.1",
                                                                     MaximumTags = 50,
                                                                     MinimumVersionTag = "2.4.1",
                                                                     PublishedSinceUtc = new DateTimeOffset(2025, 04, 02, 10, 00, 00, TimeSpan.Zero),
                                                                 })
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "Bounded Docker Hub tag listing must still succeed");
            Assert.IsNotNull(result.Data, "Bounded Docker Hub tag listing must return tag data");
            CollectionAssert.AreEqual(new[] { "3.0.0", "2.4.1" },
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
                                                  CancellationToken.None,
                                                  "linux",
                                                  "arm64")
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
    /// Create a Docker Hub client for tests
    /// </summary>
    /// <param name="httpClient">Configured HTTP client</param>
    /// <param name="registry">Configured Docker Hub registry value</param>
    /// <param name="userName">Configured Docker Hub user name</param>
    /// <returns>Docker Hub client</returns>
    private static DockerHubClient CreateClient(HttpClient httpClient,
                                                string registry = "docker.io",
                                                string? userName = "acme")
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
                                   new TestLogger<DockerHubClient>(),
                                   new TestOptionsMonitor<DockerUpdateGuardOptions>(options));
    }

    #endregion // Methods
}