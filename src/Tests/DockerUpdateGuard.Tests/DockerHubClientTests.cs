using System.Net;
using System.Net.Http;
using System.Text;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DockerHubClient"/>
/// </summary>
[TestClass]
public class DockerHubClientTests
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
    /// Verify Docker Hub tag lookup resolves the digest for the requested runtime architecture
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetTagAsyncUsesRequestedArchitectureDigestAsync()
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
            Assert.AreEqual("sha256:arm64",
                            result.Data.Digest,
                            "Tag lookup must select the digest that matches the requested runtime architecture");
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

    #region Helper types

    /// <summary>
    /// Stub HTTP message handler for deterministic Docker Hub client tests
    /// </summary>
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        #region Fields

        private readonly List<ObservedRequest> _requests = [];
        private readonly Dictionary<string, HttpResponseMessage> _responses = new(StringComparer.Ordinal);

        #endregion // Fields

        #region Properties

        /// <summary>
        /// Observed outbound requests
        /// </summary>
        public IReadOnlyList<ObservedRequest> Requests => _requests;

        #endregion // Properties

        #region Methods

        /// <summary>
        /// Add a JSON response for a request URI
        /// </summary>
        /// <param name="requestUri">Absolute request URI</param>
        /// <param name="jsonContent">JSON content</param>
        public void AddResponse(string requestUri, string jsonContent)
        {
            _responses[requestUri] = new HttpResponseMessage(HttpStatusCode.OK)
                                     {
                                         Content = new StringContent(jsonContent,
                                                                     Encoding.UTF8,
                                                                     "application/json"),
                                     };
        }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request.RequestUri);

            _requests.Add(new ObservedRequest
                          {
                              Method = request.Method.Method,
                              RequestUri = request.RequestUri.ToString(),
                              AuthorizationScheme = request.Headers.Authorization?.Scheme,
                              AuthorizationParameter = request.Headers.Authorization?.Parameter,
                          });

            if (_responses.TryGetValue(request.RequestUri.ToString(), out var response))
            {
                return Task.FromResult(CloneResponse(response));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                                   {
                                       RequestMessage = request,
                                   });
        }

        /// <summary>
        /// Clone a configured response for repeatable handler usage
        /// </summary>
        /// <param name="response">Template response</param>
        /// <returns>Cloned response</returns>
        private static HttpResponseMessage CloneResponse(HttpResponseMessage response)
        {
            var content = response.Content is null
                              ? null
                              : new StringContent(response.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
                                                  Encoding.UTF8,
                                                  response.Content.Headers.ContentType?.MediaType);

            return new HttpResponseMessage(response.StatusCode)
                   {
                       Content = content,
                   };
        }

        #endregion // Methods
    }

    /// <summary>
    /// Observed outbound request data
    /// </summary>
    private sealed class ObservedRequest
    {
        #region Properties

        /// <summary>
        /// Authorization parameter
        /// </summary>
        public string? AuthorizationParameter { get; init; }

        /// <summary>
        /// Authorization scheme
        /// </summary>
        public string? AuthorizationScheme { get; init; }

        /// <summary>
        /// HTTP method
        /// </summary>
        public string Method { get; init; } = string.Empty;

        /// <summary>
        /// Request URI
        /// </summary>
        public string RequestUri { get; init; } = string.Empty;

        #endregion // Properties
    }

    #endregion // Helper types
}