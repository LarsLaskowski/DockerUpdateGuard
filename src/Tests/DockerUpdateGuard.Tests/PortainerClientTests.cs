using System.Net;

using DockerUpdateGuard.Configuration;
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
    /// Portainer authentication endpoint
    /// </summary>
    private const string AuthUrl = "https://portainer.example.test/api/auth";

    /// <summary>
    /// Portainer container listing endpoint filtered by container name "my-container" within endpoint 7
    /// </summary>
    private const string ContainersUrl = "https://portainer.example.test/api/endpoints/7/docker/containers/json?all=true&filters=%7B%22name%22%3A%5B%22my-container%22%5D%7D";

    /// <summary>
    /// Portainer container restart action endpoint for endpoint 7 and container "container-abc"
    /// </summary>
    private const string RestartActionUrl = "https://portainer.example.test/api/endpoints/7/docker/containers/container-abc/restart";

    /// <summary>
    /// Portainer container listing response containing a single matching container
    /// </summary>
    private const string ContainerFoundJson = """[{"Id":"container-abc","Names":["/my-container"]}]""";

    /// <summary>
    /// Docker instance display name used by the tests
    /// </summary>
    private const string InstanceName = "Production";

    /// <summary>
    /// Container name used as the Portainer action resource
    /// </summary>
    private const string ContainerName = "my-container";

    /// <summary>
    /// Allow-listed Portainer action name used by the tests
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

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify capability lookups for disabled Portainer integrations return a not-configured result without contacting Portainer
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientGetCapabilityAsyncWhenPortainerDisabledReturnsNotConfiguredResultAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName, options => options.Enabled = false);

            var result = await client.GetCapabilityAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsFalse(result.IsConfigured, "A disabled Portainer integration must be reported as not configured");
            Assert.IsFalse(result.SupportsActions, "A disabled Portainer integration must not support actions");
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
    /// Verify capability lookups without a configured base URL return a not-configured result without contacting Portainer
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientGetCapabilityAsyncWhenBaseUrlMissingReturnsNotConfiguredResultAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.Enabled = true;
                                                            options.BaseUrl = null;
                                                        });

            var result = await client.GetCapabilityAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsFalse(result.IsConfigured, "A missing Portainer base URL must be reported as not configured");
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
    public async Task PortainerClientGetCapabilityAsyncWhenStatusRequestFailsReturnsUnsupportedResultAndLogsWarningAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(StatusUrl, new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.ApiToken = PatToken;
                                                            options.EndpointId = EndpointId;
                                                        });

            var result = await client.GetCapabilityAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsTrue(result.IsConfigured, "A configured Portainer integration with a failing status check must still be reported as configured");
            Assert.IsFalse(result.SupportsActions, "A failing status check must not support actions");
            Assert.Contains("HTTP 500",
                            result.Message,
                            "A failing status check must surface the returned HTTP status code");
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
    public async Task PortainerClientGetCapabilityAsyncResolvesEndpointAutomaticallyAndLogsResolutionAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(StatusUrl, new HttpResponseMessage(HttpStatusCode.OK));
            handler.AddJsonResponse(EndpointsUrl, """[{"Id":7,"Name":"local"}]""");

            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName, options => options.ApiToken = PatToken);

            var result = await client.GetCapabilityAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsTrue(result.IsConfigured, "A reachable Portainer integration must be reported as configured");
            Assert.IsTrue(result.SupportsActions, "A resolved endpoint must support actions");
            Assert.Contains("endpoint 7",
                            result.Message,
                            "The automatically resolved endpoint identifier must be reported in the capability message");
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
    /// Verify capability lookups report unsupported actions when no Portainer endpoint can be resolved
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientGetCapabilityAsyncWhenNoEndpointFoundReturnsUnsupportedResultAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(StatusUrl, new HttpResponseMessage(HttpStatusCode.OK));
            handler.AddJsonResponse(EndpointsUrl, "[]");

            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName, options => options.ApiToken = PatToken);

            var result = await client.GetCapabilityAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsTrue(result.IsConfigured, "A reachable Portainer integration without endpoints must still be reported as configured");
            Assert.IsFalse(result.SupportsActions, "No resolvable endpoint must not support actions");
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
    /// Verify capability lookups catch authentication exceptions and report an unsupported result
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientGetCapabilityAsyncWhenLoginFailsReturnsUnsupportedResultAndLogsExceptionAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(AuthUrl, new HttpResponseMessage(HttpStatusCode.Unauthorized));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.Username = AdminUsername;
                                                            options.Password = "wrong-password";
                                                            options.EndpointId = EndpointId;
                                                        });

            var result = await client.GetCapabilityAsync(instanceOptions, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsTrue(result.IsConfigured, "A capability check that fails during login must still be reported as configured");
            Assert.IsFalse(result.SupportsActions, "A failed login must not support actions");
            Assert.Contains("Portainer connection error",
                            result.Message,
                            "A failed login during capability checks must surface a connection-error message");
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
    /// Verify action execution for disabled Portainer integrations returns a not-configured result without contacting Portainer
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteActionAsyncWhenPortainerDisabledReturnsNotConfiguredResultAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName, options => options.Enabled = false);
            var actionRequest = new PortainerActionRequest
                                {
                                    ActionName = RestartAction,
                                    ResourceName = ContainerName,
                                };

            var result = await client.ExecuteActionAsync(instanceOptions, actionRequest, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsFalse(result.Succeeded, "Action execution against a disabled Portainer integration must fail");
            Assert.AreEqual("Portainer is not configured",
                            result.Message,
                            "A disabled Portainer integration must produce a clear action-result message");
            Assert.IsEmpty(handler.Requests, "A disabled Portainer integration must not contact Portainer");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify actions outside the allow-list are rejected before Portainer is contacted
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteActionAsyncWithActionOutsideAllowListRejectsWithoutContactingPortainerAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName, options => options.ApiToken = PatToken);
            var actionRequest = new PortainerActionRequest
                                {
                                    ActionName = "remove",
                                    ResourceName = ContainerName,
                                };

            var result = await client.ExecuteActionAsync(instanceOptions, actionRequest, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsFalse(result.Succeeded, "Actions outside the allow-list must be rejected");
            Assert.Contains("is not supported",
                            result.Message,
                            "A rejected action must explain that it is not supported");
            Assert.Contains(RestartAction,
                            result.Message,
                            "A rejected action must list the allowed actions");
            Assert.IsEmpty(handler.Requests, "Actions outside the allow-list must not contact Portainer");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a configured API token is used as the X-API-Key header and skips the username/password login
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteActionAsyncWithApiTokenUsesApiKeyHeaderAndSkipsLoginAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddJsonResponse(ContainersUrl, ContainerFoundJson);
            handler.AddResponse(RestartActionUrl, new HttpResponseMessage(HttpStatusCode.NoContent));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.ApiToken = PatToken;
                                                            options.EndpointId = EndpointId;
                                                        });
            var actionRequest = new PortainerActionRequest
                                {
                                    ActionName = RestartAction,
                                    ResourceName = ContainerName,
                                };

            var result = await client.ExecuteActionAsync(instanceOptions, actionRequest, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsTrue(result.Succeeded, "A configured API token must allow the action to succeed");
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
    public async Task PortainerClientExecuteActionAsyncWithUsernamePasswordLogsInAndUsesBearerTokenAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddJsonResponse(AuthUrl, """{"jwt":"jwt-token-xyz"}""");
            handler.AddJsonResponse(ContainersUrl, ContainerFoundJson);
            handler.AddResponse(RestartActionUrl, new HttpResponseMessage(HttpStatusCode.NoContent));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.Username = AdminUsername;
                                                            options.Password = "correct-password";
                                                            options.EndpointId = EndpointId;
                                                        });
            var actionRequest = new PortainerActionRequest
                                {
                                    ActionName = RestartAction,
                                    ResourceName = ContainerName,
                                };

            var result = await client.ExecuteActionAsync(instanceOptions, actionRequest, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsTrue(result.Succeeded, "A successful username/password login must allow the action to succeed");
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
    public async Task PortainerClientExecuteActionAsyncWhenLoginReturnsNoJwtFailsAndLogsAuthFailedAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddJsonResponse(AuthUrl, "{}");

            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.Username = AdminUsername;
                                                            options.Password = "correct-password";
                                                            options.EndpointId = EndpointId;
                                                        });
            var actionRequest = new PortainerActionRequest
                                {
                                    ActionName = RestartAction,
                                    ResourceName = ContainerName,
                                };

            var result = await client.ExecuteActionAsync(instanceOptions, actionRequest, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsFalse(result.Succeeded, "A login response without a JWT must fail the action");
            Assert.Contains("returned no JWT token",
                            result.Message,
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
    public async Task PortainerClientExecuteActionAsyncWhenLoginFailsFailsAndLogsAuthFailedAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddResponse(AuthUrl, new HttpResponseMessage(HttpStatusCode.Unauthorized));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.Username = AdminUsername;
                                                            options.Password = "wrong-password";
                                                            options.EndpointId = EndpointId;
                                                        });
            var actionRequest = new PortainerActionRequest
                                {
                                    ActionName = RestartAction,
                                    ResourceName = ContainerName,
                                };

            var result = await client.ExecuteActionAsync(instanceOptions, actionRequest, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsFalse(result.Succeeded, "A failing login request must fail the action");
            Assert.Contains("HTTP 401",
                            result.Message,
                            "A failing login request must surface the returned HTTP status code");
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
    /// Verify action execution fails when no Portainer endpoint can be resolved
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteActionAsyncWhenNoEndpointFoundReturnsFailureResultAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddJsonResponse(EndpointsUrl, "[]");

            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName, options => options.ApiToken = PatToken);
            var actionRequest = new PortainerActionRequest
                                {
                                    ActionName = RestartAction,
                                    ResourceName = ContainerName,
                                };

            var result = await client.ExecuteActionAsync(instanceOptions, actionRequest, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsFalse(result.Succeeded, "Action execution must fail when no Portainer endpoint can be resolved");
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
    /// Verify action execution fails and logs a warning when the target container cannot be found
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteActionAsyncWhenContainerNotFoundReturnsFailureAndLogsWarningAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddJsonResponse(ContainersUrl, "[]");

            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.ApiToken = PatToken;
                                                            options.EndpointId = EndpointId;
                                                        });
            var actionRequest = new PortainerActionRequest
                                {
                                    ActionName = RestartAction,
                                    ResourceName = ContainerName,
                                };

            var result = await client.ExecuteActionAsync(instanceOptions, actionRequest, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsFalse(result.Succeeded, "Action execution must fail when the target container cannot be found");
            Assert.Contains("not found",
                            result.Message,
                            "A missing container must produce a clear action-result message");
            Assert.Contains(entry => entry.EventId.Id == 3307
                                     && entry.LogLevel == LogLevel.Warning
                                     && entry.Message.Contains(ContainerName, StringComparison.Ordinal)
                                     && entry.Message.Contains(InstanceName, StringComparison.Ordinal),
                            logger.Entries,
                            "A missing container must log a warning naming the container and Docker instance");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a failing Portainer action request fails the result and logs a warning
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteActionAsyncWhenActionRequestFailsReturnsFailureAndLogsWarningAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddJsonResponse(ContainersUrl, ContainerFoundJson);
            handler.AddResponse(RestartActionUrl, new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.ApiToken = PatToken;
                                                            options.EndpointId = EndpointId;
                                                        });
            var actionRequest = new PortainerActionRequest
                                {
                                    ActionName = RestartAction,
                                    ResourceName = ContainerName,
                                };

            var result = await client.ExecuteActionAsync(instanceOptions, actionRequest, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsFalse(result.Succeeded, "A failing Portainer action request must fail the result");
            Assert.Contains("HTTP 500",
                            result.Message,
                            "A failing Portainer action request must surface the returned HTTP status code");
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
            handler.Dispose();
        }
    }

    /// <summary>
    /// Verify a successful Portainer action logs the successful execution
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task PortainerClientExecuteActionAsyncWhenActionSucceedsLogsExecutionAsync()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new TestLogger<PortainerClient>();

        try
        {
            handler.AddJsonResponse(ContainersUrl, ContainerFoundJson);
            handler.AddResponse(RestartActionUrl, new HttpResponseMessage(HttpStatusCode.NoContent));

            var client = new PortainerClient(CreateHttpClientFactory(httpClient), logger);
            var instanceOptions = CreateInstanceOptions(InstanceName,
                                                        options =>
                                                        {
                                                            options.ApiToken = PatToken;
                                                            options.EndpointId = EndpointId;
                                                        });
            var actionRequest = new PortainerActionRequest
                                {
                                    ActionName = RestartAction,
                                    ResourceName = ContainerName,
                                };

            var result = await client.ExecuteActionAsync(instanceOptions, actionRequest, CancellationToken.None)
                                     .ConfigureAwait(false);

            Assert.IsTrue(result.Succeeded, "A successful Portainer action must report success");
            Assert.Contains(entry => entry.EventId.Id == 3308
                                     && entry.LogLevel == LogLevel.Information
                                     && entry.Message.Contains(RestartAction, StringComparison.Ordinal)
                                     && entry.Message.Contains(ContainerName, StringComparison.Ordinal)
                                     && entry.Message.Contains(InstanceName, StringComparison.Ordinal),
                            logger.Entries,
                            "A successful Portainer action must log the action, resource and Docker instance");
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
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

    #endregion // Methods
}