using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Portainer.Data;
using DockerUpdateGuard.Portainer.Interfaces;

namespace DockerUpdateGuard.Portainer;

/// <summary>
/// Portainer API adapter
/// </summary>
public partial class PortainerClient : IPortainerClient
{
    #region Constants

    /// <summary>
    /// Named HTTP client used for Portainer API requests
    /// </summary>
    public const string HttpClientName = "Portainer";

    /// <summary>
    /// Maximum number of environments listed in an ambiguity message
    /// </summary>
    private const int MaxListedEnvironments = 10;

    /// <summary>
    /// Maximum length of an environment name in an ambiguity message, in UTF-16 units
    /// </summary>
    private const int MaxEnvironmentNameLength = 64;

    /// <summary>
    /// Name of the response header carrying the total number of environments
    /// </summary>
    private const string TotalCountHeaderName = "X-Total-Count";

    /// <summary>
    /// Additional wait in seconds a stop or restart request is granted for the container's own stop timeout
    /// </summary>
    private const int StopWaitSeconds = 300;

    /// <summary>
    /// Message of a container the Docker Engine does not know by the snapshot's identifier
    /// </summary>
    private const string ContainerChangedMessage = "Container changed since the last scan; run a runtime scan and try again";

    /// <summary>
    /// Suffix of an outcome whose action request may have reached the Docker Engine
    /// </summary>
    private const string MayHaveBeenAppliedSuffix = "; the action may have been applied, check the container before repeating it";

    /// <summary>
    /// Length of a full Docker container identifier
    /// </summary>
    private const int ContainerIdLength = 64;

    #endregion // Constants

    #region Fields

    /// <summary>
    /// HTTP-client factory
    /// </summary>
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Docker instance names for which the plaintext HTTP warning was already logged
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _insecureHttpWarningInstances = new(StringComparer.Ordinal);

    /// <summary>
    /// Logger
    /// </summary>
    private readonly ILogger<PortainerClient> _logger;

    /// <summary>
    /// Registry of the login Retry-After windows
    /// </summary>
    private readonly LoginRetryAfterRegistry _loginRetryAfterRegistry;

