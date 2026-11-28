using System.Net;
using System.Text;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for the pagination guards of <see cref="DockerHubClient"/>
/// </summary>
[TestClass]
public class DockerHubClientPaginationTests
{
    #region Constants

    /// <summary>
    /// Number of requests after which the repeated-link fixtures end the listing, so an implementation without the check does not loop forever
    /// </summary>
    private const int RepeatSafetyLimit = 3;

    /// <summary>
    /// Bearer token the login response hands out
    /// </summary>
    private const string LoginToken = "header.eyJleHAiOjQxMDI0NDQ4MDB9.signature";

    /// <summary>
    /// Docker Hub login response
    /// </summary>
    private const string LoginResponseJson = $$"""{ "token": "{{LoginToken}}" }""";

    /// <summary>
    /// Listing page without entries and without a next link
    /// </summary>
    private const string EmptyLastPageJson = """{ "next": null, "results": [] }""";

    #endregion // Constants

    #region Methods

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
    /// Verify a Docker Hub tag listing that never ends stops failed after the page cap
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task DockerHubClientGetTagsAsyncEndlessNextLinkReturnsFailedAtPageCapAsync()
    {
        var listingRequests = 0;
        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request,
                                                                                        () =>
                                                                                        {
                                                                                            listingRequests++;

                                                                                            return CreateTagListingPage(request.RequestUri!, PaginationLinkGuard.MaxListingPages * 2);
                                                                                        }));
        using var httpClient = new HttpClient(handler);

        httpClient.BaseAddress = new Uri("https://hub.docker.com/");

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetTagsAsync("docker.io", "acme/api", cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A tag listing that does not end within the page cap must fail");
        Assert.IsNull(result.Data, "A stopped tag listing must hand out no partial data");
        Assert.AreEqual($"Docker Hub tag listing for 'acme/api' stopped: the listing did not end within {PaginationLinkGuard.MaxListingPages} pages",
                        result.Message,
                        "The failure message must name the listing and the page cap");
        Assert.AreEqual(PaginationLinkGuard.MaxListingPages, listingRequests, "Exactly the page cap of listing requests must be sent");
        Assert.ContainsSingle(entry => entry.EventId.Id == 3214 && entry.LogLevel == LogLevel.Warning,
                              logger.Entries,
                              "The page cap must be logged once as warning 3214");
    }

    /// <summary>
    /// Verify a Docker Hub repository listing that never ends stops failed after the page cap
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task DockerHubClientGetRepositoriesAsyncEndlessNextLinkReturnsFailedAtPageCapAsync()
    {
        var listingRequests = 0;
        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request,
                                                                                        () =>
                                                                                        {
                                                                                            listingRequests++;

                                                                                            return CreateRepositoryListingPage(request.RequestUri!, PaginationLinkGuard.MaxListingPages * 2);
                                                                                        }));
        using var httpClient = new HttpClient(handler);

        httpClient.BaseAddress = new Uri("https://hub.docker.com/");

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetRepositoriesAsync("acme", CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A repository listing that does not end within the page cap must fail");
        Assert.IsNull(result.Data, "A stopped repository listing must hand out no partial data");
        Assert.AreEqual($"Docker Hub repository listing for 'acme' stopped: the listing did not end within {PaginationLinkGuard.MaxListingPages} pages",
                        result.Message,
                        "The failure message must name the listing and the page cap");
        Assert.AreEqual(PaginationLinkGuard.MaxListingPages, listingRequests, "Exactly the page cap of listing requests must be sent");
        Assert.ContainsSingle(entry => entry.EventId.Id == 3214 && entry.LogLevel == LogLevel.Warning,
                              logger.Entries,
                              "The page cap must be logged once as warning 3214");
    }

    /// <summary>
    /// Verify a Docker Hub tag listing that ends exactly on the page cap succeeds with every tag
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task DockerHubClientGetTagsAsyncLastPageAtPageCapReturnsSucceededAsync()
    {
        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateTagListingPage(request.RequestUri!, PaginationLinkGuard.MaxListingPages)));
        using var httpClient = new HttpClient(handler);

        httpClient.BaseAddress = new Uri("https://hub.docker.com/");

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetTagsAsync("docker.io", "acme/api", cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A tag listing of exactly the page cap must succeed");
        Assert.IsNotNull(result.Data, "A completed tag listing must hand out its data");
        Assert.HasCount(PaginationLinkGuard.MaxListingPages, result.Data, "Every page up to the cap must contribute its tag");
        Assert.DoesNotContain(entry => entry.EventId.Id == 3214 || entry.EventId.Id == 3215,
                              logger.Entries,
                              "A listing that ends on the cap must log neither the page cap nor a repeated link");
    }

    /// <summary>
    /// Verify a Docker Hub repository listing that ends exactly on the page cap succeeds with every repository
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task DockerHubClientGetRepositoriesAsyncLastPageAtPageCapReturnsSucceededAsync()
    {
        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request, () => CreateRepositoryListingPage(request.RequestUri!, PaginationLinkGuard.MaxListingPages)));
        using var httpClient = new HttpClient(handler);

        httpClient.BaseAddress = new Uri("https://hub.docker.com/");

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetRepositoriesAsync("acme", CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A repository listing of exactly the page cap must succeed");
        Assert.IsNotNull(result.Data, "A completed repository listing must hand out its data");
        Assert.HasCount(PaginationLinkGuard.MaxListingPages, result.Data, "Every page up to the cap must contribute its repository");
        Assert.DoesNotContain(entry => entry.EventId.Id == 3214 || entry.EventId.Id == 3215,
                              logger.Entries,
                              "A listing that ends on the cap must log neither the page cap nor a repeated link");
    }

    /// <summary>
    /// Verify a Docker Hub tag listing stops failed when the next link names the page just read
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task DockerHubClientGetTagsAsyncRepeatedNextLinkReturnsFailedAsync()
    {
        const string firstPage = "https://hub.docker.com/v2/namespaces/acme/repositories/api/tags?page_size=100";

        var listingRequests = 0;
        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request,
                                                                                        () =>
                                                                                        {
                                                                                            listingRequests++;

                                                                                            return listingRequests > RepeatSafetyLimit ? CreateJsonResponse(EmptyLastPageJson) : CreateTagPageResponse(firstPage);
                                                                                        }));
        using var httpClient = new HttpClient(handler);

        httpClient.BaseAddress = new Uri("https://hub.docker.com/");

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetTagsAsync("docker.io", "acme/api", cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        AssertRepeatedLinkStop(result.Status,
                               result.Data is null,
                               result.Message,
                               "Docker Hub tag listing for 'acme/api'",
                               listingRequests,
                               logger);
    }

    /// <summary>
    /// Verify a Docker Hub tag listing stops failed when the next link names the page just read with another fragment
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task DockerHubClientGetTagsAsyncRepeatedNextLinkWithOtherFragmentReturnsFailedAsync()
    {
        const string firstPage = "https://hub.docker.com/v2/namespaces/acme/repositories/api/tags?page_size=100";

        var listingRequests = 0;
        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request,
                                                                                        () =>
                                                                                        {
                                                                                            listingRequests++;

                                                                                            return listingRequests > RepeatSafetyLimit ? CreateJsonResponse(EmptyLastPageJson) : CreateTagPageResponse($"{firstPage}#other");
                                                                                        }));
        using var httpClient = new HttpClient(handler);

        httpClient.BaseAddress = new Uri("https://hub.docker.com/");

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetTagsAsync("docker.io", "acme/api", cancellationToken: CancellationToken.None)
                                 .ConfigureAwait(false);

        AssertRepeatedLinkStop(result.Status,
                               result.Data is null,
                               result.Message,
                               "Docker Hub tag listing for 'acme/api'",
                               listingRequests,
                               logger);
    }

    /// <summary>
    /// Verify a Docker Hub repository listing stops failed when the next link names the page just read
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task DockerHubClientGetRepositoriesAsyncRepeatedNextLinkReturnsFailedAsync()
    {
        const string firstPage = "https://hub.docker.com/v2/repositories/acme/?page_size=100";

        var listingRequests = 0;
        using var handler = new DelegateHttpMessageHandler(request => CreateHubResponse(request,
                                                                                        () =>
                                                                                        {
                                                                                            listingRequests++;

                                                                                            return CreateJsonResponse(listingRequests > RepeatSafetyLimit ? EmptyLastPageJson : $$"""{ "next": "{{firstPage}}", "results": [ { "namespace": "acme", "name": "api" } ] }""");
                                                                                        }));
        using var httpClient = new HttpClient(handler);

        httpClient.BaseAddress = new Uri("https://hub.docker.com/");

        var logger = new TestLogger<DockerHubClient>();
        var client = CreateClient(httpClient, logger: logger);

        var result = await client.GetRepositoriesAsync("acme", CancellationToken.None)
                                 .ConfigureAwait(false);

        AssertRepeatedLinkStop(result.Status,
                               result.Data is null,
                               result.Message,
                               "Docker Hub repository listing for 'acme'",
                               listingRequests,
                               logger);
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
    /// Assert a listing stopped failed without data because its next link repeated a page already read
    /// </summary>
    /// <param name="status">Result status</param>
    /// <param name="hasNoData">Whether the result carries no data</param>
    /// <param name="message">Result message</param>
    /// <param name="listingDescription">Expected description of the listing</param>
    /// <param name="listingRequests">Number of listing requests sent</param>
    /// <param name="logger">Captured logger</param>
    private static void AssertRepeatedLinkStop(ExternalOperationStatus status,
                                               bool hasNoData,
                                               string? message,
                                               string listingDescription,
                                               int listingRequests,
                                               TestLogger<DockerHubClient> logger)
    {
        Assert.AreEqual(ExternalOperationStatus.Failed, status, "A repeated next-page link must fail the listing");
        Assert.IsTrue(hasNoData, "A stopped listing must hand out no partial data");
        Assert.AreEqual($"{listingDescription} stopped: the next-page link repeated a page already read",
                        message,
                        "The failure message must name the listing and the repeated link");
        Assert.AreEqual(1, listingRequests, "No request may be sent for a page already read");
        Assert.ContainsSingle(entry => entry.EventId.Id == 3215 && entry.LogLevel == LogLevel.Warning,
                              logger.Entries,
                              "A repeated next-page link must be logged once as warning 3215");
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
    /// Create the tag listing page a request asks for; every page links to the next one up to the last page
    /// </summary>
    /// <param name="requestUri">Request address</param>
    /// <param name="lastPage">Number of the last page</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateTagListingPage(Uri requestUri, int lastPage)
    {
        var page = GetListingPage(requestUri);
        var next = page >= lastPage
                       ? "null"
                       : $"\"https://hub.docker.com/v2/namespaces/acme/repositories/api/tags?page={page + 1}&page_size=100\"";

        return CreateJsonResponse($$"""{ "next": {{next}}, "results": [ { "name": "t{{page}}", "digest": "sha256:t{{page}}", "last_pushed": "2025-04-02T10:00:00Z" } ] }""");
    }

    /// <summary>
    /// Create the repository listing page a request asks for; every page links to the next one up to the last page
    /// </summary>
    /// <param name="requestUri">Request address</param>
    /// <param name="lastPage">Number of the last page</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateRepositoryListingPage(Uri requestUri, int lastPage)
    {
        var page = GetListingPage(requestUri);
        var next = page >= lastPage
                       ? "null"
                       : $"\"https://hub.docker.com/v2/repositories/acme/?page={page + 1}&page_size=100\"";

        return CreateJsonResponse($$"""{ "next": {{next}}, "results": [ { "namespace": "acme", "name": "r{{page}}" } ] }""");
    }

    /// <summary>
    /// Read the page number of a listing request; the first request carries none
    /// </summary>
    /// <param name="requestUri">Request address</param>
    /// <returns>Page number</returns>
    private static int GetListingPage(Uri requestUri)
    {
        var pageEntry = requestUri.Query.TrimStart('?')
                                        .Split('&')
                                        .FirstOrDefault(entry => entry.StartsWith("page=", StringComparison.Ordinal));

        return pageEntry is null
                   ? 1
                   : int.Parse(pageEntry["page=".Length..], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Create a Docker Hub client for tests
    /// </summary>
    /// <param name="httpClient">Configured HTTP client</param>
    /// <param name="registry">Configured Docker Hub registry value</param>
    /// <param name="userName">Configured Docker Hub user name</param>
    /// <param name="logger">Logger that captures the client's entries; null creates a new one</param>
    /// <param name="loginRetryAfterRegistry">Registry of the login windows; null creates a new one</param>
    /// <param name="accessTokenCache">Shared cache of the access token; null creates a new one</param>
    /// <param name="pat">Configured personal access token</param>
    /// <param name="requestLimiter">Shared request limiter; null creates a new one from the options</param>
    /// <returns>Docker Hub client</returns>
    private static DockerHubClient CreateClient(HttpClient httpClient,
                                                string registry = "docker.io",
                                                string? userName = "acme",
                                                TestLogger<DockerHubClient>? logger = null,
                                                LoginRetryAfterRegistry? loginRetryAfterRegistry = null,
                                                DockerHubAccessTokenCache? accessTokenCache = null,
                                                string pat = "test-pat",
                                                DockerHubRequestLimiter? requestLimiter = null)
    {
        var options = new DockerUpdateGuardOptions
                      {
                          DockerHub = new DockerHubOptions
                                      {
                                          Registry = registry,
                                          UserName = userName,
                                          Pat = pat,
                                          RequestTimeoutSeconds = 30,
                                      },
                      };

        var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(options);

        return new DockerHubClient(httpClient,
                                   logger ?? new TestLogger<DockerHubClient>(),
                                   optionsMonitor,
                                   loginRetryAfterRegistry ?? new LoginRetryAfterRegistry(TimeProvider.System),
                                   accessTokenCache ?? new DockerHubAccessTokenCache(TimeProvider.System),
                                   requestLimiter ?? new DockerHubRequestLimiter(optionsMonitor));
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

    #endregion // Methods
}