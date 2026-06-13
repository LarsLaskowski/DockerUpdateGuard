using System.Net.Http.Headers;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Portainer.Data;

namespace DockerUpdateGuard.Portainer;

/// <summary>
/// Portainer API adapter
/// </summary>
public partial class PortainerClient : IPortainerClient
{
    #region Constants

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
    /// Resolve the first available Portainer endpoint identifier
    /// </summary>
    /// <param name="client">Authenticated HTTP client</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Endpoint identifier or null</returns>
    private static async Task<string?> ResolveEndpointIdAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/api/endpoints", cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode == false)
        {
            return null;
        }

        var endpoints = await response.Content.ReadFromJsonAsync<PortainerEndpointItem[]>(cancellationToken: cancellationToken).ConfigureAwait(false);

        return endpoints?.FirstOrDefault()?.Id.ToString();
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
        var filters = Uri.EscapeDataString($"{{\"name\":[\"{containerName}\"]}}");
        var url = $"/api/endpoints/{endpointId}/docker/containers/json?all=true&filters={filters}";

        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode == false)
        {
            return null;
        }

        var containers = await response.Content.ReadFromJsonAsync<DockerContainerItem[]>(cancellationToken: cancellationToken).ConfigureAwait(false);

        return containers?.FirstOrDefault(c =>
        c.Names?.Any(n => string.Equals(n, $"/{containerName}", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(n, containerName, StringComparison.OrdinalIgnoreCase)) == true)?.Id;
    }

    #endregion // Static methods

    #region Methods

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
            using var client = await CreateAuthenticatedClientAsync(instanceOptions.Portainer, cancellationToken).ConfigureAwait(false);
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

            var endpointId = instanceOptions.Portainer.EndpointId;

            if (string.IsNullOrWhiteSpace(endpointId))
            {
                endpointId = await ResolveEndpointIdAsync(client, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(endpointId))
            {
                return new PortainerCapabilityData
                       {
                           IsConfigured = true,
                           SupportsActions = false,
                           Message = "No Portainer endpoint found",
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
                       Message = $"Portainer connection error: {ex.Message}",
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
            using var client = await CreateAuthenticatedClientAsync(instanceOptions.Portainer, cancellationToken).ConfigureAwait(false);

            var endpointId = instanceOptions.Portainer.EndpointId;

            if (string.IsNullOrWhiteSpace(endpointId))
            {
                endpointId = await ResolveEndpointIdAsync(client, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(endpointId))
            {
                return new PortainerActionResult
                       {
                           Succeeded = false,
                           Message = "No Portainer endpoint found",
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
            using var actionResponse = await client.PostAsync(actionUrl, content: null, cancellationToken).ConfigureAwait(false);

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
                       Message = $"Action '{actionRequest.ActionName}' failed: {ex.Message}",
                   };
        }
    }

    /// <summary>
    /// Create an authenticated HTTP client for the given Portainer options.
    /// PAT takes precedence; falls back to username/password JWT login
    /// </summary>
    /// <param name="options">Portainer options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authenticated HTTP client</returns>
    private async Task<HttpClient> CreateAuthenticatedClientAsync(PortainerOptions options, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();

        try
        {
            client.BaseAddress = new Uri(options.BaseUrl!);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);

            if (string.IsNullOrWhiteSpace(options.ApiToken) == false)
            {
                client.DefaultRequestHeaders.Add("X-API-Key", options.ApiToken);

                return client;
            }

            if (string.IsNullOrWhiteSpace(options.Username) == false
                && string.IsNullOrWhiteSpace(options.Password) == false)
            {
                var loginBody = new PortainerLoginRequest(options.Username!, options.Password!);
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
                        _logger.PortainerAuthFailed(options.BaseUrl ?? string.Empty);

                        throw new InvalidOperationException($"Portainer authentication succeeded but returned no JWT token for '{options.BaseUrl}'");
                    }
                }
                else
                {
                    _logger.PortainerAuthFailed(options.BaseUrl ?? string.Empty);

                    throw new InvalidOperationException($"Portainer authentication failed with HTTP {(int)loginResponse.StatusCode} for '{options.BaseUrl}'");
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
}