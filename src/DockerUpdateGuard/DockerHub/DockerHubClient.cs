using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Telemetry;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.DockerHub;

/// <summary>
/// Conservative first iteration Docker Hub adapter
/// </summary>
public sealed class DockerHubClient : IDockerHubClient, IDisposable
{
    #region Fields

    private readonly HttpClient _httpClient;
    private readonly ILogger<DockerHubClient> _logger;
    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _optionsMonitor;
    private readonly SemaphoreSlim _tokenRefreshLock = new(1, 1);

    private DateTimeOffset _accessTokenExpiresAtUtc;
    private string? _accessToken;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="httpClient">Configured HTTP client</param>
    /// <param name="logger">Logger</param>
    /// <param name="optionsMonitor">Options monitor</param>
    public DockerHubClient(HttpClient httpClient,
                           ILogger<DockerHubClient> logger,
                           IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor)
    {
        _httpClient = httpClient;
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Resolve the base URI for Docker Hub requests
    /// </summary>
    /// <param name="options">Docker Hub options</param>
    /// <returns>Base URI</returns>
    public static Uri GetBaseUri(DockerHubOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (Uri.TryCreate(options.Registry, UriKind.Absolute, out var absoluteUri))
        {
            if (TryNormalizeDockerHubBaseUri(absoluteUri, out var normalizedDockerHubUri))
            {
                return normalizedDockerHubUri;
            }

            return absoluteUri;
        }

        return new Uri("https://hub.docker.com/");
    }

    #endregion // Static methods

    #region Methods

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<DockerHubAuthenticatedUserData>> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (IsSupportedRegistry(_optionsMonitor.CurrentValue.DockerHub.Registry) == false)
        {
            _logger.DockerHubRegistryUnsupported(_optionsMonitor.CurrentValue.DockerHub.Registry, nameof(GetCurrentUserAsync));

            return ExternalOperationResult<DockerHubAuthenticatedUserData>.Unsupported($"Registry '{_optionsMonitor.CurrentValue.DockerHub.Registry}' is not supported by the Docker Hub adapter");
        }

        using var response = await SendGetAsync("v2/user/", cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode == false)
        {
            _logger.DockerHubRequestFailed(nameof(GetCurrentUserAsync),
                                           "current-user",
                                           (int)response.StatusCode);

            return await CreateFailureResultAsync<DockerHubAuthenticatedUserData>(response,
                                                                                  "Docker Hub current user lookup failed",
                                                                                  cancellationToken).ConfigureAwait(false);
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                                                   .ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken)
                                                       .ConfigureAwait(false);
            var userName = TryGetString(jsonDocument.RootElement, "username");

            if (string.IsNullOrWhiteSpace(userName))
            {
                return ExternalOperationResult<DockerHubAuthenticatedUserData>.Failed("Docker Hub current user response did not contain a username");
            }

            _logger.DockerHubAuthenticatedAccountResolved(userName);

            return ExternalOperationResult<DockerHubAuthenticatedUserData>.Succeeded(new DockerHubAuthenticatedUserData
                                                                                     {
                                                                                         UserName = userName,
                                                                                     });
        }
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<IReadOnlyList<DockerHubRepositoryData>>> GetRepositoriesAsync(string accountName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        if (IsSupportedRegistry(_optionsMonitor.CurrentValue.DockerHub.Registry) == false)
        {
            _logger.DockerHubRegistryUnsupported(_optionsMonitor.CurrentValue.DockerHub.Registry, nameof(GetRepositoriesAsync));

            return ExternalOperationResult<IReadOnlyList<DockerHubRepositoryData>>.Unsupported($"Registry '{_optionsMonitor.CurrentValue.DockerHub.Registry}' is not supported by the Docker Hub adapter");
        }

        var repositories = new List<DockerHubRepositoryData>();
        var requestUri = $"v2/repositories/{Uri.EscapeDataString(accountName)}/?page_size=100";

        while (string.IsNullOrWhiteSpace(requestUri) == false)
        {
            using var response = await SendGetAsync(requestUri, cancellationToken)
                                     .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.DockerHubTargetNotFound(nameof(GetRepositoriesAsync), accountName);

                return ExternalOperationResult<IReadOnlyList<DockerHubRepositoryData>>.NotFound($"Docker Hub account '{accountName}' was not found");
            }

            if (response.IsSuccessStatusCode == false)
            {
                _logger.DockerHubRequestFailed(nameof(GetRepositoriesAsync),
                                               accountName,
                                               (int)response.StatusCode);

                return await CreateFailureResultAsync<IReadOnlyList<DockerHubRepositoryData>>(response,
                                                                                              $"Docker Hub repository listing failed for '{accountName}'",
                                                                                              cancellationToken)
                    .ConfigureAwait(false);
            }

            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                                                       .ConfigureAwait(false);

            await using (responseStream.ConfigureAwait(false))
            {
                using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken)
                                                           .ConfigureAwait(false);

                if (jsonDocument.RootElement.TryGetProperty("results", out var resultsElement)
                    && resultsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var resultElement in resultsElement.EnumerateArray())
                    {
                        repositories.Add(ParseRepository(resultElement));
                    }
                }

