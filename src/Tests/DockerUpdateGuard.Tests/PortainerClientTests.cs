using System.Net;
using System.Net.Http.Headers;
using System.Text;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Portainer;
using DockerUpdateGuard.Portainer.Data;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Extensions.Logging;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="PortainerClient"/>
/// </summary>
[TestClass]
public class PortainerClientTests
{
    #region Constants

    /// <summary>
    /// Portainer base URL used by the tests
    /// </summary>
    private const string BaseUrl = "https://portainer.example.test";

    /// <summary>
    /// Portainer system status endpoint
    /// </summary>
    private const string StatusUrl = "https://portainer.example.test/api/system/status";

    /// <summary>
    /// Portainer endpoints endpoint
    /// </summary>
    private const string EndpointsUrl = "https://portainer.example.test/api/endpoints";

    /// <summary>
    /// Portainer environment check of environment 7
    /// </summary>
    private const string EndpointCheckUrl = "https://portainer.example.test/api/endpoints/7";

    /// <summary>
    /// Portainer authentication endpoint
    /// </summary>
    private const string AuthUrl = "https://portainer.example.test/api/auth";

    /// <summary>
    /// Full container identifier of the snapshot used by the tests, 64 lowercase hexadecimal characters
    /// </summary>
    private const string ContainerId = "f907e278af7c63d535c4c440b720247f8c2dca219ffc8167dd3865b16004319e";

    /// <summary>
    /// Another full container identifier, used for a container of the same name that was recreated since the scan
    /// </summary>
    private const string OtherContainerId = "d9298a10d1b0735837dc4bd85dac641b0f3cef27a47e5d53a54f2f3f5b2fcffa";

    /// <summary>
    /// Path of the container list within environment 7
    /// </summary>
    private const string ContainerListPath = "/api/endpoints/7/docker/containers/json";

    /// <summary>
    /// Path prefix of the container actions of the test container within environment 7
    /// </summary>
    private const string ActionPathPrefix = "/api/endpoints/7/docker/containers/" + ContainerId + "/";

    /// <summary>
    /// Portainer container listing endpoint filtered by the container identifier within endpoint 7
    /// </summary>
    private const string ContainersUrl = BaseUrl + ContainerListPath + "?all=true&filters=%7B%22id%22%3A%5B%22" + ContainerId + "%22%5D%7D";

    /// <summary>
    /// Portainer container restart action endpoint for endpoint 7 and the test container
    /// </summary>
    private const string RestartActionUrl = BaseUrl + ActionPathPrefix + "restart";

    /// <summary>
    /// Portainer container listing response containing the test container
    /// </summary>
    private const string ContainerFoundJson = """[{"Id":"f907e278af7c63d535c4c440b720247f8c2dca219ffc8167dd3865b16004319e","Names":["/my-container"]}]""";

    /// <summary>
    /// Portainer environment answer of environment 7
    /// </summary>
    private const string EndpointJson = """{"Id":7,"Name":"local"}""";

    /// <summary>
    /// Docker instance display name used by the tests
    /// </summary>
    private const string InstanceName = "Production";

    /// <summary>
    /// Container name used as the Portainer action resource
    /// </summary>
    private const string ContainerName = "my-container";

    /// <summary>
    /// Action name of a restart in messages, logs and paths
    /// </summary>
    private const string RestartAction = "restart";

    /// <summary>
    /// Portainer endpoint identifier used by the tests
    /// </summary>
    private const string EndpointId = "7";

    /// <summary>
    /// Portainer API token (PAT) used by the tests
    /// </summary>
    private const string PatToken = "pat-token";

    /// <summary>
    /// Portainer username used by the tests
    /// </summary>
    private const string AdminUsername = "admin";

    /// <summary>
    /// Portainer base URL using plaintext HTTP
    /// </summary>
    private const string InsecureBaseUrl = "http://portainer.example.test";

    /// <summary>
    /// Portainer authentication endpoint using plaintext HTTP
    /// </summary>
    private const string InsecureAuthUrl = "http://portainer.example.test/api/auth";

    /// <summary>
    /// Event identifier of the plaintext HTTP warning
    /// </summary>
    private const int InsecureHttpEventId = 3311;

    /// <summary>
    /// Event identifier of the ambiguous environment warning
    /// </summary>
    private const int AmbiguousEventId = 3312;

    /// <summary>
    /// Event identifier of the failed environment list warning
    /// </summary>
    private const int ListFailedEventId = 3313;

    /// <summary>
    /// Name of the response header carrying the total number of environments
    /// </summary>
    private const string TotalCountHeaderName = "X-Total-Count";

    /// <summary>
    /// Host name marker injected into exception texts that must never reach a result message
    /// </summary>
    private const string HostMarker = "portainer.internal.test";

    /// <summary>
    /// Message of a container the Docker Engine does not know by the snapshot's identifier (E5)
    /// </summary>
    private const string ContainerChangedMessage = "Container changed since the last scan; run a runtime scan and try again";

    /// <summary>
    /// Suffix of an outcome whose action request may have reached the Docker Engine (E4)
    /// </summary>
    private const string MayHaveBeenAppliedSuffix = "; the action may have been applied, check the container before repeating it";

    /// <summary>
    /// Request timeout in seconds of the bound tests, distinct from the default of 15 seconds
    /// </summary>
    private const int BoundTestTimeoutSeconds = 20;

    /// <summary>
    /// Seconds a test handler waits for a fired bound to cancel its request before it answers anyway
    /// </summary>
    private const int SafetyDelaySeconds = 5;

    #endregion // Constants

    #region Properties