    /// <summary>
    /// Time provider
    /// </summary>
    private readonly TimeProvider _timeProvider;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory</param>
    /// <param name="logger">Logger</param>
    /// <param name="loginRetryAfterRegistry">Registry of the login Retry-After windows</param>
    /// <param name="timeProvider">Time provider</param>
    public PortainerClient(IHttpClientFactory httpClientFactory,
                           ILogger<PortainerClient> logger,
                           LoginRetryAfterRegistry loginRetryAfterRegistry,
                           TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _loginRetryAfterRegistry = loginRetryAfterRegistry;
        _timeProvider = timeProvider;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Read the total environment count Portainer reports in the X-Total-Count header
    /// </summary>
    /// <param name="response">Environment list response</param>
    /// <param name="totalCount">Reported count; null when the header is absent or invalid</param>
    /// <returns>True when the header is absent or valid; false when it is present but invalid</returns>
    private static bool TryReadTotalCount(HttpResponseMessage response, out int? totalCount)
    {
        totalCount = null;

        if (response.Headers.TryGetValues(TotalCountHeaderName, out var values) == false)
        {
            return true;
        }

        var valueList = values.ToList();

        if (valueList.Count == 1 && int.TryParse(valueList[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            totalCount = parsed;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Clean an environment name for display in a message
    /// </summary>
    /// <param name="name">Environment name reported by Portainer</param>
    /// <returns>Trimmed, sanitized and shortened name; empty when there is none</returns>
    private static string FormatEnvironmentName(string? name)
    {
        var cleaned = (name ?? string.Empty).Trim();

        if (cleaned.Length == 0)
        {
            return string.Empty;
        }

        var characters = cleaned.ToCharArray();

        for (var index = 0; index < characters.Length; index++)
        {
            var category = char.GetUnicodeCategory(characters[index]);

            if (category is UnicodeCategory.Control
                         or UnicodeCategory.Format
                         or UnicodeCategory.LineSeparator
                         or UnicodeCategory.ParagraphSeparator)
            {
                characters[index] = '?';
            }
        }

        if (characters.Length <= MaxEnvironmentNameLength)
        {
            return new string(characters);
        }

        var keep = char.IsHighSurrogate(characters[MaxEnvironmentNameLength - 1]) ? MaxEnvironmentNameLength - 1 : MaxEnvironmentNameLength;

        return new string(characters, 0, keep) + "…";
    }

    /// <summary>
    /// Build the message for a Docker instance whose environment cannot be chosen without an EndpointId
    /// </summary>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="endpoints">Listed environments</param>
    /// <param name="environmentCount">Number of environments; null when unknown</param>
    /// <returns>Failure message</returns>
    private static string FormatAmbiguousEnvironmentsMessage(string dockerInstanceName, PortainerEndpointItem[] endpoints, int? environmentCount)
    {
        var countClause = environmentCount is null
                              ? "Portainer reported no valid environment count"
                              : $"Portainer manages {environmentCount.Value.ToString(CultureInfo.InvariantCulture)} environments";
        var message = $"{countClause}; set Portainer:EndpointId of Docker instance '{dockerInstanceName}'";
        var shown = endpoints.OrderBy(e => e.Id)
                             .Take(MaxListedEnvironments)
                             .ToList();

        if (shown.Count == 0)
        {
            return message;
        }

        var entries = shown.Select(e =>
                                   {
                                       var name = FormatEnvironmentName(e.Name);

                                       return name.Length == 0
                                                  ? e.Id.ToString(CultureInfo.InvariantCulture)
                                                  : $"{e.Id.ToString(CultureInfo.InvariantCulture)} ({name})";
                                   })
                           .ToList();
        var list = string.Join(", ", entries);

        if (environmentCount is null || environmentCount.Value > shown.Count)
        {
            list += ", …";
        }

        return $"{message} to one of: {list}";
    }

    /// <summary>
    /// Get the Docker action of an action type
    /// </summary>
    /// <param name="actionType">Action type</param>
    /// <returns>Docker action name; null when the type is not executable</returns>
    private static string? GetActionName(ContainerActionType actionType)
    {
        return actionType switch
               {
                   ContainerActionType.StartContainer => "start",
                   ContainerActionType.StopContainer => "stop",
                   ContainerActionType.RestartContainer => "restart",
                   _ => null,
               };
    }

    /// <summary>
    /// Check that a value is a full Docker container identifier, so that it is safe within a URL path
    /// </summary>
    /// <param name="containerId">Container identifier</param>
    /// <returns>True when the value is exactly 64 lowercase hexadecimal characters</returns>
    private static bool IsContainerId(string? containerId)
    {
        return containerId is not null
               && containerId.Length == ContainerIdLength
               && containerId.All(c => (c is >= '0' and <= '9') || (c is >= 'a' and <= 'f'));
    }

    /// <summary>
    /// Check that a value consists of ASCII digits only, so that it is safe within a URL path
    /// </summary>
    /// <param name="value">Value to check</param>
    /// <returns>True when the value is not empty and holds ASCII digits only</returns>
    private static bool IsDigitsOnly(string value)
    {
        return value.Length > 0 && value.All(char.IsAsciiDigit);
    }

    /// <summary>
    /// Find the container with the given identifier within an environment
    /// </summary>
    /// <param name="client">Authenticated HTTP client</param>
    /// <param name="endpointId">Portainer environment identifier, digits only</param>
    /// <param name="containerId">Full container identifier</param>
    /// <param name="cancellationToken">Cancellation token with the bound of the request</param>
    /// <returns>HTTP status code of the list request and whether the list holds a container with exactly that identifier</returns>
    private static async Task<(int StatusCode, bool Found)> FindContainerAsync(HttpClient client,
                                                                               string endpointId,
                                                                               string containerId,
                                                                               CancellationToken cancellationToken)
    {
        var filter = new Dictionary<string, string[]>(StringComparer.Ordinal)
                     {
                         ["id"] = [containerId],
                     };

        var filters = Uri.EscapeDataString(JsonSerializer.Serialize(filter));
        var url = $"api/endpoints/{endpointId}/docker/containers/json?all=true&filters={filters}";

        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode == false)
        {
            return ((int)response.StatusCode, false);
        }

        var containers = await ExternalJson.ReadFromJsonAsync<DockerContainerItem[]>(response.Content, cancellationToken).ConfigureAwait(false);

        return ((int)response.StatusCode, containers?.Any(c => string.Equals(c.Id, containerId, StringComparison.Ordinal)) == true);
    }

    /// <summary>
    /// Send the action request with an empty body and read its status
    /// </summary>
    /// <param name="client">Authenticated HTTP client</param>
    /// <param name="actionUrl">Action path</param>
    /// <param name="cancellationToken">Cancellation token with the bound of the request</param>
    /// <returns>HTTP status code</returns>
    private static async Task<HttpStatusCode> SendActionAsync(HttpClient client, string actionUrl, CancellationToken cancellationToken)
    {
        // An empty body keeps SocketsHttpHandler from resending a body-less POST on a reused connection
        using var actionContent = new ByteArrayContent([]);
        using var actionResponse = await client.PostAsync(actionUrl, actionContent, cancellationToken).ConfigureAwait(false);

        return actionResponse.StatusCode;
    }

    /// <summary>
    /// Check that Portainer knows an environment
    /// </summary>
    /// <param name="client">Authenticated HTTP client</param>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="endpointId">Environment identifier to check</param>
    /// <param name="bounds">Bounds of the requests of the operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Failure message; null when the environment exists</returns>
    private static async Task<string?> CheckEnvironmentAsync(HttpClient client,
                                                             DockerInstanceOptions instanceOptions,
                                                             string endpointId,
                                                             PortainerRequestBounds bounds,
                                                             CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Portainer environment {endpointId} not found; check Portainer:EndpointId of Docker instance '{instanceOptions.Name}'";

        // Only digits reach the URL path, so a configured value cannot address another Portainer resource
        if (IsDigitsOnly(endpointId) == false)
        {
            return notFoundMessage;
        }

        var statusCode = await bounds.RunAsync(bounds.RequestTimeout, token => GetStatusCodeAsync(client, $"api/endpoints/{endpointId}", token), cancellationToken).ConfigureAwait(false);

        if (statusCode == HttpStatusCode.NotFound)
        {
            return notFoundMessage;
        }

        return statusCode is >= HttpStatusCode.OK and < HttpStatusCode.Ambiguous
                   ? null
                   : $"Portainer environment {endpointId} check failed: HTTP {(int)statusCode}";
    }

    /// <summary>
    /// Send a GET request and read its status only
    /// </summary>
    /// <param name="client">Authenticated HTTP client</param>
    /// <param name="url">Request path</param>
    /// <param name="cancellationToken">Cancellation token with the bound of the request</param>
    /// <returns>HTTP status code</returns>
    private static async Task<HttpStatusCode> GetStatusCodeAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

        return response.StatusCode;
    }

    /// <summary>
    /// Create the base address of the Portainer requests: the configured address up to its path, without query and fragment, with the path closed by a slash so that relative request paths resolve below it
    /// </summary>
    /// <param name="baseUrl">Configured Portainer base URL</param>
    /// <returns>Base address</returns>
    private static Uri CreateBaseAddress(string baseUrl)
    {
        var address = new Uri(baseUrl).GetLeftPart(UriPartial.Path);

        if (address.EndsWith('/') == false)
        {
            address += "/";
        }

        return new Uri(address);
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Create the bounds of the requests of an operation on a Docker instance
    /// </summary>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <returns>Request bounds</returns>
    private PortainerRequestBounds CreateBounds(DockerInstanceOptions instanceOptions)
    {
        return new PortainerRequestBounds(_timeProvider, TimeSpan.FromSeconds(instanceOptions.Portainer.RequestTimeoutSeconds));
    }

    /// <summary>
    /// Get the message that explains why Portainer cannot be used for a Docker instance at all
    /// </summary>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <returns>Not configured message; null when Portainer is enabled and has a base URL</returns>
    private string? GetNotConfiguredMessage(DockerInstanceOptions instanceOptions)
    {
        if (instanceOptions.Portainer.Enabled == false)
        {
            _logger.PortainerCapabilityNotConfigured(instanceOptions.Name);

            return "Portainer is not configured for this Docker instance";
        }

        if (string.IsNullOrWhiteSpace(instanceOptions.Portainer.BaseUrl))
        {
            return "Portainer base URL is not configured";
        }

        return null;
    }

    /// <summary>
    /// List the environments of Portainer and pick the only one
    /// </summary>
    /// <param name="client">Authenticated HTTP client</param>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="cancellationToken">Cancellation token with the bound of the request</param>
    /// <returns>Resolution carrying either the environment identifier or a failure message</returns>
    private async Task<PortainerEndpointResolution> ResolveListedEndpointAsync(HttpClient client, DockerInstanceOptions instanceOptions, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("api/endpoints", cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode == false)
        {
            _logger.PortainerEnvironmentListFailed(instanceOptions.Name, (int)response.StatusCode);

            return new PortainerEndpointResolution(null, $"Portainer environment list failed: HTTP {(int)response.StatusCode}");
        }

        var endpoints = await ExternalJson.ReadFromJsonAsync<PortainerEndpointItem[]>(response.Content, cancellationToken).ConfigureAwait(false) ?? [];
        var headerIsValid = TryReadTotalCount(response, out var totalCount);
        int? environmentCount = headerIsValid ? Math.Max(endpoints.Length, totalCount ?? endpoints.Length) : null;

        if (environmentCount > 1 || (environmentCount is null && endpoints.Length > 0))
        {
            _logger.PortainerEnvironmentAmbiguous(instanceOptions.Name, endpoints.Length, headerIsValid ? totalCount : null);

            return new PortainerEndpointResolution(null, FormatAmbiguousEnvironmentsMessage(instanceOptions.Name, endpoints, environmentCount));
        }

        if (endpoints.Length == 1 && environmentCount == 1)
        {
            return new PortainerEndpointResolution(endpoints[0].Id.ToString(CultureInfo.InvariantCulture), null);
        }

        return new PortainerEndpointResolution(null, "No Portainer endpoint found");
    }

    /// <summary>
    /// Resolve the Portainer environment to use for a Docker instance
    /// </summary>
    /// <param name="client">Authenticated HTTP client</param>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="bounds">Bounds of the requests of the operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resolution carrying either the environment identifier or a failure message</returns>
    private async Task<PortainerEndpointResolution> ResolveEndpointAsync(HttpClient client,
                                                                         DockerInstanceOptions instanceOptions,
                                                                         PortainerRequestBounds bounds,
                                                                         CancellationToken cancellationToken)
    {
        var configuredEndpointId = instanceOptions.Portainer.EndpointId;

        if (string.IsNullOrWhiteSpace(configuredEndpointId) == false)
        {
            return new PortainerEndpointResolution(configuredEndpointId, null);
        }

        return await bounds.RunAsync(bounds.RequestTimeout, token => ResolveListedEndpointAsync(client, instanceOptions, token), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Check the connection to Portainer and the environment of a Docker instance
    /// </summary>
    /// <param name="client">Authenticated HTTP client</param>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="bounds">Bounds of the requests of the operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Succeeded with the environment identifier, or unsupported with the reason</returns>
    private async Task<ExternalOperationResult<PortainerCapabilityData>> ProbeEnvironmentAsync(HttpClient client,
                                                                                               DockerInstanceOptions instanceOptions,
                                                                                               PortainerRequestBounds bounds,
                                                                                               CancellationToken cancellationToken)
    {
        var statusCode = await bounds.RunAsync(bounds.RequestTimeout, token => GetStatusCodeAsync(client, "api/system/status", token), cancellationToken).ConfigureAwait(false);

        if (statusCode is < HttpStatusCode.OK or >= HttpStatusCode.Ambiguous)
        {
            _logger.PortainerCapabilityConnectFailed(instanceOptions.Name, (int)statusCode);

            return ExternalOperationResult.Unsupported<PortainerCapabilityData>($"Portainer connection failed: HTTP {(int)statusCode}");
        }

        var resolution = await ResolveEndpointAsync(client, instanceOptions, bounds, cancellationToken).ConfigureAwait(false);
        var endpointId = resolution.EndpointId;

        if (string.IsNullOrWhiteSpace(endpointId))
        {
            return ExternalOperationResult.Unsupported<PortainerCapabilityData>(resolution.FailureMessage ?? "No Portainer endpoint found");
        }

        var environmentFailure = await CheckEnvironmentAsync(client, instanceOptions, endpointId, bounds, cancellationToken).ConfigureAwait(false);

        if (environmentFailure is not null)
        {
            return ExternalOperationResult.Unsupported<PortainerCapabilityData>(environmentFailure);
        }

        _logger.PortainerCapabilityResolved(instanceOptions.Name, endpointId);

        return ExternalOperationResult.Succeeded(new PortainerCapabilityData
                                                 {
                                                     IsConfigured = true,
                                                     SupportsActions = true,
                                                     Message = $"Portainer connected, endpoint {endpointId}",
                                                     EndpointId = endpointId,
                                                 });
    }

    /// <summary>
    /// Look up the container of an action request by its full identifier within an environment
    /// </summary>
    /// <param name="client">Authenticated HTTP client</param>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="actionRequest">Action request</param>
    /// <param name="actionName">Docker action of the request</param>
    /// <param name="endpointId">Environment identifier, digits only</param>
    /// <param name="bounds">Bounds of the requests of the operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Failed or not found result when the action must not be sent; null when the container exists</returns>
    private async Task<ExternalOperationResult<PortainerActionOutcomeData>?> LookUpContainerAsync(HttpClient client,
                                                                                                  DockerInstanceOptions instanceOptions,
                                                                                                  PortainerContainerActionRequest actionRequest,
                                                                                                  string actionName,
                                                                                                  string endpointId,
                                                                                                  PortainerRequestBounds bounds,
                                                                                                  CancellationToken cancellationToken)
    {
        var (statusCode, found) = await bounds.RunAsync(bounds.RequestTimeout,
                                                        token => FindContainerAsync(client, endpointId, actionRequest.ContainerId, token),
                                                        cancellationToken)
                                              .ConfigureAwait(false);

        if (statusCode is < (int)HttpStatusCode.OK or >= (int)HttpStatusCode.Ambiguous)
        {
            _logger.PortainerActionFailed(actionName, actionRequest.ContainerName, instanceOptions.Name, statusCode);

            return ExternalOperationResult.Failed<PortainerActionOutcomeData>($"Action '{actionName}' failed: HTTP {statusCode}");
        }

        if (found == false)
        {
            _logger.PortainerContainerNotFound(actionRequest.ContainerName, instanceOptions.Name);

            return ExternalOperationResult.NotFound<PortainerActionOutcomeData>(ContainerChangedMessage);
        }

        return null;
    }

    /// <summary>
    /// Send the action request and map its answer; from here on every failure may have happened after the Docker Engine applied the action
    /// </summary>
    /// <param name="client">Authenticated HTTP client</param>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="actionRequest">Action request</param>
    /// <param name="actionName">Docker action of the request</param>
    /// <param name="endpointId">Environment identifier, digits only</param>
    /// <param name="bounds">Bounds of the requests of the operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Action outcome</returns>
    private async Task<ExternalOperationResult<PortainerActionOutcomeData>> SendContainerActionAsync(HttpClient client,
                                                                                                     DockerInstanceOptions instanceOptions,
                                                                                                     PortainerContainerActionRequest actionRequest,
                                                                                                     string actionName,
                                                                                                     string endpointId,
                                                                                                     PortainerRequestBounds bounds,
                                                                                                     CancellationToken cancellationToken)
    {
        var actionUrl = $"api/endpoints/{endpointId}/docker/containers/{actionRequest.ContainerId}/{actionName}";
        var waitsForStop = actionRequest.ActionType is ContainerActionType.StopContainer or ContainerActionType.RestartContainer;
        var bound = waitsForStop ? bounds.RequestTimeout + TimeSpan.FromSeconds(StopWaitSeconds) : bounds.RequestTimeout;

        try
        {
            var statusCode = await bounds.RunAsync(bound, token => SendActionAsync(client, actionUrl, token), cancellationToken).ConfigureAwait(false);
            var status = (int)statusCode;

            if (statusCode is >= HttpStatusCode.OK and < HttpStatusCode.Ambiguous)
            {
                _logger.PortainerActionExecuted(actionName, actionRequest.ContainerName, instanceOptions.Name);

                return ExternalOperationResult.Succeeded(new PortainerActionOutcomeData
                                                         {
                                                             Message = $"Action '{actionName}' on '{actionRequest.ContainerName}' executed successfully",
                                                         });
            }

            if (statusCode == HttpStatusCode.NotModified)
            {
                _logger.PortainerActionExecuted(actionName, actionRequest.ContainerName, instanceOptions.Name);

                return ExternalOperationResult.Succeeded(new PortainerActionOutcomeData
                                                         {
                                                             AlreadyInRequestedState = true,
                                                             Message = $"Action '{actionName}' on '{actionRequest.ContainerName}' had no effect: the container was already in the requested state",
                                                         });
            }

            _logger.PortainerActionFailed(actionName, actionRequest.ContainerName, instanceOptions.Name, status);

            var failure = $"Action '{actionName}' failed: HTTP {status}";

            return status >= (int)HttpStatusCode.InternalServerError
                       ? ExternalOperationResult.Unknown<PortainerActionOutcomeData>(failure + MayHaveBeenAppliedSuffix)
                       : ExternalOperationResult.Failed<PortainerActionOutcomeData>(failure);
        }
        catch (Exception ex)
        {
            _logger.PortainerActionException(actionName, actionRequest.ContainerName, instanceOptions.Name, ex);

            return ExternalOperationResult.Unknown<PortainerActionOutcomeData>($"Action '{actionName}' failed: {FailureReason.FromException(ex, bounds.Latest, cancellationToken)}{MayHaveBeenAppliedSuffix}");
        }
    }

    /// <summary>
    /// Log in to Portainer with the user name and password of the options
    /// </summary>
    /// <param name="client">HTTP client with the base address of Portainer</param>
    /// <param name="loginBody">Login request body</param>
    /// <param name="loginUri">Address of the login request, used to track Retry-After windows</param>
    /// <param name="maskedBaseUrl">Portainer base URL without user info</param>
    /// <param name="cancellationToken">Cancellation token with the bound of the request</param>
    /// <returns>JWT token; null when the answer carries none</returns>
    private async Task<string?> LoginAsync(HttpClient client, PortainerLoginRequest loginBody, Uri loginUri, string maskedBaseUrl, CancellationToken cancellationToken)
    {
        using var loginResponse = await client.PostAsJsonAsync("api/auth", loginBody, cancellationToken).ConfigureAwait(false);

        _loginRetryAfterRegistry.RecordLoginResponse(loginUri, loginBody.Username, loginResponse);

        if (loginResponse.IsSuccessStatusCode == false)
        {
            _logger.PortainerAuthFailed(maskedBaseUrl);

            throw new PortainerAuthenticationException($"Portainer authentication failed with HTTP {(int)loginResponse.StatusCode} for '{maskedBaseUrl}'");
        }

        var authResponse = await ExternalJson.ReadFromJsonAsync<PortainerAuthResponse>(loginResponse.Content, cancellationToken).ConfigureAwait(false);

        return authResponse?.Jwt;
    }

    /// <summary>
    /// Create an authenticated HTTP client for the Portainer options of the given Docker instance.
    /// PAT takes precedence; falls back to username/password JWT login
    /// </summary>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="bounds">Bounds of the requests of the operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authenticated HTTP client</returns>
    private async Task<HttpClient> CreateAuthenticatedClientAsync(DockerInstanceOptions instanceOptions, PortainerRequestBounds bounds, CancellationToken cancellationToken)
    {
        var options = instanceOptions.Portainer;
        var client = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            client.BaseAddress = CreateBaseAddress(options.BaseUrl!);

            if (client.BaseAddress.Scheme == Uri.UriSchemeHttp
                && _insecureHttpWarningInstances.TryAdd(instanceOptions.Name, 0))
            {
                _logger.PortainerInsecureHttpInUse(instanceOptions.Name);
            }

            // Every request runs under its own bound, see PortainerRequestBounds
            client.Timeout = Timeout.InfiniteTimeSpan;

            if (string.IsNullOrWhiteSpace(options.ApiToken) == false)
            {
                client.DefaultRequestHeaders.Add("X-API-Key", options.ApiToken);

                return client;
            }

            if (string.IsNullOrWhiteSpace(options.Username) == false
                && string.IsNullOrWhiteSpace(options.Password) == false)
            {
                var maskedBaseUrl = UrlRedaction.RedactUserInfo(options.BaseUrl);
                var loginBody = new PortainerLoginRequest(options.Username, options.Password);
                var loginUri = new Uri(client.BaseAddress, "api/auth");

                if (_loginRetryAfterRegistry.IsLoginDeferred(loginUri, options.Username, out var retryAt))
                {
                    throw new PortainerAuthenticationException($"Portainer authentication for '{maskedBaseUrl}' not sent: Portainer answered HTTP 429 and asked to wait until {LoginRetryAfterRegistry.FormatRetryAt(retryAt)}");
                }

                var jwt = await bounds.RunAsync(bounds.RequestTimeout, token => LoginAsync(client, loginBody, loginUri, maskedBaseUrl, token), cancellationToken).ConfigureAwait(false);

                if (jwt is null)
                {
                    _logger.PortainerAuthFailed(maskedBaseUrl);

                    throw new PortainerAuthenticationException($"Portainer authentication succeeded but returned no JWT token for '{maskedBaseUrl}'");
                }

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            }

            return client;
        }
        catch
        {
            client.Dispose();

            throw;
        }
    }

    #endregion // Methods

    #region IPortainerClient

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<PortainerCapabilityData>> CheckCapabilityAsync(DockerInstanceOptions instanceOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instanceOptions);

        var notConfiguredMessage = GetNotConfiguredMessage(instanceOptions);

        if (notConfiguredMessage is not null)
        {
            return ExternalOperationResult.NotConfigured<PortainerCapabilityData>(notConfiguredMessage);
        }

        var bounds = CreateBounds(instanceOptions);

        try
        {
            using var client = await CreateAuthenticatedClientAsync(instanceOptions, bounds, cancellationToken).ConfigureAwait(false);

            return await ProbeEnvironmentAsync(client, instanceOptions, bounds, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.PortainerCapabilityException(instanceOptions.Name, ex);

            return ExternalOperationResult.Failed<PortainerCapabilityData>($"Portainer connection error: {FailureReason.FromException(ex, bounds.Latest, cancellationToken)}");
        }
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<PortainerActionOutcomeData>> ExecuteContainerActionAsync(DockerInstanceOptions instanceOptions,
                                                                                                       PortainerContainerActionRequest actionRequest,
                                                                                                       CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instanceOptions);
        ArgumentNullException.ThrowIfNull(actionRequest);

        var actionName = GetActionName(actionRequest.ActionType);

        if (actionName is null)
        {
            return ExternalOperationResult.Unsupported<PortainerActionOutcomeData>($"Action '{actionRequest.ActionType}' is not supported; allowed: start, stop, restart");
        }

        if (IsContainerId(actionRequest.ContainerId) == false)
        {
            return ExternalOperationResult.NotFound<PortainerActionOutcomeData>(ContainerChangedMessage);
        }

        var notConfiguredMessage = GetNotConfiguredMessage(instanceOptions);

        if (notConfiguredMessage is not null)
        {
            return ExternalOperationResult.NotConfigured<PortainerActionOutcomeData>(notConfiguredMessage);
        }

        var bounds = CreateBounds(instanceOptions);

        try
        {
            using var client = await CreateAuthenticatedClientAsync(instanceOptions, bounds, cancellationToken).ConfigureAwait(false);

            var capability = await ProbeEnvironmentAsync(client, instanceOptions, bounds, cancellationToken).ConfigureAwait(false);

            if (capability.Status != ExternalOperationStatus.Succeeded || capability.Data?.EndpointId is null)
            {
                return capability.PropagateFailure<PortainerActionOutcomeData>("No Portainer endpoint found");
            }

            var endpointId = capability.Data.EndpointId;
            var lookupFailure = await LookUpContainerAsync(client, instanceOptions, actionRequest, actionName, endpointId, bounds, cancellationToken).ConfigureAwait(false);

            if (lookupFailure is not null)
            {
                return lookupFailure;
            }

            return await SendContainerActionAsync(client, instanceOptions, actionRequest, actionName, endpointId, bounds, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.PortainerActionException(actionName, actionRequest.ContainerName, instanceOptions.Name, ex);

            return ExternalOperationResult.Failed<PortainerActionOutcomeData>($"Action '{actionName}' failed: {FailureReason.FromException(ex, bounds.Latest, cancellationToken)}");
        }
    }

    #endregion // IPortainerClient
}