                requestUri = TryGetString(jsonDocument.RootElement, "next");
            }
        }

        _logger.DockerHubRepositoriesListed(accountName, repositories.Count);

        return ExternalOperationResult<IReadOnlyList<DockerHubRepositoryData>>.Succeeded(repositories);
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<DockerHubRepositoryData>> GetRepositoryAsync(ImageReference imageReference, CancellationToken cancellationToken = default)
    {
        if (IsSupportedRegistry(imageReference.Registry) == false)
        {
            _logger.DockerHubRegistryUnsupported(imageReference.Registry, nameof(GetRepositoryAsync));

            return ExternalOperationResult<DockerHubRepositoryData>.Unsupported($"Registry '{imageReference.Registry}' is not supported by the Docker Hub adapter");
        }

        var (namespaceName, repositoryName) = SplitRepository(imageReference.Repository);
        using var activity = DockerUpdateGuardTelemetry.ActivitySource.StartActivity(TelemetryActivityNames.DockerHubRequest, ActivityKind.Client);

        activity?.SetTag(TelemetryTagNames.ImageReference, imageReference.FullReference);

        using var response = await SendGetAsync($"v2/namespaces/{Uri.EscapeDataString(namespaceName)}/repositories/{EscapeRepository(repositoryName)}", cancellationToken)
                                 .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.DockerHubTargetNotFound(nameof(GetRepositoryAsync), imageReference.Repository);

            return ExternalOperationResult<DockerHubRepositoryData>.NotFound($"Repository '{imageReference.Repository}' was not found in Docker Hub");
        }

        if (response.IsSuccessStatusCode == false)
        {
            _logger.DockerHubRequestFailed(nameof(GetRepositoryAsync),
                                           imageReference.Repository,
                                           (int)response.StatusCode);

            return await CreateFailureResultAsync<DockerHubRepositoryData>(response,
                                                                           $"Repository metadata lookup failed for '{imageReference.Repository}'",
                                                                           cancellationToken).ConfigureAwait(false);
        }

        var responseStream = await response.Content
                                         .ReadAsStreamAsync(cancellationToken)
                                         .ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken)
                                                       .ConfigureAwait(false);
            var root = jsonDocument.RootElement;
            var repositoryData = new DockerHubRepositoryData
                                 {
                                     Registry = imageReference.Registry,
                                     Repository = imageReference.Repository,
                                     Description = TryGetString(root, "description"),
                                     LastUpdatedAtUtc = TryGetDateTimeOffset(root, "last_updated"),
                                 };

            return ExternalOperationResult<DockerHubRepositoryData>.Succeeded(repositoryData);
        }
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<DockerHubTagData>> GetTagAsync(ImageReference imageReference, CancellationToken cancellationToken = default)
    {
        if (IsSupportedRegistry(imageReference.Registry) == false)
        {
            _logger.DockerHubRegistryUnsupported(imageReference.Registry, nameof(GetTagAsync));

            return ExternalOperationResult<DockerHubTagData>.Unsupported($"Registry '{imageReference.Registry}' is not supported by the Docker Hub adapter");
        }

        var (namespaceName, repositoryName) = SplitRepository(imageReference.Repository);

        using var activity = DockerUpdateGuardTelemetry.ActivitySource.StartActivity(TelemetryActivityNames.DockerHubRequest, ActivityKind.Client);

        activity?.SetTag(TelemetryTagNames.ImageReference, imageReference.FullReference);

        using var response = await SendGetAsync($"v2/namespaces/{Uri.EscapeDataString(namespaceName)}/repositories/{EscapeRepository(repositoryName)}/tags/{Uri.EscapeDataString(imageReference.Tag)}", cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.DockerHubTargetNotFound(nameof(GetTagAsync), imageReference.FullReference);

            return ExternalOperationResult<DockerHubTagData>.NotFound($"Tag '{imageReference.Tag}' was not found for '{imageReference.Repository}'");
        }

        if (response.IsSuccessStatusCode == false)
        {
            _logger.DockerHubRequestFailed(nameof(GetTagAsync),
                                           imageReference.FullReference,
                                           (int)response.StatusCode);

            return await CreateFailureResultAsync<DockerHubTagData>(response,
                                                                    $"Tag metadata lookup failed for '{imageReference.FullReference}'",
                                                                    cancellationToken)
                         .ConfigureAwait(false);
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                                                   .ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken)
                                                       .ConfigureAwait(false);

            return ExternalOperationResult<DockerHubTagData>.Succeeded(ParseTag(jsonDocument.RootElement));
        }
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>> GetTagsAsync(string registry,
                                                                                             string repository,
                                                                                             CancellationToken cancellationToken = default)
    {
        if (IsSupportedRegistry(registry) == false)
        {
            _logger.DockerHubRegistryUnsupported(registry, nameof(GetTagsAsync));

            return ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Unsupported($"Registry '{registry}' is not supported by the Docker Hub adapter");
        }

        var (namespaceName, repositoryName) = SplitRepository(repository);
        using var response = await SendGetAsync($"v2/namespaces/{Uri.EscapeDataString(namespaceName)}/repositories/{EscapeRepository(repositoryName)}/tags?page_size=100", cancellationToken)
                                 .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.DockerHubTargetNotFound(nameof(GetTagsAsync), repository);

            return ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.NotFound($"Repository '{repository}' was not found in Docker Hub");
        }

        if (response.IsSuccessStatusCode == false)
        {
            _logger.DockerHubRequestFailed(nameof(GetTagsAsync),
                                           repository,
                                           (int)response.StatusCode);

            return await CreateFailureResultAsync<IReadOnlyList<DockerHubTagData>>(response,
                                                                                   $"Tag lookup failed for '{repository}'",
                                                                                   cancellationToken).ConfigureAwait(false);
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                                                   .ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken)
                                                       .ConfigureAwait(false);
            var results = new List<DockerHubTagData>();

            if (jsonDocument.RootElement.TryGetProperty("results", out var resultsElement)
                && resultsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var resultElement in resultsElement.EnumerateArray())
                {
                    results.Add(ParseTag(resultElement));
                }
            }

            return ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded(results);
        }
    }

    /// <inheritdoc/>
    public Task<ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>> ResolveBaseImagesAsync(ImageReference imageReference, CancellationToken cancellationToken = default)
    {
        _logger.DockerHubBaseImageResolutionUnavailable(imageReference.FullReference);

        return Task.FromResult(ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Unknown("Base image chain resolution is not available in the first host iteration"));
    }

    /// <summary>
    /// Send a GET request to Docker Hub
    /// </summary>
    /// <param name="relativeUri">Relative request URI</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response</returns>
    private async Task<HttpResponseMessage> SendGetAsync(string relativeUri, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        var authenticationResult = await ApplyAuthenticationAsync(request, cancellationToken)
                                   .ConfigureAwait(false);

        if (authenticationResult.Status == ExternalOperationStatus.Failed)
        {
            return CreateAuthenticationFailureResponse(authenticationResult.Message);
        }

        var response = await _httpClient.SendAsync(request,
                                                   HttpCompletionOption.ResponseHeadersRead,
                                                   cancellationToken)
                                        .ConfigureAwait(false);

        return response;
    }

    /// <summary>
    /// Apply Docker Hub authentication to an outbound request when configured
    /// </summary>
    /// <param name="request">Outbound request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result</returns>
    private async Task<ExternalOperationResult<bool>> ApplyAuthenticationAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _optionsMonitor.CurrentValue.DockerHub;

        if (string.IsNullOrWhiteSpace(options.Pat))
        {
            return ExternalOperationResult<bool>.Succeeded(true);
        }

        if (string.IsNullOrWhiteSpace(options.UserName))
        {
            _logger.DockerHubAuthenticationUserNameMissing();

            return ExternalOperationResult<bool>.Failed("Docker Hub authentication requires 'DockerUpdateGuard:DockerHub:UserName' when a PAT is configured");
        }

        var accessTokenResult = await GetAccessTokenAsync(options.UserName.Trim(),
                                                          options.Pat.Trim(),
                                                          cancellationToken)
                                      .ConfigureAwait(false);

        if (accessTokenResult.Status != ExternalOperationStatus.Succeeded
            || string.IsNullOrWhiteSpace(accessTokenResult.Data))
        {
            return ExternalOperationResult<bool>.Failed(accessTokenResult.Message ?? "Docker Hub authentication token request failed");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessTokenResult.Data);

        return ExternalOperationResult<bool>.Succeeded(true);
    }

    /// <summary>
    /// Get a cached Docker Hub access token or request a new one
    /// </summary>
    /// <param name="userName">Docker Hub user name</param>
    /// <param name="personalAccessToken">Docker Hub PAT</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Access token result</returns>
    private async Task<ExternalOperationResult<string>> GetAccessTokenAsync(string userName,
                                                                            string personalAccessToken,
                                                                            CancellationToken cancellationToken)
    {
        if (HasValidAccessToken())
        {
            return ExternalOperationResult<string>.Succeeded(_accessToken!);
        }

        await _tokenRefreshLock.WaitAsync(cancellationToken)
                               .ConfigureAwait(false);

        try
        {
            if (HasValidAccessToken())
            {
                return ExternalOperationResult<string>.Succeeded(_accessToken!);
            }

            var tokenResult = await RequestAccessTokenAsync(userName,
                                                            personalAccessToken,
                                                            cancellationToken)
                                  .ConfigureAwait(false);

            if (tokenResult.Status != ExternalOperationStatus.Succeeded
                || string.IsNullOrWhiteSpace(tokenResult.Data))
            {
                return tokenResult;
            }

            _accessToken = tokenResult.Data;
            _accessTokenExpiresAtUtc = ResolveTokenExpiry(tokenResult.Data);

            return ExternalOperationResult<string>.Succeeded(tokenResult.Data);
        }
        finally
        {
            _tokenRefreshLock.Release();
        }
    }

    /// <summary>
    /// Request a short-lived Docker Hub access token
    /// </summary>
    /// <param name="userName">Docker Hub user name</param>
    /// <param name="personalAccessToken">Docker Hub PAT</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Access token result</returns>
    private async Task<ExternalOperationResult<string>> RequestAccessTokenAsync(string userName,
                                                                                string personalAccessToken,
                                                                                CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
                                               {
                                                   username = userName,
                                                   password = personalAccessToken,
                                               });
        using var request = new HttpRequestMessage(HttpMethod.Post, "v2/users/login")
                            {
                                Content = new StringContent(payload,
                                                            Encoding.UTF8,
                                                            "application/json"),
                            };
        using var response = await _httpClient.SendAsync(request,
                                                         HttpCompletionOption.ResponseHeadersRead,
                                                         cancellationToken)
                                              .ConfigureAwait(false);

        if (response.IsSuccessStatusCode == false)
        {
            _logger.DockerHubRequestFailed(nameof(RequestAccessTokenAsync),
                                           userName,
                                           (int)response.StatusCode);

            return await CreateFailureResultAsync<string>(response,
                                                          $"Docker Hub authentication token request failed for '{userName}'",
                                                          cancellationToken)
                .ConfigureAwait(false);
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                                                   .ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken)
                                                       .ConfigureAwait(false);
            var token = TryGetString(jsonDocument.RootElement, "token");

            return string.IsNullOrWhiteSpace(token)
                ? ExternalOperationResult<string>.Failed("Docker Hub authentication token response did not contain a token")
                : ExternalOperationResult<string>.Succeeded(token);
        }
    }

    /// <summary>
    /// Determine whether the client currently holds a valid cached access token
    /// </summary>
    /// <returns>True when a cached token can be reused</returns>
    private bool HasValidAccessToken()
    {
        return string.IsNullOrWhiteSpace(_accessToken) == false
               && _accessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1);
    }

    /// <summary>
    /// Resolve the expiry of a Docker Hub access token
    /// </summary>
    /// <param name="token">JWT access token</param>
    /// <returns>Expiry timestamp</returns>
    private static DateTimeOffset ResolveTokenExpiry(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var segments = token.Split('.');

        if (segments.Length >= 2)
        {
            var payloadBytes = DecodeBase64Url(segments[1]);

            if (payloadBytes.Length > 0)
            {
                using var jsonDocument = JsonDocument.Parse(payloadBytes);

                if (jsonDocument.RootElement.TryGetProperty("exp", out var expirationElement)
                    && expirationElement.TryGetInt64(out var expirationUnixTimeSeconds))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(expirationUnixTimeSeconds);
                }
            }
        }

        return DateTimeOffset.UtcNow.AddMinutes(5);
    }

    /// <summary>
    /// Decode a Base64 URL value
    /// </summary>
    /// <param name="value">Encoded value</param>
    /// <returns>Decoded bytes</returns>
    private static byte[] DecodeBase64Url(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalizedValue = value.Replace('-', '+')
                                   .Replace('_', '/');
        var paddingLength = (4 - (normalizedValue.Length % 4)) % 4;
        var paddedValue = normalizedValue.PadRight(normalizedValue.Length + paddingLength, '=');

        return Convert.FromBase64String(paddedValue);
    }

    /// <summary>
    /// Create a synthetic response for an authentication failure
    /// </summary>
    /// <param name="message">Failure message</param>
    /// <returns>HTTP response</returns>
    private static HttpResponseMessage CreateAuthenticationFailureResponse(string? message)
    {
        return new HttpResponseMessage(HttpStatusCode.Unauthorized)
               {
                   Content = new StringContent(message ?? "Docker Hub authentication failed",
                                               Encoding.UTF8,
                                               "text/plain"),
               };
    }

    /// <summary>
    /// Create a typed failure result for an unsuccessful response
    /// </summary>
    /// <typeparam name="T">Payload type</typeparam>
    /// <param name="response">HTTP response</param>
    /// <param name="message">Failure message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Typed failure result</returns>
    private static async Task<ExternalOperationResult<T>> CreateFailureResultAsync<T>(HttpResponseMessage response,
                                                                                      string message,
                                                                                      CancellationToken cancellationToken)
    {
        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken)
                                              .ConfigureAwait(false);

        return ExternalOperationResult<T>.Failed($"{message}. Status code: {(int)response.StatusCode}. Body: {errorBody}");
    }

    /// <summary>
    /// Parse a Docker Hub tag payload
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <returns>Tag data</returns>
    private static DockerHubTagData ParseTag(JsonElement element)
    {
        return new DockerHubTagData
               {
                   Tag = TryGetString(element, "name") ?? string.Empty,
                   Digest = TryGetString(element, "digest"),
                   PublishedAtUtc = TryGetDateTimeOffset(element, "last_pushed"),
               };
    }

    /// <summary>
    /// Parse a Docker Hub repository payload
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <returns>Repository data</returns>
    private static DockerHubRepositoryData ParseRepository(JsonElement element)
    {
        var namespaceName = TryGetString(element, "namespace");
        var repositoryName = TryGetString(element, "name");
        var normalizedRepository = string.IsNullOrWhiteSpace(namespaceName) || string.IsNullOrWhiteSpace(repositoryName)
            ? string.Empty
            : $"{namespaceName.Trim().ToLowerInvariant()}/{repositoryName.Trim().ToLowerInvariant()}";

        return new DockerHubRepositoryData
               {
                   Registry = "docker.io",
                   Repository = normalizedRepository,
                   Description = TryGetString(element, "description"),
                   LastUpdatedAtUtc = TryGetDateTimeOffset(element, "last_updated"),
               };
    }

    /// <summary>
    /// Determine whether the registry can be served by Docker Hub endpoints
    /// </summary>
    /// <param name="registry">Registry value</param>
    /// <returns>True when supported</returns>
    private static bool IsSupportedRegistry(string registry)
    {
        if (string.IsNullOrWhiteSpace(registry))
        {
            return false;
        }

        if (Uri.TryCreate(registry, UriKind.Absolute, out var registryUri))
        {
            return IsSupportedDockerHubHost(registryUri.Host);
        }

        return IsSupportedDockerHubHost(registry);
    }

    /// <summary>
    /// Normalize known Docker Hub URLs to the API host
    /// </summary>
    /// <param name="registryUri">Configured registry URI</param>
    /// <param name="normalizedUri">Normalized Docker Hub base URI</param>
    /// <returns>True when the URI points to Docker Hub</returns>
    private static bool TryNormalizeDockerHubBaseUri(Uri registryUri, out Uri normalizedUri)
    {
        ArgumentNullException.ThrowIfNull(registryUri);

        normalizedUri = new Uri("https://hub.docker.com/");

        return IsSupportedDockerHubHost(registryUri.Host);
    }

    /// <summary>
    /// Determine whether a host name belongs to Docker Hub
    /// </summary>
    /// <param name="host">Host or registry value</param>
    /// <returns>True when the host is a supported Docker Hub alias</returns>
    private static bool IsSupportedDockerHubHost(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        return string.Equals(host, "docker.io", StringComparison.OrdinalIgnoreCase)
               || string.Equals(host, "index.docker.io", StringComparison.OrdinalIgnoreCase)
               || string.Equals(host, "registry-1.docker.io", StringComparison.OrdinalIgnoreCase)
               || string.Equals(host, "hub.docker.com", StringComparison.OrdinalIgnoreCase)
               || string.Equals(host, "docker.com", StringComparison.OrdinalIgnoreCase)
               || string.Equals(host, "www.docker.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Split the Docker Hub repository path into namespace and repository name
    /// </summary>
    /// <param name="repository">Repository path</param>
    /// <returns>Namespace and repository name</returns>
    private static (string NamespaceName, string RepositoryName) SplitRepository(string repository)
    {
        var segments = repository.Split('/');

        if (segments.Length == 1)
        {
            return ("library", segments[0]);
        }

        return (segments[0], string.Join('/', segments.Skip(1)));
    }

    /// <summary>
    /// Escape a repository path for use in a request URI
    /// </summary>
    /// <param name="repository">Repository value</param>
    /// <returns>Escaped repository path</returns>
    private static string EscapeRepository(string repository)
    {
        return string.Join('/', repository.Split('/').Select(Uri.EscapeDataString));
    }

    /// <summary>
    /// Read an optional string from JSON
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <param name="propertyName">Property name</param>
    /// <returns>String value</returns>
    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }

    /// <summary>
    /// Read an optional timestamp from JSON
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <param name="propertyName">Property name</param>
    /// <returns>Timestamp value</returns>
    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement element, string propertyName)
    {
        var rawValue = TryGetString(element, propertyName);

        if (DateTimeOffset.TryParse(rawValue, out var timestamp))
        {
            return timestamp;
        }

        return null;
    }

    #endregion // Methods

    #region IDisposable implementation

    /// <summary>
    /// Releases the resources used by the current instance of the class
    /// </summary>
    public void Dispose()
    {
        _tokenRefreshLock.Dispose();
    }

    #endregion // IDisposable implementation
}