    /// <summary>
    /// Test context providing information about and functionality for the current test run
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify capability checks for disabled Portainer integrations return a not-configured outcome without contacting Portainer
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWhenPortainerDisabledReturnsNotConfiguredAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName, options => options.Enabled = false);

            var result = await client.CheckCapabilityAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.NotConfigured, result.Status, "A disabled Portainer integration must be reported as not configured");
            Assert.AreEqual("Portainer is not configured for this Docker instance",
                            result.Message,
                            "A disabled Portainer integration must keep today's capability message");
            Assert.IsEmpty(handler.Requests, "A disabled Portainer integration must not contact Portainer");
            Assert.Contains(entry => entry.EventId.Id == 3300
                                     && entry.LogLevel == LogLevel.Information
                                     && entry.Message.Contains(InstanceName, StringComparison.Ordinal),
                            logger.Entries,
                            "Disabled Portainer integrations must log the skip decision with the configured instance name");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify capability checks without a configured base URL return a not-configured outcome without contacting Portainer
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWhenBaseUrlMissingReturnsNotConfiguredAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.Enabled = true;
                                                            options.BaseUrl = null;
                                                        });

            var result = await client.CheckCapabilityAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.NotConfigured, result.Status, "A missing Portainer base URL must be reported as not configured");
            Assert.AreEqual("Portainer base URL is not configured",
                            result.Message,
                            "A missing Portainer base URL must produce a clear capability message");
            Assert.IsEmpty(handler.Requests, "A missing Portainer base URL must not contact Portainer");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a failing status check is reported as unsupported and logs a warning
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWhenStatusRequestFailsReturnsUnsupportedAndLogsWarningAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(StatusUrl, new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);

            var result = await client.CheckCapabilityAsync(CreateFailureInstanceOptions(), CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, "A failing status check must not support actions");
            Assert.AreEqual("Portainer connection failed: HTTP 500",
                            result.Message,
                            "A failing status check must keep today's message with the returned HTTP status code");
            Assert.Contains(entry => entry.EventId.Id == 3305
                                     && entry.LogLevel == LogLevel.Warning
                                     && entry.Message.Contains(InstanceName, StringComparison.Ordinal),
                            logger.Entries,
                            "A failing status check must log a warning naming the Docker instance");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify the Portainer endpoint identifier is automatically resolved when not configured
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncResolvesEndpointAutomaticallyAndLogsResolutionAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(StatusUrl, new HttpResponseMessage(HttpStatusCode.OK));
            handler.AddJsonResponse(EndpointsUrl, """[{"Id":7,"Name":"local"}]""");
            handler.AddJsonResponse(EndpointCheckUrl, EndpointJson);

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName, options => options.ApiToken = PatToken);

            var result = await client.CheckCapabilityAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A resolved endpoint must support actions");
            Assert.IsNotNull(result.Data, "A succeeded capability check must carry its data");
            Assert.AreEqual(EndpointId,
                            result.Data.EndpointId,
                            "The automatically resolved endpoint identifier must be reported");
            Assert.Contains(entry => entry.EventId.Id == 3304
                                     && entry.LogLevel == LogLevel.Information
                                     && entry.Message.Contains(EndpointId, StringComparison.Ordinal),
                            logger.Entries,
                            "Automatic endpoint resolution must log the resolved endpoint identifier");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify capability checks report unsupported actions when no Portainer endpoint can be resolved
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWhenNoEndpointFoundReturnsUnsupportedAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(StatusUrl, new HttpResponseMessage(HttpStatusCode.OK));
            handler.AddJsonResponse(EndpointsUrl, "[]");

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName, options => options.ApiToken = PatToken);

            var result = await client.CheckCapabilityAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, "No resolvable endpoint must not support actions");
            Assert.AreEqual("No Portainer endpoint found",
                            result.Message,
                            "An empty endpoint list must produce a clear capability message");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify capability checks catch authentication exceptions and report a failed outcome
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWhenLoginFailsReturnsFailedAndLogsExceptionAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(AuthUrl, new HttpResponseMessage(HttpStatusCode.Unauthorized));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.Username = AdminUsername;
                                                            options.Password = "wrong-password";
                                                            options.EndpointId = EndpointId;
                                                        });

            var result = await client.CheckCapabilityAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A failed login must fail the capability check");
            Assert.IsNotNull(result.Message, "A failed login must produce a capability message");
            Assert.StartsWith("Portainer connection error: ",
                              result.Message,
                              "A failed login during capability checks must surface a connection-error message");
            Assert.Contains("HTTP 401",
                            result.Message,
                            "A failed login during capability checks must keep today's authentication error");
            Assert.Contains(entry => entry.EventId.Id == 3306
                                     && entry.LogLevel == LogLevel.Error,
                            logger.Entries,
                            "A failed login during capability checks must log the exception");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a configured environment Portainer does not know, or an EndpointId that is not all digits, is reported as an unknown environment
    /// </summary>
    /// <param name="endpointId">Configured EndpointId</param>
    /// <param name="expectedCheckRequests">Number of requests expected besides the status request</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    [DataRow("7", 1)]
    [DataRow("1/../../users", 0)]
    [DataRow("1?x=", 0)]
    [DataRow(" 1 ", 0)]
    public async Task PortainerClientCheckCapabilityAsyncWithUnknownEndpointIdReportsEnvironmentNotFoundAsync(string endpointId, int expectedCheckRequests)
    {
        var handler = new SequenceHttpMessageHandler();
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(StatusUrl, new HttpResponseMessage(HttpStatusCode.OK));
            handler.AddResponse(EndpointCheckUrl, new HttpResponseMessage(HttpStatusCode.NotFound));

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.ApiToken = PatToken;
                                                            options.EndpointId = endpointId;
                                                        });

            var result = await client.CheckCapabilityAsync(instanceOptions, TestContext.CancellationToken)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, $"The environment '{endpointId}' must not support actions");
            Assert.AreEqual($"Portainer environment {endpointId} not found; check Portainer:EndpointId of Docker instance 'Production'",
                            result.Message,
                            $"The environment '{endpointId}' must be reported as not found with the key to check");
            Assert.AreEqual(expectedCheckRequests,
                            handler.Requests.Count(request => request.RequestUri != StatusUrl),
                            $"The environment '{endpointId}' must be checked only when it is all digits, so no other value reaches a URL");
            Assert.DoesNotContain(entry => entry.EventId.Id == 3304,
                                  logger.Entries,
                                  "An unknown environment must never be reported as resolved");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify action execution for disabled Portainer integrations returns a not-configured outcome without contacting Portainer
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWhenPortainerDisabledReturnsNotConfiguredAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName, options => options.Enabled = false);

            var result = await client.ExecuteContainerActionAsync(instanceOptions, CreateRestartRequest(), CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.NotConfigured, result.Status, "Action execution against a disabled Portainer integration must not run");
            Assert.AreEqual("Portainer is not configured for this Docker instance",
                            result.Message,
                            "A disabled Portainer integration must produce the capability check's message");
            Assert.IsEmpty(handler.Requests, "A disabled Portainer integration must not contact Portainer");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify action types other than start, stop and restart of a container are rejected before Portainer is contacted
    /// </summary>
    /// <param name="actionType">Requested action type</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow(ContainerActionType.NotSet)]
    [DataRow(ContainerActionType.RedeployStack)]
    [DataRow(ContainerActionType.UpdateService)]
    public async Task PortainerClientExecuteContainerActionAsyncWithUnsupportedActionTypeRejectsWithoutContactingPortainerAsync(ContainerActionType actionType)
    {
        var handler = new SequenceHttpMessageHandler();

        try
        {
            AddLookupResponses(handler);

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             new TestLogger<PortainerClient>(),
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);

            var result = await client.ExecuteContainerActionAsync(CreateFailureInstanceOptions(), CreateActionRequest(actionType), TestContext.CancellationToken)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, $"The action type {actionType} must be rejected");
            Assert.AreEqual($"Action '{actionType}' is not supported; allowed: start, stop, restart",
                            result.Message,
                            $"The rejection of {actionType} must name the type and the allowed actions");
            Assert.IsEmpty(handler.Requests, $"The action type {actionType} must be rejected before Portainer is contacted");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify each executable action type posts the Docker action path of its action to the snapshot's container identifier
    /// </summary>
    /// <param name="actionType">Requested action type</param>
    /// <param name="actionName">Docker action expected in the path</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow(ContainerActionType.StartContainer, "start")]
    [DataRow(ContainerActionType.StopContainer, "stop")]
    [DataRow(ContainerActionType.RestartContainer, "restart")]
    public async Task PortainerClientExecuteContainerActionAsyncPostsActionPathOfActionTypeAsync(ContainerActionType actionType, string actionName)
    {
        var handler = new SequenceHttpMessageHandler();

        try
        {
            AddLookupResponses(handler);
            handler.AddResponse($"{BaseUrl}{ActionPathPrefix}{actionName}", new HttpResponseMessage(HttpStatusCode.NoContent));

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             new TestLogger<PortainerClient>(),
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);

            var result = await client.ExecuteContainerActionAsync(CreateFailureInstanceOptions(), CreateActionRequest(actionType), TestContext.CancellationToken)
                                     .ConfigureAwait(false);

            var posts = handler.Requests.Where(request => request.Method == "POST")
                                        .ToList();

            Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, $"The action type {actionType} must be executed");
            Assert.HasCount(1, posts, $"The action type {actionType} must send exactly one action request");
            Assert.AreEqual($"{BaseUrl}{ActionPathPrefix}{actionName}",
                            posts[0].RequestUri,
                            $"The action type {actionType} must post the '{actionName}' path of the snapshot's container identifier");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a configured API token is used as the X-API-Key header and skips the username/password login
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWithApiTokenUsesApiKeyHeaderAndSkipsLoginAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            AddLookupResponses(handler);
            handler.AddResponse(RestartActionUrl, new HttpResponseMessage(HttpStatusCode.NoContent));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);

            var result = await client.ExecuteContainerActionAsync(CreateFailureInstanceOptions(), CreateRestartRequest(), CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A configured API token must allow the action to succeed");
            Assert.DoesNotContain(request => request.RequestUri == AuthUrl,
                                  handler.Requests,
                                  "A configured API token must skip the username/password login");
            Assert.Contains(request => request.RequestUri == ContainersUrl
                                       && request.ApiKeyHeader == PatToken,
                            handler.Requests,
                            "The configured API token must be sent as the X-API-Key header");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify username/password credentials log in and use the returned JWT as a bearer token
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWithUsernamePasswordLogsInAndUsesBearerTokenAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddJsonResponse(AuthUrl, """{"jwt":"jwt-token-xyz"}""");
            AddLookupResponses(handler);
            handler.AddResponse(RestartActionUrl, new HttpResponseMessage(HttpStatusCode.NoContent));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);

            var result = await client.ExecuteContainerActionAsync(CreateLoginInstanceOptions(InstanceName), CreateRestartRequest(), CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A successful username/password login must allow the action to succeed");
            Assert.Contains(request => request.RequestUri == ContainersUrl
                                       && request.AuthorizationScheme == "Bearer"
                                       && request.AuthorizationParameter == "jwt-token-xyz",
                            handler.Requests,
                            "The JWT returned by the login request must be sent as a bearer token");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a successful login that returns no JWT fails the action and logs the authentication failure
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWhenLoginReturnsNoJwtFailsAndLogsAuthFailedAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddJsonResponse(AuthUrl, "{}");

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);

            var result = await client.ExecuteContainerActionAsync(CreateLoginInstanceOptions(InstanceName), CreateRestartRequest(), CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A login response without a JWT must fail the action");
            Assert.Contains("returned no JWT token",
                            result.Message ?? string.Empty,
                            "A missing JWT must produce a clear failure message");
            Assert.Contains(entry => entry.EventId.Id == 3303
                                     && entry.LogLevel == LogLevel.Warning
                                     && entry.Message.Contains(BaseUrl, StringComparison.Ordinal),
                            logger.Entries,
                            "A missing JWT must log the authentication failure for the configured base URL");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a failing login request fails the action and logs the authentication failure
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWhenLoginFailsFailsAndLogsAuthFailedAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(AuthUrl, new HttpResponseMessage(HttpStatusCode.Unauthorized));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.Username = AdminUsername;
                                                            options.Password = "wrong-password";
                                                            options.EndpointId = EndpointId;
                                                        });

            var result = await client.ExecuteContainerActionAsync(instanceOptions, CreateRestartRequest(), CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A failing login request must fail the action");
            Assert.Contains("HTTP 401",
                            result.Message ?? string.Empty,
                            "A failing login request must surface the returned HTTP status code");
            Assert.DoesNotContain(request => request.Method == "POST" && request.RequestUri != AuthUrl,
                                  handler.Requests,
                                  "A failing login must send no action request");
            Assert.Contains(entry => entry.EventId.Id == 3303
                                     && entry.LogLevel == LogLevel.Warning,
                            logger.Entries,
                            "A failing login request must log the authentication failure");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify action execution is unsupported when no Portainer endpoint can be resolved
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWhenNoEndpointFoundReturnsUnsupportedAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(StatusUrl, new HttpResponseMessage(HttpStatusCode.OK));
            handler.AddJsonResponse(EndpointsUrl, "[]");

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName, options => options.ApiToken = PatToken);

            var result = await client.ExecuteContainerActionAsync(instanceOptions, CreateRestartRequest(), CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, "Action execution must not run when no Portainer endpoint can be resolved");
            Assert.AreEqual("No Portainer endpoint found",
                            result.Message,
                            "An unresolved endpoint must produce a clear action-result message");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a container the Docker Engine does not know by the snapshot's identifier, also one of the same name with another identifier, ends without an action request
    /// </summary>
    /// <param name="containerListJson">Answer of the container list filtered by the identifier</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    [DataRow("[]")]
    [DataRow("""[{"Id":"d9298a10d1b0735837dc4bd85dac641b0f3cef27a47e5d53a54f2f3f5b2fcffa","Names":["/my-container"]}]""")]
    [DataRow("""[{"Id":"F907E278AF7C63D535C4C440B720247F8C2DCA219FFC8167DD3865B16004319E","Names":["/my-container"]}]""")]
    public async Task PortainerClientExecuteContainerActionAsyncWhenIdUnknownReportsContainerChangedWithoutActionAsync(string containerListJson)
    {
        var handler = new SequenceHttpMessageHandler();
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(StatusUrl, new HttpResponseMessage(HttpStatusCode.OK));
            handler.AddJsonResponse(EndpointCheckUrl, EndpointJson);
            handler.AddJsonResponse(ContainersUrl, containerListJson);
            handler.AddResponse(RestartActionUrl, new HttpResponseMessage(HttpStatusCode.NoContent));
            handler.AddResponse(BaseUrl + "/api/endpoints/7/docker/containers/" + OtherContainerId + "/restart", new HttpResponseMessage(HttpStatusCode.NoContent));

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);

            var result = await client.ExecuteContainerActionAsync(CreateFailureInstanceOptions(), CreateRestartRequest(), TestContext.CancellationToken)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.NotFound, result.Status, $"The container list {containerListJson} holds no container with the snapshot's identifier");
            Assert.AreEqual(ContainerChangedMessage, result.Message, "A container unknown by its identifier must be reported as changed since the scan");
            Assert.Contains(request => request.RequestUri == ContainersUrl,
                            handler.Requests,
                            "The container must be looked up by the snapshot's identifier");
            Assert.DoesNotContain(request => request.Method == "POST",
                                  handler.Requests,
                                  "No action request may be sent for a container unknown by its identifier, whatever its name");
            Assert.Contains(entry => entry.EventId.Id == 3307
                                     && entry.LogLevel == LogLevel.Warning
                                     && entry.Message.Contains(ContainerName, StringComparison.Ordinal)
                                     && entry.Message.Contains(InstanceName, StringComparison.Ordinal),
                            logger.Entries,
                            "A missing container must log a warning naming the container and Docker instance");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a container identifier that is not exactly 64 lowercase hexadecimal characters is rejected before any request
    /// </summary>
    /// <param name="containerId">Container identifier of the request</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    [DataRow("")]
    [DataRow("f907e278af7c")]
    [DataRow("f907e278af7c63d535c4c440b720247f8c2dca219ffc8167dd3865b16004319")]
    [DataRow("f907e278af7c63d535c4c440b720247f8c2dca219ffc8167dd3865b16004319e0")]
    [DataRow("F907E278AF7C63D535C4C440B720247F8C2DCA219FFC8167DD3865B16004319E")]
    [DataRow("f907e278af7c63d535c4c440b720247f8c2dca219ffc8167dd3865b16004319g")]
    [DataRow("f907e278af7c63d535c4c440b720247f8c2dca219ffc8167dd3865b16004/../")]
    [DataRow("my-container")]
    public async Task PortainerClientExecuteContainerActionAsyncWithMalformedContainerIdRejectsWithoutContactingPortainerAsync(string containerId)
    {
        var handler = new SequenceHttpMessageHandler();

        try
        {
            AddLookupResponses(handler);

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             new TestLogger<PortainerClient>(),
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var actionRequest = new PortainerContainerActionRequest
                                {
                                    ActionType = ContainerActionType.RestartContainer,
                                    ContainerId = containerId,
                                    ContainerName = ContainerName,
                                };

            var result = await client.ExecuteContainerActionAsync(CreateFailureInstanceOptions(), actionRequest, TestContext.CancellationToken)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.NotFound, result.Status, $"The container identifier '{containerId}' must be rejected");
            Assert.AreEqual(ContainerChangedMessage, result.Message, $"The container identifier '{containerId}' must be reported like an unknown container");
            Assert.IsEmpty(handler.Requests, $"The container identifier '{containerId}' must never reach a request");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a successful action reports the E1 message, logs the execution and sends exactly one action request
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task PortainerClientExecuteContainerActionAsyncWhenActionSucceedsReportsExecutedSuccessfullyAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var logger = new TestLogger<PortainerClient>();

        try
        {
            AddLookupResponses(handler);
            handler.AddResponse(RestartActionUrl, new HttpResponseMessage(HttpStatusCode.NoContent));

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);

            var result = await client.ExecuteContainerActionAsync(CreateFailureInstanceOptions(), CreateRestartRequest(), TestContext.CancellationToken)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A successful Portainer action must report success");
            Assert.IsNotNull(result.Data, "A successful action must carry its outcome data");
            Assert.AreEqual("Action 'restart' on 'my-container' executed successfully",
                            result.Data.Message,
                            "A successful action must carry the message of E1");
            Assert.IsFalse(result.Data.AlreadyInRequestedState, "A 2xx answer must not be reported as already in the requested state");
            Assert.HasCount(1,
                            handler.Requests.Where(request => request.Method == "POST").ToList(),
                            "Exactly one action request must be sent");
            Assert.Contains(entry => entry.EventId.Id == 3308
                                     && entry.LogLevel == LogLevel.Information
                                     && entry.Message.Contains(RestartAction, StringComparison.Ordinal)
                                     && entry.Message.Contains(ContainerName, StringComparison.Ordinal)
                                     && entry.Message.Contains(InstanceName, StringComparison.Ordinal),
                            logger.Entries,
                            "A successful Portainer action must log the action, container and Docker instance");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a 304 answer of the Docker Engine succeeds as already in the requested state with the E2 message
    /// </summary>
    /// <param name="actionType">Requested action type</param>
    /// <param name="actionName">Docker action of the type</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    [DataRow(ContainerActionType.StartContainer, "start")]
    [DataRow(ContainerActionType.StopContainer, "stop")]
    public async Task PortainerClientExecuteContainerActionAsyncWhenEngineAnswersNotModifiedSucceedsAlreadyInStateAsync(ContainerActionType actionType, string actionName)
    {
        var handler = new SequenceHttpMessageHandler();

        try
        {
            AddLookupResponses(handler);
            handler.AddResponse($"{BaseUrl}{ActionPathPrefix}{actionName}", new HttpResponseMessage(HttpStatusCode.NotModified));

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             new TestLogger<PortainerClient>(),
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);

            var result = await client.ExecuteContainerActionAsync(CreateFailureInstanceOptions(), CreateActionRequest(actionType), TestContext.CancellationToken)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, $"A 304 answer to '{actionName}' must succeed");
            Assert.IsNotNull(result.Data, "A succeeded action must carry its outcome data");
            Assert.IsTrue(result.Data.AlreadyInRequestedState, $"A 304 answer to '{actionName}' must be reported as already in the requested state");
            Assert.AreEqual($"Action '{actionName}' on 'my-container' had no effect: the container was already in the requested state",
                            result.Data.Message,
                            $"A 304 answer to '{actionName}' must carry the message of E2");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a client error answer to the action request fails with its HTTP status after one action request
    /// </summary>
    /// <param name="statusCode">HTTP status of the action answer</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(404)]
    public async Task PortainerClientExecuteContainerActionAsyncWhenActionAnswersClientErrorFailsWithStatusAsync(int statusCode)
    {
        var handler = new SequenceHttpMessageHandler();
        var logger = new TestLogger<PortainerClient>();

        try
        {
            AddLookupResponses(handler);
            handler.AddResponse(RestartActionUrl, new HttpResponseMessage((HttpStatusCode)statusCode));

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);

            var result = await client.ExecuteContainerActionAsync(CreateFailureInstanceOptions(), CreateRestartRequest(), TestContext.CancellationToken)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, $"An HTTP {statusCode} answer to the action must fail it");
            Assert.AreEqual($"Action 'restart' failed: HTTP {statusCode}",
                            result.Message,
                            $"An HTTP {statusCode} answer must carry the message of E3");
            Assert.HasCount(1,
                            handler.Requests.Where(request => request.Method == "POST").ToList(),
                            "Exactly one action request must be sent");
            Assert.Contains(entry => entry.EventId.Id == 3309
                                     && entry.LogLevel == LogLevel.Warning
                                     && entry.Message.Contains(RestartAction, StringComparison.Ordinal)
                                     && entry.Message.Contains(ContainerName, StringComparison.Ordinal),
                            logger.Entries,
                            "A failing Portainer action request must log a warning naming the action and container");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a server error answer to the action request is sent once through the real retry handler and reported as possibly applied
    /// </summary>
    /// <param name="statusCode">HTTP status of the action answer</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    [DataRow(500)]
    [DataRow(503)]
    public async Task PortainerClientExecuteContainerActionAsyncWhenActionAnswersServerErrorReportsMayHaveBeenAppliedAfterOneAttemptAsync(int statusCode)
    {
        var sequenceHandler = new SequenceHttpMessageHandler();
        var retryHandler = new TransientHttpRetryHandler(new TestOptionsMonitor<DockerUpdateGuardOptions>(new DockerUpdateGuardOptions
                                                                                                          {
                                                                                                              Scanning = new ScanningOptions
                                                                                                                         {
                                                                                                                             RetryCount = 2,
                                                                                                                         },
                                                                                                          }),
                                                         new TestLogger<TransientHttpRetryHandler>(),
                                                         (_, _) => Task.CompletedTask,
                                                         _ => 0,
                                                         TimeProvider.System)
                           {
                               InnerHandler = sequenceHandler,
                           };
        var httpClient = new HttpClient(retryHandler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            AddLookupResponses(sequenceHandler);
            sequenceHandler.AddResponse(RestartActionUrl, new HttpResponseMessage((HttpStatusCode)statusCode));
            sequenceHandler.AddResponse(RestartActionUrl, new HttpResponseMessage(HttpStatusCode.OK));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);

            var result = await client.ExecuteContainerActionAsync(CreateFailureInstanceOptions(), CreateRestartRequest(), TestContext.CancellationToken)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Unknown, result.Status, $"An HTTP {statusCode} answer may come after the action was applied");
            Assert.AreEqual($"Action 'restart' failed: HTTP {statusCode}{MayHaveBeenAppliedSuffix}",
                            result.Message,
                            $"An HTTP {statusCode} answer must carry the message of E4");
            Assert.HasCount(1,
                            sequenceHandler.Requests.Where(request => request.Method == "POST").ToList(),
                            "The action request must be sent exactly once because the server may already have executed it");
            Assert.Contains(entry => entry.EventId.Id == 3309
                                     && entry.LogLevel == LogLevel.Warning
                                     && entry.Message.Contains(RestartAction, StringComparison.Ordinal)
                                     && entry.Message.Contains(ContainerName, StringComparison.Ordinal),
                            logger.Entries,
                            "A failing Portainer action request must log a warning naming the action and container");
        }
        finally
        {
            httpClient.Dispose();
            retryHandler.Dispose();
            sequenceHandler.Dispose();
        }
    }

    /// <summary>
    /// Verify an action request that breaks off is reported as possibly applied with the fixed reason and without the exception text
    /// </summary>
    /// <param name="error">Transport error of the injected exception</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    [DataRow(HttpRequestError.ConnectionError)]
    [DataRow(HttpRequestError.ResponseEnded)]
    public async Task PortainerClientExecuteContainerActionAsyncWhenActionRequestBreaksOffReportsMayHaveBeenAppliedAsync(HttpRequestError error)
    {
        var injected = new HttpRequestException(error, "Error while copying content to a stream. portainer.internal.test");
        var logger = new TestLogger<PortainerClient>();
        var requests = new List<string>();
        using var handler = new DelegateHttpMessageHandler((request, _) => AnswerOrThrowAsync(request, "POST " + ActionPathPrefix + RestartAction, injected, requests));

        var client = new PortainerClient(CreateHttpClientFactory(handler),
                                         logger,
                                         new LoginRetryAfterRegistry(TimeProvider.System),
                                         TimeProvider.System);

        var result = await client.ExecuteContainerActionAsync(CreateRoutedInstanceOptions(15, EndpointId), CreateRestartRequest(), TestContext.CancellationToken)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unknown, result.Status, "An action request that breaks off may have been applied");
        Assert.AreEqual($"Action 'restart' failed: connection failed{MayHaveBeenAppliedSuffix}",
                        result.Message,
                        "An action request that breaks off must carry the fixed reason and the suffix of E4");
        Assert.HasCount(1,
                        requests.Where(request => request.StartsWith("POST " + ActionPathPrefix, StringComparison.Ordinal)).ToList(),
                        "The action request must be sent exactly once");
        Assert.Contains(entry => entry.EventId.Id == 3310 && ReferenceEquals(entry.Exception, injected),
                        logger.Entries,
                        "The failure must still be logged with the exception");
    }

    /// <summary>
    /// Verify each request of an action runs under its own bound: the request timeout, except a stop or restart request with 300 seconds more
    /// </summary>
    /// <param name="requestKind">Method and path of the request whose bound is checked</param>
    /// <param name="actionType">Requested action type</param>
    /// <param name="configureEndpointId">Whether the Docker instance configures an EndpointId</param>
    /// <param name="expectedBoundSeconds">Expected bound of the request in seconds</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC12")]
    [DataRow("POST /api/auth", ContainerActionType.StartContainer, true, 20)]
    [DataRow("GET /api/system/status", ContainerActionType.StartContainer, true, 20)]
    [DataRow("GET /api/endpoints", ContainerActionType.StopContainer, false, 20)]
    [DataRow("GET /api/endpoints/7", ContainerActionType.StopContainer, true, 20)]
    [DataRow("GET " + ContainerListPath, ContainerActionType.RestartContainer, true, 20)]
    [DataRow("POST " + ActionPathPrefix + "start", ContainerActionType.StartContainer, true, 20)]
    [DataRow("POST " + ActionPathPrefix + "stop", ContainerActionType.StopContainer, true, 320)]
    [DataRow("POST " + ActionPathPrefix + "restart", ContainerActionType.RestartContainer, true, 320)]
    public async Task PortainerClientExecuteContainerActionAsyncBoundsActionRequestByActionTypeAsync(string requestKind,
                                                                                                     ContainerActionType actionType,
                                                                                                     bool configureEndpointId,
                                                                                                     int expectedBoundSeconds)
    {
        var clock = new WaitRecordingTimeProvider();
        var observed = new List<(string Request, int WaitCount, TimeSpan Bound)>();
        using var handler = new DelegateHttpMessageHandler((request, _) => RecordBoundAndAnswerAsync(request, clock, observed));

        var client = new PortainerClient(CreateHttpClientFactory(handler),
                                         new TestLogger<PortainerClient>(),
                                         new LoginRetryAfterRegistry(TimeProvider.System),
                                         clock.TimeProvider);
        var instanceOptions = CreateRoutedInstanceOptions(BoundTestTimeoutSeconds, configureEndpointId ? EndpointId : null);

        var result = await client.ExecuteContainerActionAsync(instanceOptions, CreateActionRequest(actionType), TestContext.CancellationToken)
                                 .ConfigureAwait(false);

        var index = observed.FindIndex(entry => entry.Request == requestKind);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, $"The {actionType} must succeed while no bound fires");
        Assert.IsGreaterThanOrEqualTo(0, index, $"The request '{requestKind}' must be sent");

        var previousWaitCount = index > 0 ? observed[index - 1].WaitCount : 0;

        Assert.AreEqual(TimeSpan.FromSeconds(expectedBoundSeconds),
                        observed[index].Bound,
                        $"The request '{requestKind}' of a {actionType} must be bounded by {expectedBoundSeconds} seconds");
        Assert.IsGreaterThan(previousWaitCount,
                             observed[index].WaitCount,
                             $"The request '{requestKind}' must run under its own bound, started after the previous request");
    }

    /// <summary>
    /// Verify a stop the Docker Engine does not answer within 300 seconds plus the request timeout ends as possibly applied
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC12")]
    public async Task PortainerClientExecuteContainerActionAsyncWhenStopExceedsBoundReportsMayHaveBeenAppliedAsync()
    {
        var (result, requests) = await RunActionWithElapsedBoundAsync("POST " + ActionPathPrefix + "stop", ContainerActionType.StopContainer, BoundTestTimeoutSeconds).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unknown, result.Status, "A stop that exceeds its bound may have been applied");
        Assert.AreEqual($"Action 'stop' failed: timed out after 320 seconds{MayHaveBeenAppliedSuffix}",
                        result.Message,
                        "A stop that exceeds its bound must name the bound and carry the suffix of E4");
        Assert.HasCount(1,
                        requests.Where(request => request.StartsWith("POST " + ActionPathPrefix, StringComparison.Ordinal)).ToList(),
                        "The stop request must be sent exactly once");
    }

    /// <summary>
    /// Verify a login or container lookup that exceeds its bound fails the action with the request timeout and sends no action request
    /// </summary>
    /// <param name="requestKind">Method and path of the request that exceeds its bound</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("POST /api/auth")]
    [DataRow("GET " + ContainerListPath)]
    public async Task PortainerClientExecuteContainerActionAsyncWhenRequestTimesOutReportsTimedOutAsync(string requestKind)
    {
        var (result, requests) = await RunActionWithElapsedBoundAsync(requestKind, ContainerActionType.RestartContainer, 15).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, $"A timed-out '{requestKind}' must fail the action");
        Assert.AreEqual("Action 'restart' failed: timed out after 15 seconds", result.Message, "A timeout must report the fixed timed-out reason with the request timeout");
        Assert.DoesNotContain("POST " + ActionPathPrefix + RestartAction, requests, "No action request may follow a timed-out request");
    }

    /// <summary>
    /// Verify the action request carries an empty body so that the runtime never resends it on its own
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncSendsActionRequestWithEmptyContentAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        try
        {
            AddLookupResponses(handler);
            handler.AddResponse(RestartActionUrl, new HttpResponseMessage(HttpStatusCode.NoContent));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             new TestLogger<PortainerClient>(),
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);

            await client.ExecuteContainerActionAsync(CreateFailureInstanceOptions(), CreateRestartRequest(), CancellationToken.None)
                        .ConfigureAwait(false);

            var observed = handler.Requests.Single(request => request.Method == "POST");

            Assert.IsTrue(observed.HasContent, "The action request must carry a non-null body");
            Assert.AreEqual(0L,
                            observed.ContentLength,
                            "The action request body must be empty so that Content-Length 0 is sent");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a failed login masks the user info of the base URL in the capability message and the logs
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWithUserInfoAndFailedLoginMasksUserInfoAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse("https://admin:S3cret@portainer.example.test:9443/api/auth", new HttpResponseMessage(HttpStatusCode.Unauthorized));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.BaseUrl = "https://admin:S3cret@portainer.example.test:9443";
                                                            options.Username = AdminUsername;
                                                            options.Password = "wrong-password";
                                                            options.EndpointId = EndpointId;
                                                        });

            var result = await client.CheckCapabilityAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            var authFailedEntry = logger.Entries.Single(entry => entry.EventId.Id == 3303);
            var exceptionEntry = logger.Entries.Single(entry => entry.EventId.Id == 3306);

            Assert.IsNotNull(result.Message, "A failed login must produce a capability message");
            Assert.Contains("https://***@portainer.example.test:9443/",
                            result.Message,
                            "The capability message must name the base URL with its user info masked");
            Assert.DoesNotContain("S3cret", result.Message, "The capability message must not contain the password");
            Assert.Contains("https://***@portainer.example.test:9443/",
                            authFailedEntry.Message,
                            "The authentication failure log must name the base URL with its user info masked");
            Assert.DoesNotContain("S3cret", authFailedEntry.Message, "The authentication failure log must not contain the password");
            Assert.IsNotNull(exceptionEntry.Exception, "The capability failure must be logged with its exception");
            Assert.Contains("https://***@portainer.example.test:9443/",
                            exceptionEntry.Exception.Message,
                            "The logged exception must name the base URL with its user info masked");
            Assert.DoesNotContain("S3cret", exceptionEntry.Exception.Message, "The logged exception must not contain the password");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a login answer without JWT masks the user info of the base URL in the capability message and the logs
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWithUserInfoAndMissingJwtMasksUserInfoAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddJsonResponse("https://admin:S3cret@portainer.example.test:9443/api/auth", "{}");

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.BaseUrl = "https://admin:S3cret@portainer.example.test:9443";
                                                            options.Username = AdminUsername;
                                                            options.Password = "wrong-password";
                                                            options.EndpointId = EndpointId;
                                                        });

            var result = await client.CheckCapabilityAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            var authFailedEntry = logger.Entries.Single(entry => entry.EventId.Id == 3303);
            var exceptionEntry = logger.Entries.Single(entry => entry.EventId.Id == 3306);

            Assert.IsNotNull(result.Message, "A login answer without JWT must produce a capability message");
            Assert.Contains("https://***@portainer.example.test:9443/",
                            result.Message,
                            "The capability message must name the base URL with its user info masked");
            Assert.DoesNotContain("S3cret", result.Message, "The capability message must not contain the password");
            Assert.Contains("https://***@portainer.example.test:9443/",
                            authFailedEntry.Message,
                            "The authentication failure log must name the base URL with its user info masked");
            Assert.DoesNotContain("S3cret", authFailedEntry.Message, "The authentication failure log must not contain the password");
            Assert.IsNotNull(exceptionEntry.Exception, "The capability failure must be logged with its exception");
            Assert.Contains("https://***@portainer.example.test:9443/",
                            exceptionEntry.Exception.Message,
                            "The logged exception must name the base URL with its user info masked");
            Assert.DoesNotContain("S3cret", exceptionEntry.Exception.Message, "The logged exception must not contain the password");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify an http Portainer base URL logs the plaintext warning exactly once per Docker instance across calls
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientWithInsecureHttpLogsWarningOncePerInstanceAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var logger = new TestLogger<PortainerClient>();

        try
        {
            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.BaseUrl = InsecureBaseUrl;
                                                            options.AllowInsecureHttp = true;
                                                            options.ApiToken = PatToken;
                                                            options.EndpointId = EndpointId;
                                                        });

            await client.CheckCapabilityAsync(instanceOptions, CancellationToken.None)
                        .ConfigureAwait(false);
            await client.ExecuteContainerActionAsync(instanceOptions, CreateRestartRequest(), CancellationToken.None)
                        .ConfigureAwait(false);

            var warnings = logger.Entries.Where(entry => entry.EventId.Id == InsecureHttpEventId)
                                         .ToList();

            Assert.HasCount(1, warnings, "Two calls for the same Docker instance must log the plaintext HTTP warning exactly once");
            Assert.AreEqual(LogLevel.Warning, warnings[0].LogLevel, "The plaintext HTTP entry must be a warning");
            Assert.Contains(InstanceName,
                            warnings[0].Message,
                            "The plaintext HTTP warning must name the Docker instance");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify two Docker instances with differently spelled http schemes each log their own plaintext warning
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientWithInsecureHttpForTwoInstancesLogsOneWarningPerInstanceAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var logger = new TestLogger<PortainerClient>();

        try
        {
            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var lowerCaseInstance = CreateInstanceOptions("LowerCaseInstance",
                                                          options =>
                                                          {
                                                              options.BaseUrl = InsecureBaseUrl;
                                                              options.AllowInsecureHttp = true;
                                                              options.ApiToken = PatToken;
                                                              options.EndpointId = EndpointId;
                                                          });
            var upperCaseInstance = CreateInstanceOptions("UpperCaseInstance",
                                                          options =>
                                                          {
                                                              options.BaseUrl = "HTTP://portainer.example.test";
                                                              options.AllowInsecureHttp = true;
                                                              options.ApiToken = PatToken;
                                                              options.EndpointId = EndpointId;
                                                          });

            await client.CheckCapabilityAsync(lowerCaseInstance, CancellationToken.None)
                        .ConfigureAwait(false);
            await client.CheckCapabilityAsync(upperCaseInstance, CancellationToken.None)
                        .ConfigureAwait(false);
            await client.CheckCapabilityAsync(lowerCaseInstance, CancellationToken.None)
                        .ConfigureAwait(false);

            var warnings = logger.Entries.Where(entry => entry.EventId.Id == InsecureHttpEventId)
                                         .ToList();

            Assert.HasCount(2, warnings, "Two Docker instances must log one plaintext HTTP warning each");
            Assert.ContainsSingle(entry => entry.LogLevel == LogLevel.Warning && entry.Message.Contains("LowerCaseInstance", StringComparison.Ordinal),
                                  warnings,
                                  "The lower case http instance must log one warning naming itself");
            Assert.ContainsSingle(entry => entry.LogLevel == LogLevel.Warning && entry.Message.Contains("UpperCaseInstance", StringComparison.Ordinal),
                                  warnings,
                                  "The upper case HTTP instance must log one warning naming itself");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify an https base URL with AllowInsecureHttp enabled logs no plaintext warning
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWithHttpsAndAllowInsecureHttpDoesNotLogInsecureWarningAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            AddLookupResponses(handler);
            handler.AddResponse(RestartActionUrl, new HttpResponseMessage(HttpStatusCode.NoContent));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.AllowInsecureHttp = true;
                                                            options.ApiToken = PatToken;
                                                            options.EndpointId = EndpointId;
                                                        });

            var result = await client.ExecuteContainerActionAsync(instanceOptions, CreateRestartRequest(), CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "The https action must succeed so that the request path was exercised");
            Assert.DoesNotContain(entry => entry.EventId.Id == InsecureHttpEventId,
                                  logger.Entries,
                                  "An https base URL must not log the plaintext HTTP warning even with AllowInsecureHttp enabled");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify the plaintext warning contains neither the API token, the credentials nor the JWT
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientWithInsecureHttpWarningDoesNotContainSecretsAsync()
    {
        const string secretToken = "secret-api-token-4711";
        const string secretUser = "secret-user-4711";
        const string secretPassword = "secret-password-4711";
        const string secretJwt = "secret-jwt-4711";

        var handler = new SequenceHttpMessageHandler();
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddJsonResponse(InsecureAuthUrl, $$"""{"jwt":"{{secretJwt}}"}""");

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var tokenInstance = CreateInstanceOptions("TokenInstance",
                                                      options =>
                                                      {
                                                          options.BaseUrl = InsecureBaseUrl;
                                                          options.AllowInsecureHttp = true;
                                                          options.ApiToken = secretToken;
                                                          options.EndpointId = EndpointId;
                                                      });
            var loginInstance = CreateInstanceOptions("LoginInstance",
                                                      options =>
                                                      {
                                                          options.BaseUrl = InsecureBaseUrl;
                                                          options.AllowInsecureHttp = true;
                                                          options.Username = secretUser;
                                                          options.Password = secretPassword;
                                                          options.EndpointId = EndpointId;
                                                      });

            await client.CheckCapabilityAsync(tokenInstance, CancellationToken.None)
                        .ConfigureAwait(false);
            await client.CheckCapabilityAsync(loginInstance, CancellationToken.None)
                        .ConfigureAwait(false);

            var warnings = logger.Entries.Where(entry => entry.EventId.Id == InsecureHttpEventId)
                                         .ToList();

            Assert.Contains(request => request.RequestUri == InsecureAuthUrl,
                            handler.Requests,
                            "The login instance must have logged in successfully so that a JWT exists");
            Assert.HasCount(2, warnings, "Each instance must log the plaintext HTTP warning");

            foreach (var warning in warnings)
            {
                Assert.DoesNotContain(secretToken, warning.Message, "The plaintext HTTP warning must not contain the API token");
                Assert.DoesNotContain(secretUser, warning.Message, "The plaintext HTTP warning must not contain the username");
                Assert.DoesNotContain(secretPassword, warning.Message, "The plaintext HTTP warning must not contain the password");
                Assert.DoesNotContain(secretJwt, warning.Message, "The plaintext HTTP warning must not contain the JWT");
            }
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify the plaintext warning is logged before the login request outcome is handled
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWithInsecureHttpAndFailedLoginLogsWarningBeforeAuthFailureAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(InsecureAuthUrl, new HttpResponseMessage(HttpStatusCode.Unauthorized));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.BaseUrl = InsecureBaseUrl;
                                                            options.AllowInsecureHttp = true;
                                                            options.Username = AdminUsername;
                                                            options.Password = "wrong-password";
                                                            options.EndpointId = EndpointId;
                                                        });

            await client.CheckCapabilityAsync(instanceOptions, CancellationToken.None)
                        .ConfigureAwait(false);

            var warningIndex = -1;
            var authFailedIndex = -1;

            for (var index = 0; index < logger.Entries.Count; index++)
            {
                if (warningIndex < 0 && logger.Entries[index].EventId.Id == InsecureHttpEventId)
                {
                    warningIndex = index;
                }

                if (authFailedIndex < 0 && logger.Entries[index].EventId.Id == 3303)
                {
                    authFailedIndex = index;
                }
            }

            Assert.IsGreaterThanOrEqualTo(0, warningIndex, "The plaintext HTTP warning must be logged although the login fails");
            Assert.IsGreaterThanOrEqualTo(0, authFailedIndex, "The failed login must log the authentication failure");
            Assert.IsLessThan(authFailedIndex, warningIndex, "The plaintext HTTP warning must precede the authentication failure entry");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a transport failure before the action request reports the fixed connection-failed reason without the exception text
    /// </summary>
    /// <param name="error">Transport error of the injected exception</param>
    /// <param name="exceptionMessage">Message of the injected exception</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow(HttpRequestError.ConnectionError, "Connection refused (portainer.internal.test:9443)")]
    [DataRow(HttpRequestError.ResponseEnded, "Error while copying content to a stream. portainer.internal.test")]
    public async Task PortainerClientExecuteContainerActionAsyncWhenTransportFailsReportsConnectionFailedAsync(HttpRequestError error, string exceptionMessage)
    {
        var injected = new HttpRequestException(error, exceptionMessage);
        var logger = new TestLogger<PortainerClient>();
        var result = await RunFailingActionAsync(() => injected, logger, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A transport failure before the action request must fail the action");
        Assert.AreEqual("Action 'restart' failed: connection failed", result.Message, "A transport failure must report the fixed connection-failed reason");
        Assert.DoesNotContain(HostMarker, result.Message ?? string.Empty, "The exception text must not reach the action message");
        Assert.Contains(entry => entry.EventId.Id == 3310 && ReferenceEquals(entry.Exception, injected),
                        logger.Entries,
                        "The transport failure must still be logged with the exception");
    }

    /// <summary>
    /// Verify a response that breaks off while it is read before the action request reports the fixed connection-failed reason
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWhenResponseBreaksOffReportsConnectionFailedAsync()
    {
        var injected = new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely. portainer.internal.test");
        var logger = new TestLogger<PortainerClient>();
        var result = await RunFailingActionAsync(() => injected, logger, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A response that breaks off before the action request must fail the action");
        Assert.AreEqual("Action 'restart' failed: connection failed", result.Message, "A response that breaks off must report the fixed connection-failed reason");
        Assert.DoesNotContain(HostMarker, result.Message ?? string.Empty, "The exception text must not reach the action message");
        Assert.Contains(entry => entry.EventId.Id == 3310 && ReferenceEquals(entry.Exception, injected),
                        logger.Entries,
                        "The failure must still be logged with the exception");
    }

    /// <summary>
    /// Verify a transport failure during a capability check reports the fixed connection-failed reason without the exception text
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWhenTransportFailsReportsConnectionFailedAsync()
    {
        var injected = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (portainer.internal.test:9443)");
        var logger = new TestLogger<PortainerClient>();
        var result = await RunFailingCapabilityAsync(() => injected, logger, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A failed capability check must not support actions");
        Assert.AreEqual("Portainer connection error: connection failed", result.Message, "A transport failure must report the fixed connection-failed reason");
        Assert.DoesNotContain(HostMarker, result.Message ?? string.Empty, "The exception text must not reach the capability message");
        Assert.Contains(entry => entry.EventId.Id == 3306 && ReferenceEquals(entry.Exception, injected),
                        logger.Entries,
                        "The transport failure must still be logged with the exception");
    }

    /// <summary>
    /// Verify a login or status request that exceeds its bound during a capability check reports the timed-out reason with the request timeout
    /// </summary>
    /// <param name="requestKind">Method and path of the request that exceeds its bound</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("POST /api/auth")]
    [DataRow("GET /api/system/status")]
    public async Task PortainerClientCheckCapabilityAsyncWhenRequestTimesOutReportsTimedOutAsync(string requestKind)
    {
        var clock = new WaitRecordingTimeProvider();
        var requests = new List<string>();
        using var handler = new DelegateHttpMessageHandler((request, token) => AnswerOrElapseAsync(request, requestKind, clock, requests, token));

        var client = new PortainerClient(CreateHttpClientFactory(handler),
                                         new TestLogger<PortainerClient>(),
                                         new LoginRetryAfterRegistry(TimeProvider.System),
                                         clock.TimeProvider);

        var result = await client.CheckCapabilityAsync(CreateRoutedInstanceOptions(15, EndpointId), TestContext.CancellationToken)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, $"A timed-out '{requestKind}' must fail the capability check");
        Assert.AreEqual("Portainer connection error: timed out after 15 seconds", result.Message, "A timeout must report the fixed timed-out reason with the request timeout");
    }

    /// <summary>
    /// Verify a cancelled caller token during an action is reported as cancelled without throwing
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWhenCallerCancelsReportsCancelledAsync()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

        await cts.CancelAsync().ConfigureAwait(false);

        var token = cts.Token;
        var result = await RunFailingActionAsync(() => new TaskCanceledException("A task was canceled.", null, token), new TestLogger<PortainerClient>(), token).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A cancelled action must fail the result");
        Assert.AreEqual("Action 'restart' failed: cancelled", result.Message, "A cancelled caller token must report the fixed cancelled reason");
    }

    /// <summary>
    /// Verify a cancelled caller token during a capability check is reported as cancelled without throwing
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWhenCallerCancelsReportsCancelledAsync()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

        await cts.CancelAsync().ConfigureAwait(false);

        var token = cts.Token;
        var result = await RunFailingCapabilityAsync(() => new TaskCanceledException("A task was canceled.", null, token), new TestLogger<PortainerClient>(), token).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A cancelled capability check must not support actions");
        Assert.AreEqual("Portainer connection error: cancelled", result.Message, "A cancelled caller token must report the fixed cancelled reason");
    }

    /// <summary>
    /// Verify a container list that is not valid JSON reports the invalid-response reason without the parser text
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWhenResponseIsNotJsonReportsInvalidResponseAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(StatusUrl, new HttpResponseMessage(HttpStatusCode.OK));
            handler.AddJsonResponse(EndpointCheckUrl, EndpointJson);
            handler.AddJsonResponse(ContainersUrl, "<html>nope</html>");

            var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var result = await client.ExecuteContainerActionAsync(CreateFailureInstanceOptions(), CreateRestartRequest(), CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "An answer that is not JSON must fail the action");
            Assert.AreEqual("Action 'restart' failed: invalid response", result.Message, "An answer that is not JSON must report the fixed invalid-response reason");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify an unexpected exception before the action request reports the fixed unexpected-error reason without the exception text
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWhenUnexpectedExceptionReportsUnexpectedErrorAsync()
    {
        var result = await RunFailingActionAsync(CreateUnexpectedException, new TestLogger<PortainerClient>(), CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "An unexpected exception must fail the action");
        Assert.AreEqual("Action 'restart' failed: unexpected error", result.Message, "An unexpected exception must report the fixed unexpected-error reason");
        Assert.DoesNotContain(HostMarker, result.Message ?? string.Empty, "The exception text must not reach the action message");
    }

    /// <summary>
    /// Verify an unexpected exception during a capability check reports the fixed unexpected-error reason without the exception text
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWhenUnexpectedExceptionReportsUnexpectedErrorAsync()
    {
        var result = await RunFailingCapabilityAsync(CreateUnexpectedException, new TestLogger<PortainerClient>(), CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "An unexpected exception must not support actions");
        Assert.AreEqual("Portainer connection error: unexpected error", result.Message, "An unexpected exception must report the fixed unexpected-error reason");
        Assert.DoesNotContain(HostMarker, result.Message ?? string.Empty, "The exception text must not reach the capability message");
    }

    /// <summary>
    /// Verify a capability check without an endpoint identifier refuses to pick one of several environments and lists them by identifier
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWithMultipleEnvironmentsAndNoEndpointIdReportsAmbiguousAsync()
    {
        var logger = new TestLogger<PortainerClient>();
        var listResponse = CreateEnvironmentListResponse("""[{"Id":5,"Name":"edge"},{"Id":1,"Name":"local"},{"Id":2,"Name":"nas"}]""");

        var (result, _) = await RunCapabilityWithoutEndpointIdAsync(listResponse, logger).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, "An ambiguous environment list must not support actions");
        Assert.AreEqual("Portainer manages 3 environments; set Portainer:EndpointId of Docker instance 'Production' to one of: 1 (local), 2 (nas), 5 (edge)",
                        result.Message,
                        "The message must name the key relative to the instance and list the environments by ascending identifier");
        Assert.Contains(entry => entry.EventId.Id == AmbiguousEventId
                                 && entry.LogLevel == LogLevel.Warning
                                 && entry.Message.Contains(InstanceName, StringComparison.Ordinal)
                                 && entry.Message.Contains("lists 3 environments", StringComparison.Ordinal)
                                 && entry.Message.Contains("edge", StringComparison.Ordinal) == false
                                 && entry.Message.Contains("local", StringComparison.Ordinal) == false
                                 && entry.Message.Contains("nas", StringComparison.Ordinal) == false,
                        logger.Entries,
                        "The ambiguity warning must name the instance and the listed count but no environment name");
        Assert.DoesNotContain(entry => entry.EventId.Id == 3304,
                              logger.Entries,
                              "No environment may be reported as resolved when none is chosen");
    }

    /// <summary>
    /// Verify an action without an endpoint identifier fails before any container lookup when Portainer lists several environments
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWithMultipleEnvironmentsAndNoEndpointIdFailsWithoutActionAsync()
    {
        var logger = new TestLogger<PortainerClient>();
        var listResponse = CreateEnvironmentListResponse("""[{"Id":2,"Name":"nas"},{"Id":1,"Name":"local"}]""");

        var (result, requests) = await RunActionWithoutEndpointIdAsync(listResponse, logger).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, "An action must not run when several environments are listed and none is configured");
        Assert.AreEqual("Portainer manages 2 environments; set Portainer:EndpointId of Docker instance 'Production' to one of: 1 (local), 2 (nas)",
                        result.Message,
                        "The action message must use the same format as the capability message");
        Assert.Contains(request => request.RequestUri == EndpointsUrl && request.Method == "GET",
                        requests,
                        "The environment list must be read");
        Assert.DoesNotContain(request => request.RequestUri != StatusUrl && request.RequestUri != EndpointsUrl,
                              requests,
                              "Only the status and the environment list may be requested, no container lookup and no action");
    }

    /// <summary>
    /// Verify an action without an endpoint identifier runs against the only listed environment
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWithSingleEnvironmentAndNoEndpointIdUsesItAsync()
    {
        var logger = new TestLogger<PortainerClient>();
        var listResponse = CreateEnvironmentListResponse("""[{"Id":7,"Name":"local"}]""");

        var (result, requests) = await RunActionWithoutEndpointIdAsync(listResponse, logger).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "An action must run when exactly one environment is listed");
        Assert.Contains(request => request.RequestUri == RestartActionUrl && request.Method == "POST",
                        requests,
                        "The action must be sent to the only listed environment");
    }

    /// <summary>
    /// Verify a null environment list body is reported as no endpoint found
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWhenEnvironmentListIsNullReturnsNoEndpointFoundAsync()
    {
        var listResponse = CreateEnvironmentListResponse("null");

        var (result, _) = await RunCapabilityWithoutEndpointIdAsync(listResponse, new TestLogger<PortainerClient>()).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, "A null environment list must not support actions");
        Assert.AreEqual("No Portainer endpoint found",
                        result.Message,
                        "A null environment list must be reported like an empty one");
    }

    /// <summary>
    /// Verify a failing environment list request is reported with its status code and logged
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWhenEnvironmentListFailsReportsStatusCodeAsync()
    {
        var logger = new TestLogger<PortainerClient>();
        var listResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var (result, _) = await RunCapabilityWithoutEndpointIdAsync(listResponse, logger).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, "A failing environment list must not support actions");
        Assert.AreEqual("Portainer environment list failed: HTTP 500",
                        result.Message,
                        "The message must carry the status code of the environment list");
        Assert.Contains(entry => entry.EventId.Id == ListFailedEventId
                                 && entry.LogLevel == LogLevel.Warning
                                 && entry.Message.Contains(InstanceName, StringComparison.Ordinal)
                                 && entry.Message.Contains("500", StringComparison.Ordinal),
                        logger.Entries,
                        "A failing environment list must log a warning naming the instance and the status code");
    }

    /// <summary>
    /// Verify an action fails with the status code of a failing environment list before any container lookup
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWhenEnvironmentListFailsReportsStatusCodeWithoutActionAsync()
    {
        var logger = new TestLogger<PortainerClient>();
        var listResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var (result, requests) = await RunActionWithoutEndpointIdAsync(listResponse, logger).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, "An action must not run when the environment list fails");
        Assert.AreEqual("Portainer environment list failed: HTTP 500",
                        result.Message,
                        "The action message must carry the status code of the environment list");
        Assert.DoesNotContain(request => request.RequestUri != StatusUrl && request.RequestUri != EndpointsUrl,
                              requests,
                              "No container lookup or action may follow a failed environment list");
        Assert.Contains(entry => entry.EventId.Id == ListFailedEventId
                                 && entry.LogLevel == LogLevel.Warning
                                 && entry.Message.Contains(InstanceName, StringComparison.Ordinal)
                                 && entry.Message.Contains("500", StringComparison.Ordinal),
                        logger.Entries,
                        "A failing environment list must log a warning naming the instance and the status code");
    }

    /// <summary>
    /// Verify a capability check with a configured endpoint identifier checks that environment and never requests the environment list
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWithEndpointIdDoesNotRequestEnvironmentListAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(StatusUrl, new HttpResponseMessage(HttpStatusCode.OK));
            handler.AddResponse(EndpointsUrl, CreateEnvironmentListResponse("""[{"Id":1,"Name":"local"},{"Id":2,"Name":"nas"}]"""));
            handler.AddJsonResponse(EndpointCheckUrl, EndpointJson);

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);

            var result = await client.CheckCapabilityAsync(CreateFailureInstanceOptions(), TestContext.CancellationToken)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "A configured endpoint identifier Portainer knows must support actions");
            Assert.IsNotNull(result.Data, "A succeeded capability check must carry its data");
            Assert.AreEqual(EndpointId, result.Data.EndpointId, "The configured endpoint identifier must be reported");
            Assert.Contains(request => request.RequestUri == EndpointCheckUrl && request.Method == "GET",
                            handler.Requests,
                            "A configured endpoint identifier must be checked against Portainer");
            Assert.DoesNotContain(request => request.RequestUri == EndpointsUrl,
                                  handler.Requests,
                                  "A configured endpoint identifier must not trigger the environment list request");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify an action with a configured endpoint identifier never requests the environment list
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWithEndpointIdDoesNotRequestEnvironmentListAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(EndpointsUrl, CreateEnvironmentListResponse("""[{"Id":1,"Name":"local"},{"Id":2,"Name":"nas"}]"""));
            AddLookupResponses(handler);
            handler.AddResponse(RestartActionUrl, new HttpResponseMessage(HttpStatusCode.NoContent));

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);

            var result = await client.ExecuteContainerActionAsync(CreateFailureInstanceOptions(), CreateRestartRequest(), TestContext.CancellationToken)
                                     .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Succeeded, result.Status, "An action with a configured endpoint identifier must run");
            Assert.DoesNotContain(request => request.RequestUri == EndpointsUrl,
                                  handler.Requests,
                                  "A configured endpoint identifier must not trigger the environment list request");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify at most ten environments are listed, by ascending identifier, followed by an ellipsis
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWithMoreThanTenEnvironmentsListsFirstTenAsync()
    {
        var listResponse = CreateEnvironmentListResponse("""
                                                         [{"Id":12,"Name":"e12"},{"Id":3,"Name":"e3"},{"Id":11,"Name":"e11"},{"Id":1,"Name":"e1"},
                                                          {"Id":9,"Name":"e9"},{"Id":2,"Name":"e2"},{"Id":10,"Name":"e10"},{"Id":4,"Name":"e4"},
                                                          {"Id":8,"Name":"e8"},{"Id":5,"Name":"e5"},{"Id":7,"Name":"e7"},{"Id":6,"Name":"e6"}]
                                                         """);

        var (result, _) = await RunCapabilityWithoutEndpointIdAsync(listResponse, new TestLogger<PortainerClient>()).ConfigureAwait(false);

        Assert.AreEqual("Portainer manages 12 environments; set Portainer:EndpointId of Docker instance 'Production' to one of: 1 (e1), 2 (e2), 3 (e3), 4 (e4), 5 (e5), 6 (e6), 7 (e7), 8 (e8), 9 (e9), 10 (e10), …",
                        result.Message,
                        "Only the first ten environments by ascending identifier must be listed, followed by an ellipsis");
    }

    /// <summary>
    /// Verify a reported total above the listed environments makes the list ambiguous even when one environment is listed
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWithTotalCountAboveListedEnvironmentsReportsAmbiguousAsync()
    {
        var logger = new TestLogger<PortainerClient>();
        var listResponse = CreateEnvironmentListResponse("""[{"Id":7,"Name":"local"}]""", "3");

        var (result, _) = await RunCapabilityWithoutEndpointIdAsync(listResponse, logger).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, "A truncated list must not make its only environment usable");
        Assert.AreEqual("Portainer manages 3 environments; set Portainer:EndpointId of Docker instance 'Production' to one of: 7 (local), …",
                        result.Message,
                        "The message must use the reported total and mark the list as incomplete");
        Assert.Contains(entry => entry.EventId.Id == AmbiguousEventId
                                 && entry.Message.Contains("lists 1 environments", StringComparison.Ordinal)
                                 && entry.Message.Contains("reported total 3", StringComparison.Ordinal),
                        logger.Entries,
                        "The ambiguity warning must carry the listed count and the reported total");
    }

    /// <summary>
    /// Verify an empty list with a reported total fails the action with a message that lists no environment
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteContainerActionAsyncWithEmptyListAndTotalCountReportsAmbiguousWithoutActionAsync()
    {
        var listResponse = CreateEnvironmentListResponse("[]", "3");

        var (result, requests) = await RunActionWithoutEndpointIdAsync(listResponse, new TestLogger<PortainerClient>()).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, "An action must not run when Portainer reports environments but lists none");
        Assert.AreEqual("Portainer manages 3 environments; set Portainer:EndpointId of Docker instance 'Production'",
                        result.Message,
                        "The message must end after the instance name when no environment is listed");
        Assert.DoesNotContain(request => request.RequestUri != StatusUrl && request.RequestUri != EndpointsUrl,
                              requests,
                              "No container lookup may follow an ambiguous resolution");
    }

    /// <summary>
    /// Verify a present but invalid total count header never makes a single listed environment usable
    /// </summary>
    /// <param name="firstValue">First header value</param>
    /// <param name="secondValue">Second header value; null when the header carries one value</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("abc", null)]
    [DataRow("-1", null)]
    [DataRow("1", "1")]
    public async Task PortainerClientCheckCapabilityAsyncWithInvalidTotalCountAndSingleEnvironmentReportsUnknownCountAsync(string firstValue, string? secondValue)
    {
        var listResponse = CreateEnvironmentListResponse("""[{"Id":7,"Name":"local"}]""", firstValue, secondValue);

        var (result, _) = await RunCapabilityWithoutEndpointIdAsync(listResponse, new TestLogger<PortainerClient>()).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, "An invalid total count must fail closed");
        Assert.AreEqual("Portainer reported no valid environment count; set Portainer:EndpointId of Docker instance 'Production' to one of: 7 (local), …",
                        result.Message,
                        $"The header value '{firstValue}' / '{secondValue}' must be reported as an unknown count");
    }

    /// <summary>
    /// Verify an empty environment list with an invalid total count header is reported as no endpoint found
    /// </summary>
    /// <param name="headerValue">Total count header value</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("abc")]
    [DataRow("-1")]
    public async Task PortainerClientCheckCapabilityAsyncWithEmptyEnvironmentListAndInvalidTotalCountReturnsNoEndpointFoundAsync(string headerValue)
    {
        var listResponse = CreateEnvironmentListResponse("[]", headerValue);

        var (result, _) = await RunCapabilityWithoutEndpointIdAsync(listResponse, new TestLogger<PortainerClient>()).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, "An empty environment list must not support actions");
        Assert.AreEqual("No Portainer endpoint found",
                        result.Message,
                        $"An empty list with the invalid header '{headerValue}' must not be reported as ambiguous");
    }

    /// <summary>
    /// Verify an action without an endpoint identifier fails with no endpoint found for an empty list with an invalid total count header
    /// </summary>
    /// <param name="headerValue">Total count header value</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("abc")]
    [DataRow("-1")]
    public async Task PortainerClientExecuteContainerActionAsyncWithEmptyEnvironmentListAndInvalidTotalCountFailsWithNoEndpointFoundAsync(string headerValue)
    {
        var listResponse = CreateEnvironmentListResponse("[]", headerValue);

        var (result, requests) = await RunActionWithoutEndpointIdAsync(listResponse, new TestLogger<PortainerClient>()).ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported, result.Status, "An action must not run when no environment is listed");
        Assert.AreEqual("No Portainer endpoint found",
                        result.Message,
                        $"An empty list with the invalid header '{headerValue}' must not be reported as ambiguous");
        Assert.DoesNotContain(request => request.RequestUri != StatusUrl && request.RequestUri != EndpointsUrl,
                              requests,
                              "Only the status and the environment list may be requested");
    }

    /// <summary>
    /// Verify environment names are trimmed, cleaned of control, format and separator characters and shortened
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientCheckCapabilityAsyncWithAmbiguousEnvironmentsSanitizesEnvironmentNamesAsync()
    {
        var longName = new string('n', 100);
        var prefix = new string('p', 63);
        var json = $$"""
                     [{"Id":1,"Name":"a\u202Eb"},{"Id":2,"Name":"x\u2028y"},{"Id":3,"Name":"t\tu"},
                      {"Id":4,"Name":"{{longName}}"},{"Id":5,"Name":"{{prefix}}\uD83D\uDE00"},{"Id":6,"Name":null},
                      {"Id":7,"Name":"  "},{"Id":8,"Name":"  pad  "},{"Id":9,"Name":"q\u2029r"}]
                     """;

        Assert.Contains("""\u202E""", json, "The input must still carry the escape of the bidirectional override");
        Assert.Contains("""\u2028""", json, "The input must still carry the escape of the line separator");
        Assert.Contains("""\u2029""", json, "The input must still carry the escape of the paragraph separator");
        Assert.Contains("""\t""", json, "The input must still carry the escape of the tab");
        Assert.Contains("""\uD83D\uDE00""", json, "The input must still carry the escape of the surrogate pair");

        var (result, _) = await RunCapabilityWithoutEndpointIdAsync(CreateEnvironmentListResponse(json), new TestLogger<PortainerClient>()).ConfigureAwait(false);

        Assert.AreEqual($"Portainer manages 9 environments; set Portainer:EndpointId of Docker instance 'Production' to one of: 1 (a?b), 2 (x?y), 3 (t?u), 4 ({new string('n', 64)}…), 5 ({prefix}…), 6, 7, 8 (pad), 9 (q?r)",
                        result.Message,
                        "Names must be trimmed, cleaned, cut to 64 UTF-16 units without splitting a surrogate pair, and omitted when empty");
    }

    /// <summary>
    /// Verify a login answered 429 with Retry-After defers the next capability check without any request
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task PortainerClientLoginRetryAfterDefersCapabilityCheckAsync()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var handler = new SequenceHttpMessageHandler();

        try
        {
            handler.AddResponse(AuthUrl, CreateTooManyRequests(120));

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             new TestLogger<PortainerClient>(),
                                             new LoginRetryAfterRegistry(CreateClock(() => now)),
                                             TimeProvider.System);
            var instanceOptions = CreateLoginInstanceOptions(InstanceName);

            await client.CheckCapabilityAsync(instanceOptions, CancellationToken.None).ConfigureAwait(false);

            Assert.HasCount(1, handler.Requests, "The first capability check must send exactly the login");

            var result = await client.CheckCapabilityAsync(instanceOptions, CancellationToken.None).ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "A deferred capability check must not support actions");
            Assert.AreEqual($"Portainer connection error: {DeferredMessage("2026-03-10T12:02:00Z")}",
                            result.Message,
                            "The capability message must carry the authentication error with the end of the window");
            Assert.HasCount(1, handler.Requests, "A capability check inside the window must send no request");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a login window opened by one client defers the action of another client and another Docker instance with the same login
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task PortainerClientLoginRetryAfterDefersActionOfOtherInstanceAsync()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var handler = new SequenceHttpMessageHandler();

        try
        {
            handler.AddResponse(AuthUrl, CreateTooManyRequests(120));

            var registry = new LoginRetryAfterRegistry(CreateClock(() => now));
            var firstClient = new PortainerClient(CreateHttpClientFactory(handler),
                                                  new TestLogger<PortainerClient>(),
                                                  registry,
                                                  TimeProvider.System);
            var secondClient = new PortainerClient(CreateHttpClientFactory(handler),
                                                   new TestLogger<PortainerClient>(),
                                                   registry,
                                                   TimeProvider.System);

            await firstClient.CheckCapabilityAsync(CreateLoginInstanceOptions(InstanceName), CancellationToken.None).ConfigureAwait(false);

            var result = await secondClient.ExecuteContainerActionAsync(CreateLoginInstanceOptions("Staging"), CreateRestartRequest(), CancellationToken.None)
                                           .ConfigureAwait(false);

            Assert.AreEqual(ExternalOperationStatus.Failed, result.Status, "An action inside the window must fail");
            Assert.AreEqual($"Action 'restart' failed: {DeferredMessage("2026-03-10T12:02:00Z")}",
                            result.Message,
                            "The action message must carry the authentication error with the end of the window");
            Assert.HasCount(1, handler.Requests, "An action of another Docker instance with the same login must send no request inside the window");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify the next capability check logs in again once the clock reaches the end of the window
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task PortainerClientLoginRetryAfterPassedLogsInAgainAsync()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var handler = new SequenceHttpMessageHandler();

        try
        {
            handler.AddResponse(AuthUrl, CreateTooManyRequests(120));
            handler.AddJsonResponse(AuthUrl, """{"jwt":"jwt-token-xyz"}""");

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             new TestLogger<PortainerClient>(),
                                             new LoginRetryAfterRegistry(CreateClock(() => now)),
                                             TimeProvider.System);
            var instanceOptions = CreateLoginInstanceOptions(InstanceName);

            await client.CheckCapabilityAsync(instanceOptions, CancellationToken.None).ConfigureAwait(false);

            now = now.AddSeconds(119);

            await client.CheckCapabilityAsync(instanceOptions, CancellationToken.None).ConfigureAwait(false);

            Assert.HasCount(1, handler.Requests, "A capability check one second before the end of the window must send nothing");

            now = now.AddSeconds(1);

            await client.CheckCapabilityAsync(instanceOptions, CancellationToken.None).ConfigureAwait(false);

            Assert.AreEqual(2,
                            handler.Requests.Count(request => request.RequestUri == AuthUrl),
                            "The login must be sent again once the window has passed");
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Create a successful environment list response
    /// </summary>
    /// <param name="json">Response body</param>
    /// <param name="totalCountValues">Values of the total count header; none leaves the header out</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateEnvironmentListResponse(string json, params string?[] totalCountValues)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
                       {
                           Content = new StringContent(json, Encoding.UTF8, "application/json"),
                       };
        var values = totalCountValues.OfType<string>().ToArray();

        if (values.Length > 0)
        {
            response.Headers.TryAddWithoutValidation(TotalCountHeaderName, values);
        }

        return response;
    }

    /// <summary>
    /// Create a successful JSON response
    /// </summary>
    /// <param name="json">Response body</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
               {
                   Content = new StringContent(json, Encoding.UTF8, "application/json"),
               };
    }

    /// <summary>
    /// Name a request by its method and path, without the query
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>Method and path</returns>
    private static string GetRequestKind(HttpRequestMessage request)
    {
        return $"{request.Method.Method} {request.RequestUri?.AbsolutePath}";
    }

    /// <summary>
    /// Answer a request of a successful action by its method and path: login, status, environment list and check, container list, and the action
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateRoutedResponse(HttpRequestMessage request)
    {
        return GetRequestKind(request) switch
               {
                   "POST /api/auth" => CreateJsonResponse("""{"jwt":"jwt-token-xyz"}"""),
                   "GET /api/system/status" => new HttpResponseMessage(HttpStatusCode.OK),
                   "GET /api/endpoints" => CreateJsonResponse("""[{"Id":7,"Name":"local"}]"""),
                   "GET /api/endpoints/7" => CreateJsonResponse(EndpointJson),
                   "GET " + ContainerListPath => CreateJsonResponse(ContainerFoundJson),
                   _ => new HttpResponseMessage(HttpStatusCode.NoContent),
               };
    }

    /// <summary>
    /// Record the bound that runs while a request is sent, then answer it as a successful action would be answered
    /// </summary>
    /// <param name="request">Request</param>
    /// <param name="clock">Clock recording the bounds</param>
    /// <param name="observed">Requests with the number of bounds started so far and the latest bound</param>
    /// <returns>Response</returns>
    private static Task<HttpResponseMessage> RecordBoundAndAnswerAsync(HttpRequestMessage request,
                                                                       WaitRecordingTimeProvider clock,
                                                                       List<(string Request, int WaitCount, TimeSpan Bound)> observed)
    {
        var waits = clock.Waits;
        var latestBound = waits.Count > 0 ? waits[^1] : TimeSpan.Zero;

        observed.Add((GetRequestKind(request), waits.Count, latestBound));

        return Task.FromResult(CreateRoutedResponse(request));
    }

    /// <summary>
    /// Answer a request as a successful action would be answered, except the given one, which lets its bound elapse and waits for the cancellation
    /// </summary>
    /// <param name="request">Request</param>
    /// <param name="elapsedRequestKind">Method and path of the request whose bound elapses</param>
    /// <param name="clock">Clock recording the bounds</param>
    /// <param name="requests">Methods and paths of the requests sent so far</param>
    /// <param name="cancellationToken">Token of the request</param>
    /// <returns>Response</returns>
    private static async Task<HttpResponseMessage> AnswerOrElapseAsync(HttpRequestMessage request,
                                                                       string elapsedRequestKind,
                                                                       WaitRecordingTimeProvider clock,
                                                                       List<string> requests,
                                                                       CancellationToken cancellationToken)
    {
        var requestKind = GetRequestKind(request);

        requests.Add(requestKind);

        if (requestKind == elapsedRequestKind)
        {
            clock.ElapseLastWait();

            await Task.Delay(TimeSpan.FromSeconds(SafetyDelaySeconds), cancellationToken).ConfigureAwait(false);
        }

        return CreateRoutedResponse(request);
    }

    /// <summary>
    /// Answer a request as a successful action would be answered, except the given one, which throws the given exception
    /// </summary>
    /// <param name="request">Request</param>
    /// <param name="failingRequestKind">Method and path of the request that throws</param>
    /// <param name="exception">Exception thrown by that request</param>
    /// <param name="requests">Methods and paths of the requests sent so far</param>
    /// <returns>Response</returns>
    private static Task<HttpResponseMessage> AnswerOrThrowAsync(HttpRequestMessage request,
                                                                string failingRequestKind,
                                                                Exception exception,
                                                                List<string> requests)
    {
        var requestKind = GetRequestKind(request);

        requests.Add(requestKind);

        return requestKind == failingRequestKind
                   ? Task.FromException<HttpResponseMessage>(exception)
                   : Task.FromResult(CreateRoutedResponse(request));
    }

    /// <summary>
    /// Create an exception no failure mapping knows, carrying the host marker in its message
    /// </summary>
    /// <returns>Exception</returns>
    private static Exception CreateUnexpectedException()
    {
        return new InvalidOperationException("Raw runtime text for portainer.internal.test");
    }

    /// <summary>
    /// Create the text of an authentication error for a deferred login
    /// </summary>
    /// <param name="until">End of the window as formatted in the message</param>
    /// <returns>Error text</returns>
    private static string DeferredMessage(string until)
    {
        return $"Portainer authentication for '{BaseUrl}' not sent: Portainer answered HTTP 429 and asked to wait until {until}";
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
    /// Create a 429 response asking for a wait in seconds
    /// </summary>
    /// <param name="seconds">Requested wait in seconds</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage CreateTooManyRequests(int seconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));

        return response;
    }

    /// <summary>
    /// Create Docker instance options that log in to Portainer with the same user name and password
    /// </summary>
    /// <param name="name">Docker instance display name</param>
    /// <returns>Docker instance options</returns>
    private static DockerInstanceOptions CreateLoginInstanceOptions(string name)
    {
        return CreateInstanceOptions(name,
                                     options =>
                                     {
                                         options.Username = AdminUsername;
                                         options.Password = "correct-password";
                                         options.EndpointId = EndpointId;
                                     });
    }

    /// <summary>
    /// Create Docker instance options that log in to Portainer, for the tests whose requests are answered by method and path
    /// </summary>
    /// <param name="requestTimeoutSeconds">Request timeout in seconds</param>
    /// <param name="endpointId">Configured EndpointId; null resolves the environment from the list</param>
    /// <returns>Docker instance options</returns>
    private static DockerInstanceOptions CreateRoutedInstanceOptions(int requestTimeoutSeconds, string? endpointId)
    {
        return CreateInstanceOptions(InstanceName,
                                     options =>
                                     {
                                         options.Username = AdminUsername;
                                         options.Password = "correct-password";
                                         options.EndpointId = endpointId;
                                         options.RequestTimeoutSeconds = requestTimeoutSeconds;
                                     });
    }

    /// <summary>
    /// Create Docker instance options using an API token and an endpoint identifier, so the first request is the status request
    /// </summary>
    /// <returns>Docker instance options</returns>
    private static DockerInstanceOptions CreateFailureInstanceOptions()
    {
        return CreateInstanceOptions(InstanceName,
                                     options =>
                                     {
                                         options.ApiToken = PatToken;
                                         options.EndpointId = EndpointId;
                                     });
    }

    /// <summary>
    /// Create an action request of the given type for the test container
    /// </summary>
    /// <param name="actionType">Action type</param>
    /// <returns>Action request</returns>
    private static PortainerContainerActionRequest CreateActionRequest(ContainerActionType actionType)
    {
        return new PortainerContainerActionRequest
               {
                   ActionType = actionType,
                   ContainerId = ContainerId,
                   ContainerName = ContainerName,
               };
    }

    /// <summary>
    /// Create the restart action request used by most tests
    /// </summary>
    /// <returns>Action request</returns>
    private static PortainerContainerActionRequest CreateRestartRequest()
    {
        return CreateActionRequest(ContainerActionType.RestartContainer);
    }

    /// <summary>
    /// Answer the status request, the check of environment 7 and the container list of the test container
    /// </summary>
    /// <param name="handler">Handler that answers the requests</param>
    private static void AddLookupResponses(SequenceHttpMessageHandler handler)
    {
        handler.AddResponse(StatusUrl, new HttpResponseMessage(HttpStatusCode.OK));
        handler.AddJsonResponse(EndpointCheckUrl, EndpointJson);
        handler.AddJsonResponse(ContainersUrl, ContainerFoundJson);
    }

    /// <summary>
    /// Execute an action whose first request throws the exception of the factory
    /// </summary>
    /// <param name="failureFactory">Factory of the exception thrown by the first request</param>
    /// <param name="logger">Logger that captures the entries</param>
    /// <param name="cancellationToken">Token passed to the client</param>
    /// <returns>Action outcome</returns>
    private static async Task<ExternalOperationResult<PortainerActionOutcomeData>> RunFailingActionAsync(Func<Exception> failureFactory,
                                                                                                         TestLogger<PortainerClient> logger,
                                                                                                         CancellationToken cancellationToken)
    {
        using var handler = new RetryProbeHttpMessageHandler([null], HttpStatusCode.OK)
                            {
                                FailureFactory = failureFactory,
                            };
        using var httpClient = new HttpClient(handler);

        var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                         logger,
                                         new LoginRetryAfterRegistry(TimeProvider.System),
                                         TimeProvider.System);

        return await client.ExecuteContainerActionAsync(CreateFailureInstanceOptions(), CreateRestartRequest(), cancellationToken)
                           .ConfigureAwait(false);
    }

    /// <summary>
    /// Check a capability whose first request throws the exception of the factory
    /// </summary>
    /// <param name="failureFactory">Factory of the exception thrown by the first request</param>
    /// <param name="logger">Logger that captures the entries</param>
    /// <param name="cancellationToken">Token passed to the client</param>
    /// <returns>Capability outcome</returns>
    private static async Task<ExternalOperationResult<PortainerCapabilityData>> RunFailingCapabilityAsync(Func<Exception> failureFactory,
                                                                                                          TestLogger<PortainerClient> logger,
                                                                                                          CancellationToken cancellationToken)
    {
        using var handler = new RetryProbeHttpMessageHandler([null], HttpStatusCode.OK)
                            {
                                FailureFactory = failureFactory,
                            };
        using var httpClient = new HttpClient(handler);

        var client = new PortainerClient(CreateHttpClientFactory(httpClient),
                                         logger,
                                         new LoginRetryAfterRegistry(TimeProvider.System),
                                         TimeProvider.System);

        return await client.CheckCapabilityAsync(CreateFailureInstanceOptions(), cancellationToken)
                           .ConfigureAwait(false);
    }

    /// <summary>
    /// Create Docker instance options configured for Portainer integration tests
    /// </summary>
    /// <param name="name">Docker instance display name</param>
    /// <param name="configurePortainer">Callback to further configure the Portainer options</param>
    /// <returns>Docker instance options</returns>
    private static DockerInstanceOptions CreateInstanceOptions(string name, Action<PortainerOptions> configurePortainer)
    {
        var portainerOptions = new PortainerOptions
                               {
                                   Enabled = true,
                                   BaseUrl = BaseUrl,
                               };

        configurePortainer(portainerOptions);

        return new DockerInstanceOptions
               {
                   Name = name,
                   BaseUrl = "https://docker.example.test",
                   Enabled = true,
                   Portainer = portainerOptions,
               };
    }

    /// <summary>
    /// Create an <see cref="IHttpClientFactory"/> stub returning a new HTTP client on the shared handler for every call,
    /// because the Portainer client disposes the client it receives
    /// </summary>
    /// <param name="handler">Shared message handler</param>
    /// <returns>HTTP client factory stub</returns>
    private static IHttpClientFactory CreateHttpClientFactory(HttpMessageHandler handler)
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();

        httpClientFactory.CreateClient(PortainerClient.HttpClientName).Returns(_ => new HttpClient(handler, false));

        return httpClientFactory;
    }

    /// <summary>
    /// Create an <see cref="IHttpClientFactory"/> stub returning the given HTTP client for the Portainer named client
    /// </summary>
    /// <param name="httpClient">HTTP client to return</param>
    /// <returns>HTTP client factory stub</returns>
    private static IHttpClientFactory CreateHttpClientFactory(HttpClient httpClient)
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();

        httpClientFactory.CreateClient(PortainerClient.HttpClientName).Returns(httpClient);

        return httpClientFactory;
    }

    /// <summary>
    /// Run an action whose requests are answered by method and path, letting the bound of the given request elapse
    /// </summary>
    /// <param name="elapsedRequestKind">Method and path of the request whose bound elapses</param>
    /// <param name="actionType">Requested action type</param>
    /// <param name="requestTimeoutSeconds">Request timeout in seconds</param>
    /// <returns>Action outcome and the methods and paths of the requests sent</returns>
    private async Task<(ExternalOperationResult<PortainerActionOutcomeData> Result, IReadOnlyList<string> Requests)> RunActionWithElapsedBoundAsync(string elapsedRequestKind,
                                                                                                                                                    ContainerActionType actionType,
                                                                                                                                                    int requestTimeoutSeconds)
    {
        var clock = new WaitRecordingTimeProvider();
        var requests = new List<string>();
        using var handler = new DelegateHttpMessageHandler((request, token) => AnswerOrElapseAsync(request, elapsedRequestKind, clock, requests, token));

        var client = new PortainerClient(CreateHttpClientFactory(handler),
                                         new TestLogger<PortainerClient>(),
                                         new LoginRetryAfterRegistry(TimeProvider.System),
                                         clock.TimeProvider);

        var result = await client.ExecuteContainerActionAsync(CreateRoutedInstanceOptions(requestTimeoutSeconds, EndpointId), CreateActionRequest(actionType), TestContext.CancellationToken)
                                 .ConfigureAwait(false);

        return (result, requests);
    }

    /// <summary>
    /// Run a capability check of an instance without an endpoint identifier against the given environment list response
    /// </summary>
    /// <param name="listResponse">Response of the environment list request</param>
    /// <param name="logger">Logger collecting the entries</param>
    /// <returns>Capability outcome and the observed requests</returns>
    private async Task<(ExternalOperationResult<PortainerCapabilityData> Result, IReadOnlyList<ObservedRequest> Requests)> RunCapabilityWithoutEndpointIdAsync(HttpResponseMessage listResponse, TestLogger<PortainerClient> logger)
    {
        var handler = new SequenceHttpMessageHandler();

        try
        {
            handler.AddResponse(StatusUrl, new HttpResponseMessage(HttpStatusCode.OK));
            handler.AddResponse(EndpointsUrl, listResponse);
            handler.AddJsonResponse(EndpointCheckUrl, EndpointJson);

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName, options => options.ApiToken = PatToken);

            var result = await client.CheckCapabilityAsync(instanceOptions, TestContext.CancellationToken)
                                     .ConfigureAwait(false);

            return (result, handler.Requests);
        }
        finally
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Run a restart action of an instance without an endpoint identifier against the given environment list response
    /// </summary>
    /// <param name="listResponse">Response of the environment list request</param>
    /// <param name="logger">Logger collecting the entries</param>
    /// <returns>Action outcome and the observed requests</returns>
    private async Task<(ExternalOperationResult<PortainerActionOutcomeData> Result, IReadOnlyList<ObservedRequest> Requests)> RunActionWithoutEndpointIdAsync(HttpResponseMessage listResponse, TestLogger<PortainerClient> logger)
    {
        var handler = new SequenceHttpMessageHandler();

        try
        {
            handler.AddResponse(EndpointsUrl, listResponse);
            AddLookupResponses(handler);
            handler.AddResponse(RestartActionUrl, new HttpResponseMessage(HttpStatusCode.NoContent));

            var client = new PortainerClient(CreateHttpClientFactory(handler),
                                             logger,
                                             new LoginRetryAfterRegistry(TimeProvider.System),
                                             TimeProvider.System);
            var instanceOptions = CreateInstanceOptions(InstanceName, options => options.ApiToken = PatToken);

            var result = await client.ExecuteContainerActionAsync(instanceOptions, CreateRestartRequest(), TestContext.CancellationToken)
                                     .ConfigureAwait(false);

            return (result, handler.Requests);
        }
        finally
        {
            handler.Dispose();
        }
    }

    #endregion // Methods
}