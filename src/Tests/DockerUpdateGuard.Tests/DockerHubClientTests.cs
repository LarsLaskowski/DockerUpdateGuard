using System.Net;
using System.Net.Http.Headers;
using System.Text;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Helper;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Extensions.Logging;

using NSubstitute;

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

    /// <summary>
    /// Docker Hub login URL
    /// </summary>
    private const string LoginUri = "https://hub.docker.com/v2/users/login";

    /// <summary>
    /// Escape of a lone high surrogate
    /// </summary>
    private const string LoneSurrogateEscape = """\ud800""";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify every Docker Hub call refuses a registry that is not Docker Hub as unsupported without a request
    /// </summary>
    /// <param name="operation">Name of the call</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("CurrentUser")]
    [DataRow("Repositories")]
    [DataRow("Repository")]
    [DataRow("Tag")]
    [DataRow("Tags")]
    public async Task DockerHubClientCallsWhenRegistryIsNotDockerHubReturnUnsupportedAsync(string operation)
    {
        using var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient, registry: "ghcr.io");

        var (status, message) = await RunCallAsync(client, operation, "ghcr.io").ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported, status, $"The {operation} call must refuse a foreign registry");
        Assert.AreEqual("Registry 'ghcr.io' is not supported by the Docker Hub adapter", message, "The refusal must name the registry");
        Assert.IsEmpty(handler.Requests, "No request may be sent for a foreign registry");
    }

    /// <summary>
    /// Verify the Docker Hub calls answer a 404 as not found with the fixed text
    /// </summary>
    /// <param name="operation">Name of the call</param>
    /// <param name="expectedMessage">Expected message</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("Repositories", "Docker Hub account 'acme' was not found")]
    [DataRow("Repository", "Repository 'acme/api' was not found in Docker Hub")]
    [DataRow("Tag", "Tag 'latest' was not found for 'acme/api'")]
    [DataRow("Tags", "Repository 'acme/api' was not found in Docker Hub")]
    public async Task DockerHubClientCallsWhenHubAnswersNotFoundReturnNotFoundAsync(string operation, string expectedMessage)
    {
        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient);

        var (status, message) = await RunCallAsync(client, operation, "docker.io").ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.NotFound, status, $"The {operation} call must answer a 404 as not found");
        Assert.AreEqual(expectedMessage, message, "The not-found message must be the fixed text");
    }

    /// <summary>
    /// Verify the Docker Hub calls answer a non-success status as failed with the call's message prefix
    /// </summary>
    /// <param name="operation">Name of the call</param>
    /// <param name="expectedPrefix">Expected message prefix</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("CurrentUser", "Docker Hub current user lookup failed")]
    [DataRow("Repositories", "Docker Hub repository listing failed for 'acme'")]
    [DataRow("Repository", "Repository metadata lookup failed for 'acme/api'")]
    [DataRow("Tag", "Tag metadata lookup failed for 'docker.io/acme/api:latest'")]
    [DataRow("Tags", "Tag lookup failed for 'acme/api'")]
    public async Task DockerHubClientCallsWhenHubAnswersServerErrorReturnFailedAsync(string operation, string expectedPrefix)
    {
        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var (status, message) = await RunCallAsync(client, operation, "docker.io").ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, status, $"The {operation} call must answer a server error as failed");
        Assert.StartsWith(expectedPrefix, message, "The failure message must start with the call's prefix");
        Assert.Contains(entry => entry.LogLevel == LogLevel.Warning,
                        logger.Entries,
                        "The failed request must be logged as a warning");
    }

    /// <summary>
    /// Verify a current-user answer without a username is answered as failed
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetCurrentUserAsyncWhenBodyHasNoUsernameReturnsFailedAsync()
    {
        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateJsonResponse("{}")));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient);

        var result = await client.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "An answer without a username must fail");
        Assert.AreEqual("Docker Hub current user response did not contain a username", result.Message, "The failure must name the missing username");
    }

    /// <summary>
    /// Verify a current-user answer with a string that does not decode is answered as an invalid response
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetCurrentUserAsyncWhenBodyHasLoneSurrogateReturnsInvalidResponseAsync()
    {
        var body = $$"""{ "username": "ac{{LoneSurrogateEscape}}me" }""";

        Assert.Contains("\\ud800", body, "The test input must carry the escaped lone surrogate");

        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateJsonResponse(body)));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);

        AssertInvalidResponse(result, "Docker Hub current user lookup failed: invalid response", "current-user", logger);
    }

    /// <summary>
    /// Verify a repository listing whose second page has a string that does not decode is answered as an invalid response
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetRepositoriesAsyncWhenSecondPageHasLoneSurrogateReturnsInvalidResponseAsync()
    {
        var secondPage = $$"""{ "next": null, "results": [ { "namespace": "acme", "name": "w{{LoneSurrogateEscape}}eb" } ] }""";

        Assert.Contains("\\ud800", secondPage, "The test input must carry the escaped lone surrogate");

        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request,
                                                                                        () => request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal)
                                                                                                  ? CreateJsonResponse(secondPage)
                                                                                                  : CreateJsonResponse("""{ "next": "https://hub.docker.com/v2/repositories/acme/?page=2&page_size=100", "results": [ { "namespace": "acme", "name": "api" } ] }""")));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetRepositoriesAsync("acme", CancellationToken.None).ConfigureAwait(false);

        AssertInvalidResponse(result, "Docker Hub repository listing failed for 'acme': invalid response", "acme", logger);
    }

    /// <summary>
    /// Verify a repository listing with a body that is not JSON is answered as an invalid response
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetRepositoriesAsyncWhenBodyIsMalformedReturnsInvalidResponseAsync()
    {
        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateJsonResponse("""{ "results": [""")));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetRepositoriesAsync("acme", CancellationToken.None).ConfigureAwait(false);

        AssertInvalidResponse(result, "Docker Hub repository listing failed for 'acme': invalid response", "acme", logger);
    }

    /// <summary>
    /// Verify a repository answer with a description that does not decode is answered as an invalid response
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetRepositoryAsyncWhenDescriptionHasLoneSurrogateReturnsInvalidResponseAsync()
    {
        var body = $$"""{ "description": "API {{LoneSurrogateEscape}}", "last_updated": "2025-04-01T10:00:00Z" }""";

        Assert.Contains("\\ud800", body, "The test input must carry the escaped lone surrogate");

        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateJsonResponse(body)));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetRepositoryAsync(new ImageReference
                                                     {
                                                         Registry = "docker.io",
                                                         Repository = "acme/api",
                                                         Tag = "latest",
                                                     },
                                                     CancellationToken.None)
                                 .ConfigureAwait(false);

        AssertInvalidResponse(result, "Repository metadata lookup failed for 'acme/api': invalid response", "acme/api", logger);
    }

    /// <summary>
    /// Verify a tag answer with a string that does not decode is answered as an invalid response
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetTagAsyncWhenBodyHasLoneSurrogateReturnsInvalidResponseAsync()
    {
        var body = $$"""{ "name": "latest", "digest": "sha256:ab{{LoneSurrogateEscape}}", "last_updated": "2025-04-01T10:00:00Z" }""";

        Assert.Contains("\\ud800", body, "The test input must carry the escaped lone surrogate");

        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateJsonResponse(body)));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);
        var imageReference = new ImageReference
                             {
                                 Registry = "docker.io",
                                 Repository = "acme/api",
                                 Tag = "latest",
                             };

        var result = await client.GetTagAsync(imageReference, "linux", "amd64", CancellationToken.None).ConfigureAwait(false);

        AssertInvalidResponse(result, $"Tag metadata lookup failed for '{imageReference.FullReference}': invalid response", imageReference.FullReference, logger);
    }

    /// <summary>
    /// Verify a tag lookup whose login answer has a string that does not decode is answered as an invalid response
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetTagAsyncWhenLoginBodyHasLoneSurrogateReturnsInvalidResponseAsync()
    {
        var loginBody = $$"""{ "token": "{{LoginToken}}", "detail": "{{LoneSurrogateEscape}}" }""";

        Assert.Contains("\\ud800", loginBody, "The test input must carry the escaped lone surrogate");

        using var handler = new DelegateHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/users/login", StringComparison.Ordinal)
                                                                          ? CreateJsonResponse(loginBody)
                                                                          : CreateJsonResponse("""{ "name": "latest", "digest": "sha256:ab" }"""));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);
        var imageReference = new ImageReference
                             {
                                 Registry = "docker.io",
                                 Repository = "acme/api",
                                 Tag = "latest",
                             };

        var result = await client.GetTagAsync(imageReference, "linux", "amd64", CancellationToken.None).ConfigureAwait(false);

        AssertInvalidResponse(result, $"Tag metadata lookup failed for '{imageReference.FullReference}': invalid response", imageReference.FullReference, logger);
    }

    /// <summary>
    /// Verify a tag listing with a string that does not decode is answered as an invalid response
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetTagsAsyncWhenBodyHasLoneSurrogateReturnsInvalidResponseAsync()
    {
        var body = $$"""{ "next": null, "results": [ { "name": "latest", "digest": "sha256:ab{{LoneSurrogateEscape}}", "last_pushed": "2025-04-02T10:00:00Z" } ] }""";

        Assert.Contains("\\ud800", body, "The test input must carry the escaped lone surrogate");

        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateJsonResponse(body)));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetTagsAsync("docker.io", "acme/api", cancellationToken: CancellationToken.None).ConfigureAwait(false);

        AssertInvalidResponse(result, "Tag lookup failed for 'acme/api': invalid response", "acme/api", logger);
    }

    /// <summary>
    /// Verify an image configuration lookup whose blob has a label value that does not decode is answered as an invalid response
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetImageConfigurationAsyncWhenConfigHasLoneSurrogateReturnsInvalidResponseAsync()
    {
        var blobJson = $$"""{ "config": { "Labels": { "plain": "v{{LoneSurrogateEscape}}" } } }""";

        Assert.Contains("\\ud800", blobJson, "The test input must carry the escaped lone surrogate");

        using var handler = new DelegateHttpMessageHandler(request => CreateBaseImageChainResponse(request, blobJson));
        using var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        var imageReference = new ImageReference
                             {
                                 Registry = "docker.io",
                                 Repository = "library/app",
                                 Tag = "latest",
                             };

        var result = await client.GetImageConfigurationAsync(imageReference, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "An unreadable configuration blob must fail the configuration lookup");
        Assert.AreEqual($"Image configuration lookup failed for '{imageReference.FullReference}': invalid response",
                        result.Message,
                        "An unreadable configuration blob must be reported as an invalid response");
    }

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
    /// Verify the Docker Hub image configuration lookup selects a real platform manifest and skips attestation manifests
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task DockerHubClientGetImageConfigurationAsyncSkipsAttestationManifestAsync()
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
                                      "digest": "sha256:windows",
                                      "platform": {
                                        "os": "windows",
                                        "architecture": "amd64"
                                      }
                                    }
                                  ]
                                }
                                """,
                                "application/vnd.oci.image.index.v1+json");
            handler.AddResponse("https://registry-1.docker.io/v2/library/app/manifests/sha256%3Awindows",
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
                                      "org.example.marker": "platform-config"
                                    }
                                  }
                                }
                                """);

            var client = CreateClient(httpClient);
            var result = await client.GetImageConfigurationAsync(new ImageReference
                                                                 {
                                                                     Registry = "docker.io",
                                                                     Repository = "library/app",
                                                                     Tag = "latest",
                                                                 },
                                                                 CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded,
                            result.Status,
                            "The configuration lookup must succeed when a platform manifest can be selected from the manifest list");
            Assert.IsNotNull(result.Data, "The configuration lookup must return the configuration");
            Assert.AreEqual("platform-config",
                            result.Data.Labels["org.example.marker"],
                            "The configuration must come from the selected platform manifest");
            Assert.Contains(entry => entry.RequestUri == "https://registry-1.docker.io/v2/library/app/manifests/sha256%3Awindows",
                            handler.Requests,
                            "The manifest-list selection must fetch the real platform manifest when no linux entry exists");
            Assert.DoesNotContain(entry => entry.RequestUri == "https://registry-1.docker.io/v2/library/app/manifests/sha256%3Aattestation",
                                  handler.Requests,
                                  "The manifest-list selection must skip attestation manifests with an unknown platform");
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

        var tokenCache = new DockerHubAccessTokenCache(TimeProvider.System);

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
                                             new TestOptionsMonitor<DockerUpdateGuardOptions>(options),
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             tokenCache);
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
            tokenCache.Dispose();
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
    /// Verify the Hub image configuration keeps the last of label names that differ only in case
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task DockerHubClientGetImageConfigurationAsyncWhenLabelNamesDifferOnlyInCaseKeepsTheLastAsync()
    {
        var blobJson = """
                       {
                         "config": {
                           "Env": ["A=1"],
                           "Labels": {
                             "Org.Opencontainers.Image.Base.Name": "first",
                             "org.opencontainers.image.base.name": "second"
                           }
                         }
                       }
                       """;

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

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "Label names that differ only in case must not fail the configuration lookup");
        Assert.IsNotNull(result.Data, "The configuration lookup must return the configuration");
        Assert.AreEqual("second", result.Data.Labels["org.opencontainers.image.base.name"], "The label that comes last must win");
        Assert.AreSequenceEqual(["A=1"], result.Data.EnvironmentVariables.ToArray(), "The environment variable must be kept");
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
    /// Verify a login answered 429 with a Retry-After delta defers the next request of the same instance without any request
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task DockerHubClientLoginRetryAfterDeltaDefersNextRequestOfSameInstanceAsync()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var requests = new List<string>();

        using var handler = CreateLoginSequenceHandler(requests, () => CreateTooManyRequests(new RetryConditionHeaderValue(TimeSpan.FromSeconds(120))));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient, loginRetryAfterRegistry: new LoginRetryAfterRegistry(CreateClock(() => now)));

        var first = await client.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, first.Status, "The request whose login is answered 429 must fail");
        Assert.HasCount(1, requests, "The first request must send exactly the login");

        var second = await client.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, second.Status, "A request inside the window must fail");
        Assert.IsNotNull(second.Message, "The failed result must carry a message");
        Assert.Contains("Docker Hub authentication token request for 'acme' not sent: Docker Hub answered HTTP 429 and asked to wait until 2026-03-10T12:02:00Z",
                        second.Message,
                        "The failure must name the user and the end of the window");
        Assert.HasCount(1, requests, "A request inside the window must send no login and no web API request");
    }

    /// <summary>
    /// Verify a login answered 429 with a Retry-After date defers the request of another instance sharing the registry
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task DockerHubClientLoginRetryAfterDateDefersRequestOfOtherInstanceAsync()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var requests = new List<string>();
        var registry = new LoginRetryAfterRegistry(CreateClock(() => now));

        using var handler = CreateLoginSequenceHandler(requests, () => CreateTooManyRequests(new RetryConditionHeaderValue(now.AddSeconds(90))));
        using var firstHttpClient = new HttpClient(handler, false)
                                    {
                                        BaseAddress = new Uri("https://hub.docker.com/"),
                                    };
        using var secondHttpClient = new HttpClient(handler, false)
                                     {
                                         BaseAddress = new Uri("https://hub.docker.com/"),
                                     };

        var firstClient = CreateClient(firstHttpClient, loginRetryAfterRegistry: registry);
        var secondClient = CreateClient(secondHttpClient, loginRetryAfterRegistry: registry);

        var first = await firstClient.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, first.Status, "The request whose login is answered 429 must fail");

        var second = await secondClient.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, second.Status, "A request of another instance inside the window must fail");
        Assert.IsNotNull(second.Message, "The failed result must carry a message");
        Assert.Contains("Docker Hub authentication token request for 'acme' not sent: Docker Hub answered HTTP 429 and asked to wait until 2026-03-10T12:01:30Z",
                        second.Message,
                        "The failure must name the user and the end of the window given as a date");
        Assert.HasCount(1, requests, "The other instance must send no request inside the window");
    }

    /// <summary>
    /// Verify the next request logs in again once the clock reaches the end of the window
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task DockerHubClientLoginRetryAfterPassedLogsInAgainAsync()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var requests = new List<string>();

        using var handler = CreateLoginSequenceHandler(requests,
                                                       () => CreateTooManyRequests(new RetryConditionHeaderValue(TimeSpan.FromSeconds(120))),
                                                       () => CreateJsonResponse(LoginResponseJson));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient, loginRetryAfterRegistry: new LoginRetryAfterRegistry(CreateClock(() => now)));

        await client.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);

        now = now.AddSeconds(119);

        var beforeEnd = await client.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, beforeEnd.Status, "A request one second before the end of the window must still fail");
        Assert.HasCount(1, requests, "A request before the end of the window must send nothing");

        now = now.AddSeconds(1);

        var atEnd = await client.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, atEnd.Status, "A request at the end of the window must log in and succeed");
        Assert.AreEqual(2,
                        requests.Count(request => string.Equals(request, LoginUri, StringComparison.Ordinal)),
                        "The login must be sent again once the window has passed");
    }

    /// <summary>
    /// Verify a login answered 429 without Retry-After opens no window
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task DockerHubClientLoginTooManyRequestsWithoutRetryAfterLogsInAgainAsync()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var requests = new List<string>();

        using var handler = CreateLoginSequenceHandler(requests, () => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient, loginRetryAfterRegistry: new LoginRetryAfterRegistry(CreateClock(() => now)));

        await client.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);
        await client.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(2,
                        requests.Count(request => string.Equals(request, LoginUri, StringComparison.Ordinal)),
                        "A 429 without Retry-After must not defer the next login");
    }

    /// <summary>
    /// Verify a login failure other than 429 that carries Retry-After opens no window
    /// </summary>
    /// <param name="statusCode">Status code of the login response</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow(HttpStatusCode.Unauthorized)]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    public async Task DockerHubClientLoginNonTooManyRequestsWithRetryAfterLogsInAgainAsync(HttpStatusCode statusCode)
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var requests = new List<string>();

        using var handler = CreateLoginSequenceHandler(requests,
                                                       () =>
                                                       {
                                                           var response = new HttpResponseMessage(statusCode);

                                                           response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));

                                                           return response;
                                                       });
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient, loginRetryAfterRegistry: new LoginRetryAfterRegistry(CreateClock(() => now)));

        await client.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);
        await client.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(2,
                        requests.Count(request => string.Equals(request, LoginUri, StringComparison.Ordinal)),
                        "A failure other than 429 must not defer the next login even with Retry-After");
    }

    /// <summary>
    /// Verify an image configuration lookup refuses a registry that is not Docker Hub as unsupported without a request
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetImageConfigurationAsyncWhenRegistryIsNotDockerHubReturnsUnsupportedAsync()
    {
        using var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient, registry: "ghcr.io");

        var result = await client.GetImageConfigurationAsync(new ImageReference
                                                             {
                                                                 Registry = "ghcr.io",
                                                                 Repository = "acme/api",
                                                                 Tag = "latest",
                                                             },
                                                             CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, "A foreign registry must be refused");
        Assert.AreEqual("Registry 'ghcr.io' is not supported by the Docker Hub adapter", result.Message, "The refusal must name the registry");
        Assert.IsEmpty(handler.Requests, "No request may be sent for a foreign registry");
    }

    /// <summary>
    /// Verify an image configuration lookup fails when the registry token request is refused
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetImageConfigurationAsyncWhenRegistryTokenIsRefusedReturnsFailedAsync()
    {
        using var handler = new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        var imageReference = new ImageReference
                             {
                                 Registry = "docker.io",
                                 Repository = "library/app",
                                 Tag = "latest",
                             };

        var result = await client.GetImageConfigurationAsync(imageReference, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A refused registry token must fail the lookup");
        Assert.AreEqual($"Registry token acquisition failed for '{imageReference.FullReference}'", result.Message, "The failure must name the image");
    }

    /// <summary>
    /// Verify a call fails without a request when a PAT is configured but the user name is not
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetCurrentUserAsyncWhenPatHasNoUserNameReturnsFailedAsync()
    {
        using var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        var client = CreateClient(httpClient, userName: null);

        var result = await client.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A PAT without a user name must fail the call");
        Assert.Contains("Docker Hub authentication requires 'DockerUpdateGuard:DockerHub:UserName' when a PAT is configured",
                        result.Message ?? string.Empty,
                        "The failure must name the missing option");
        Assert.IsEmpty(handler.Requests, "No request may be sent without a user name");
    }

    /// <summary>
    /// Verify a call that waited for the token refresh of another call uses the token that call obtained instead of logging in again
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubClientGetCurrentUserAsyncWhenAnotherCallRefreshesTokenLogsInOnlyOnceAsync()
    {
        var loginCount = 0;
        DockerHubClient? client = null;
        Task<ExternalOperationResult<DockerHubAuthenticatedUserData>>? waitingCall = null;

        using var handler = new DelegateHttpMessageHandler(request =>
                                                           {
                                                               if (request.RequestUri!.AbsolutePath.EndsWith("/users/login", StringComparison.Ordinal))
                                                               {
                                                                   loginCount++;

                                                                   // The second call starts here, inside the first call's login, and stops at the refresh lock
                                                                   waitingCall ??= client!.GetCurrentUserAsync(CancellationToken.None);
                                                               }

                                                               return CreateHubResponse(request, () => CreateJsonResponse("""{ "username": "acme" }"""));
                                                           });
        using var httpClient = new HttpClient(handler)
                               {
                                   BaseAddress = new Uri("https://hub.docker.com/"),
                               };

        client = CreateClient(httpClient);

        var first = await client.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.IsNotNull(waitingCall, "The second call must have started during the login of the first");

        var second = await waitingCall.ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, first.Status, "The first call must succeed");
        Assert.AreEqual(ExternalOperationStatus.Succeeded, second.Status, "The waiting call must succeed");
        Assert.AreEqual(1, loginCount, "The waiting call must reuse the token of the first call");
    }

    /// <summary>
    /// Create a clock that answers the given time
    /// </summary>
    /// <param name="now">Source of the current time</param>
    /// <returns>Clock</returns>
    private static TimeProvider CreateClock(Func<DateTimeOffset> now)
    {
        var clock = Substitute.For<TimeProvider>();

        clock.GetUtcNow().Returns(_ => now());

        return clock;
    }

    /// <summary>
    /// Create a 429 response with the given Retry-After header
    /// </summary>
    /// <param name="retryAfter">Retry-After header value</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateTooManyRequests(RetryConditionHeaderValue retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        response.Headers.RetryAfter = retryAfter;

        return response;
    }

    /// <summary>
    /// Create a handler that records every request URI, answers the logins with the given responses in turn
    /// (the last one repeats) and every other request with the current user
    /// </summary>
    /// <param name="requests">List that receives the request URIs</param>
    /// <param name="loginResponses">Factories of the login responses</param>
    /// <returns>Handler</returns>
    private static DelegateHttpMessageHandler CreateLoginSequenceHandler(List<string> requests, params Func<HttpResponseMessage>[] loginResponses)
    {
        var loginCount = 0;

        return new DelegateHttpMessageHandler(request =>
                                              {
                                                  requests.Add(request.RequestUri!.AbsoluteUri);

                                                  if (request.RequestUri.AbsolutePath.EndsWith("/users/login", StringComparison.Ordinal) == false)
                                                  {
                                                      return CreateJsonResponse("""{ "username": "acme" }""");
                                                  }

                                                  var index = Math.Min(loginCount, loginResponses.Length - 1);

                                                  loginCount++;

                                                  return loginResponses[index]();
                                              });
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
    /// <param name="loginRetryAfterRegistry">Registry of the login windows; null creates a new one</param>
    /// <returns>Docker Hub client</returns>
    private static DockerHubClient CreateClient(HttpClient httpClient,
                                                string registry = "docker.io",
                                                string? userName = "acme",
                                                TestLogger<DockerHubClient>? logger = null,
                                                LoginRetryAfterRegistry? loginRetryAfterRegistry = null)
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
                                   new TestOptionsMonitor<DockerUpdateGuardOptions>(options),
                                   loginRetryAfterRegistry ?? new LoginRetryAfterRegistry(TimeProvider.System),
                                   new DockerHubAccessTokenCache(TimeProvider.System));
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

    /// <summary>
    /// Assert a call answered an unreadable body as failed with the fixed text and logged one warning 3213 with its exception
    /// </summary>
    /// <typeparam name="T">Result data type</typeparam>
    /// <param name="result">Result of the call</param>
    /// <param name="expectedMessage">Expected failure message</param>
    /// <param name="targetName">Target the warning names</param>
    /// <param name="logger">Captured logger</param>
    private static void AssertInvalidResponse<T>(ExternalOperationResult<T> result,
                                                 string expectedMessage,
                                                 string targetName,
                                                 TestLogger<DockerHubClient> logger)
    {
        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "An unreadable body must be answered as failed");
        Assert.AreEqual(expectedMessage, result.Message, "An unreadable body must be reported as an invalid response");

        var warning = Assert.ContainsSingle(entry => entry.EventId.Id == 3213 && entry.LogLevel == LogLevel.Warning,
                                            logger.Entries,
                                            "An unreadable body must be logged once as warning 3213");

        Assert.IsNotNull(warning.Exception, "The warning must carry the exception");
        Assert.StartsWith("Docker Hub ", warning.Message, "The warning must name Docker Hub");
        Assert.EndsWith($" request for {targetName} failed", warning.Message, "The warning must name the target of the call");
    }

    /// <summary>
    /// Run one Docker Hub call by name
    /// </summary>
    /// <param name="client">Client under test</param>
    /// <param name="operation">Name of the call</param>
    /// <param name="registry">Registry of the references</param>
    /// <returns>Status and message of the result</returns>
    private static async Task<(ExternalOperationStatus Status, string Message)> RunCallAsync(DockerHubClient client, string operation, string registry)
    {
        var imageReference = new ImageReference
                             {
                                 Registry = registry,
                                 Repository = "acme/api",
                                 Tag = "latest",
                             };

        switch (operation)
        {
            case "CurrentUser":
                {
                    var result = await client.GetCurrentUserAsync(CancellationToken.None).ConfigureAwait(false);

                    return (result.Status, result.Message ?? string.Empty);
                }

            case "Repositories":
                {
                    var result = await client.GetRepositoriesAsync("acme", CancellationToken.None).ConfigureAwait(false);

                    return (result.Status, result.Message ?? string.Empty);
                }

            case "Repository":
                {
                    var result = await client.GetRepositoryAsync(imageReference, CancellationToken.None).ConfigureAwait(false);

                    return (result.Status, result.Message ?? string.Empty);
                }

            case "Tag":
                {
                    var result = await client.GetTagAsync(imageReference, "linux", "amd64", CancellationToken.None).ConfigureAwait(false);

                    return (result.Status, result.Message ?? string.Empty);
                }

            default:
                {
                    var result = await client.GetTagsAsync(registry, "acme/api", cancellationToken: CancellationToken.None).ConfigureAwait(false);

                    return (result.Status, result.Message ?? string.Empty);
                }
        }
    }

    #endregion // Methods
}