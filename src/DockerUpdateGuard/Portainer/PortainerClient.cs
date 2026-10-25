using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

using DockerUpdateGuard.Configuration;
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
    /// Supported container actions
    /// </summary>
    private static readonly HashSet<string> _allowedContainerActions = new(StringComparer.OrdinalIgnoreCase)
                                                                       {
                                                                           "start",
                                                                           "stop",
                                                                           "restart",
                                                                           "kill",
                                                                           "pause",
                                                                           "unpause"
                                                                       };

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

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory</param>
    /// <param name="logger">Logger</param>
    public PortainerClient(IHttpClientFactory httpClientFactory, ILogger<PortainerClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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
    /// Find a container identifier by container name within an endpoint
    /// </summary>
    /// <param name="client">Authenticated HTTP client</param>
    /// <param name="endpointId">Portainer endpoint identifier</param>
    /// <param name="containerName">Container name to search for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Container identifier or null</returns>
    private static async Task<string?> FindContainerIdAsync(HttpClient client,
                                                            string endpointId,
                                                            string containerName,
                                                            CancellationToken cancellationToken)
    {
        var filter = new Dictionary<string, string[]>(StringComparer.Ordinal)
                     {
                         ["name"] = [containerName],
                     };

        var filters = Uri.EscapeDataString(JsonSerializer.Serialize(filter));
        var url = $"/api/endpoints/{endpointId}/docker/containers/json?all=true&filters={filters}";

        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode == false)
        {
            return null;
        }

        var containers = await response.Content.ReadFromJsonAsync<DockerContainerItem[]>(cancellationToken: cancellationToken).ConfigureAwait(false);

        return containers?.FirstOrDefault(c => c.Names?.Any(n => string.Equals(n, $"/{containerName}", StringComparison.OrdinalIgnoreCase)
                                                                 || string.Equals(n, containerName, StringComparison.OrdinalIgnoreCase)) == true)
                         ?.Id;
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Resolve the Portainer environment to use for a Docker instance
    /// </summary>
    /// <param name="client">Authenticated HTTP client</param>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resolution carrying either the environment identifier or a failure message</returns>
    private async Task<PortainerEndpointResolution> ResolveEndpointAsync(HttpClient client, DockerInstanceOptions instanceOptions, CancellationToken cancellationToken)
    {
        var configuredEndpointId = instanceOptions.Portainer.EndpointId;

        if (string.IsNullOrWhiteSpace(configuredEndpointId) == false)
        {
            return new PortainerEndpointResolution(configuredEndpointId, null);
        }

        using var response = await client.GetAsync("/api/endpoints", cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode == false)
        {
            _logger.PortainerEnvironmentListFailed(instanceOptions.Name, (int)response.StatusCode);

            return new PortainerEndpointResolution(null, $"Portainer environment list failed: HTTP {(int)response.StatusCode}");
        }

        var endpoints = await response.Content.ReadFromJsonAsync<PortainerEndpointItem[]>(cancellationToken: cancellationToken).ConfigureAwait(false) ?? [];
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
    /// Create an authenticated HTTP client for the Portainer options of the given Docker instance.
    /// PAT takes precedence; falls back to username/password JWT login
    /// </summary>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authenticated HTTP client</returns>
    private async Task<HttpClient> CreateAuthenticatedClientAsync(DockerInstanceOptions instanceOptions, CancellationToken cancellationToken)
    {
        var options = instanceOptions.Portainer;
        var client = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            client.BaseAddress = new Uri(options.BaseUrl!);

            if (client.BaseAddress.Scheme == Uri.UriSchemeHttp
                && _insecureHttpWarningInstances.TryAdd(instanceOptions.Name, 0))
            {
                _logger.PortainerInsecureHttpInUse(instanceOptions.Name);
            }

            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);

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
                using var loginResponse = await client.PostAsJsonAsync("/api/auth", loginBody, cancellationToken).ConfigureAwait(false);

                if (loginResponse.IsSuccessStatusCode)
                {
                    var authResponse = await loginResponse.Content.ReadFromJsonAsync<PortainerAuthResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);

                    if (authResponse?.Jwt is not null)
                    {
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse.Jwt);
                    }
                    else
                    {
                        _logger.PortainerAuthFailed(maskedBaseUrl);

                        throw new PortainerAuthenticationException($"Portainer authentication succeeded but returned no JWT token for '{maskedBaseUrl}'");
                    }
                }
                else
                {
                    _logger.PortainerAuthFailed(maskedBaseUrl);

                    throw new PortainerAuthenticationException($"Portainer authentication failed with HTTP {(int)loginResponse.StatusCode} for '{maskedBaseUrl}'");
                }
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
    public async Task<PortainerCapabilityData> GetCapabilityAsync(DockerInstanceOptions instanceOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instanceOptions);

        if (instanceOptions.Portainer.Enabled == false)
        {
            _logger.PortainerCapabilityNotConfigured(instanceOptions.Name);

            return new PortainerCapabilityData
                   {
                       IsConfigured = false,
                       SupportsActions = false,
                       Message = "Portainer is not configured for this Docker instance",
                   };
        }

        if (string.IsNullOrWhiteSpace(instanceOptions.Portainer.BaseUrl))
        {
            return new PortainerCapabilityData
                   {
                       IsConfigured = false,
                       SupportsActions = false,
                       Message = "Portainer base URL is not configured",
                   };
        }

        try
        {
            using var client = await CreateAuthenticatedClientAsync(instanceOptions, cancellationToken).ConfigureAwait(false);
            using var statusResponse = await client.GetAsync("/api/system/status", cancellationToken).ConfigureAwait(false);

            if (statusResponse.IsSuccessStatusCode == false)
            {
                _logger.PortainerCapabilityConnectFailed(instanceOptions.Name, (int)statusResponse.StatusCode);

                return new PortainerCapabilityData
                       {
                           IsConfigured = true,
                           SupportsActions = false,
                           Message = $"Portainer connection failed: HTTP {(int)statusResponse.StatusCode}",
                       };
            }

            var resolution = await ResolveEndpointAsync(client, instanceOptions, cancellationToken).ConfigureAwait(false);
            var endpointId = resolution.EndpointId;

            if (string.IsNullOrWhiteSpace(endpointId))
            {
                return new PortainerCapabilityData
                       {
                           IsConfigured = true,
                           SupportsActions = false,
                           Message = resolution.FailureMessage ?? "No Portainer endpoint found",
                       };
            }

            _logger.PortainerCapabilityResolved(instanceOptions.Name, endpointId);

            return new PortainerCapabilityData
                   {
                       IsConfigured = true,
                       SupportsActions = true,
                       Message = $"Portainer connected, endpoint {endpointId}",
                   };
        }
        catch (Exception ex)
        {
            _logger.PortainerCapabilityException(instanceOptions.Name, ex);

            return new PortainerCapabilityData
                   {
                       IsConfigured = true,
                       SupportsActions = false,
                       Message = $"Portainer connection error: {FailureReason.FromException(ex, TimeSpan.FromSeconds(instanceOptions.Portainer.RequestTimeoutSeconds), cancellationToken)}",
                   };
        }
    }

    /// <inheritdoc/>
    public async Task<PortainerActionResult> ExecuteActionAsync(DockerInstanceOptions instanceOptions,
                                                                PortainerActionRequest actionRequest,
                                                                CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instanceOptions);
        ArgumentNullException.ThrowIfNull(actionRequest);

        if (instanceOptions.Portainer.Enabled == false || string.IsNullOrWhiteSpace(instanceOptions.Portainer.BaseUrl))
        {
            return new PortainerActionResult
                   {
                       Succeeded = false,
                       Message = "Portainer is not configured",
                   };
        }

        if (_allowedContainerActions.Contains(actionRequest.ActionName) == false)
        {
            return new PortainerActionResult
                   {
                       Succeeded = false,
                       Message = $"Action '{actionRequest.ActionName}' is not supported — allowed: {string.Join(", ", _allowedContainerActions)}",
                   };
        }

        try
        {
            using var client = await CreateAuthenticatedClientAsync(instanceOptions, cancellationToken).ConfigureAwait(false);

            var resolution = await ResolveEndpointAsync(client, instanceOptions, cancellationToken).ConfigureAwait(false);
            var endpointId = resolution.EndpointId;

            if (string.IsNullOrWhiteSpace(endpointId))
            {
                return new PortainerActionResult
                       {
                           Succeeded = false,
                           Message = resolution.FailureMessage ?? "No Portainer endpoint found",
                       };
            }

            var containerId = await FindContainerIdAsync(client, endpointId, actionRequest.ResourceName, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(containerId))
            {
                _logger.PortainerContainerNotFound(actionRequest.ResourceName, instanceOptions.Name);

                return new PortainerActionResult
                       {
                           Succeeded = false,
                           Message = $"Container '{actionRequest.ResourceName}' not found in endpoint {endpointId}",
                       };
            }

            var actionUrl = $"/api/endpoints/{endpointId}/docker/containers/{containerId}/{actionRequest.ActionName}";

            // An empty body keeps SocketsHttpHandler from resending a body-less POST on a reused connection
            using var actionContent = new ByteArrayContent([]);
            using var actionResponse = await client.PostAsync(actionUrl, actionContent, cancellationToken).ConfigureAwait(false);

            if (actionResponse.IsSuccessStatusCode)
            {
                _logger.PortainerActionExecuted(actionRequest.ActionName, actionRequest.ResourceName, instanceOptions.Name);

                return new PortainerActionResult
                       {
                           Succeeded = true,
                           Message = $"Action '{actionRequest.ActionName}' on '{actionRequest.ResourceName}' executed successfully",
                       };
            }

            _logger.PortainerActionFailed(actionRequest.ActionName, actionRequest.ResourceName, instanceOptions.Name, (int)actionResponse.StatusCode);

            return new PortainerActionResult
                   {
                       Succeeded = false,
                       Message = $"Action '{actionRequest.ActionName}' failed: HTTP {(int)actionResponse.StatusCode}",
                   };
        }
        catch (Exception ex)
        {
            _logger.PortainerActionException(actionRequest.ActionName, actionRequest.ResourceName, instanceOptions.Name, ex);

            return new PortainerActionResult
                   {
                       Succeeded = false,
                       Message = $"Action '{actionRequest.ActionName}' failed: {FailureReason.FromException(ex, TimeSpan.FromSeconds(instanceOptions.Portainer.RequestTimeoutSeconds), cancellationToken)}",
                   };
        }
    }

    #endregion // IPortainerClient
}