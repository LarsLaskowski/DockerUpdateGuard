using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Helper;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Telemetry;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.DockerHub;

/// <summary>
/// Conservative first iteration Docker Hub adapter
/// </summary>
public sealed class DockerHubClient : IDockerHubClient, IRegistryMetadataClient, IDisposable
{
    #region Constants

    /// <summary>
    /// Name of the digest property in registry payloads
    /// </summary>
    private const string DigestPropertyName = "digest";

    /// <summary>
    /// Name of the config property in registry payloads
    /// </summary>
    private const string ConfigPropertyName = "config";

    /// <summary>
    /// Authorization scheme used for registry requests
    /// </summary>
    private const string BearerScheme = "Bearer";

    /// <summary>
    /// Login path relative to the API base address
    /// </summary>
    private const string LoginPath = "v2/users/login";

    /// <summary>
    /// Registry resource of manifests
    /// </summary>
    private const string ManifestsResource = "manifests";

    /// <summary>
    /// Registry resource of blobs
    /// </summary>
    private const string BlobsResource = "blobs";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// HTTP client
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Logger
    /// </summary>
    private readonly ILogger<DockerHubClient> _logger;

    /// <summary>
    /// Options monitor
    /// </summary>
    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _optionsMonitor;

    /// <summary>
    /// Registry of the login Retry-After windows
    /// </summary>
    private readonly LoginRetryAfterRegistry _loginRetryAfterRegistry;

    /// <summary>
    /// Shared cache of the Docker Hub access token
    /// </summary>
    private readonly DockerHubAccessTokenCache _accessTokenCache;

    /// <summary>
    /// Throttle bounding the number of concurrent outbound Docker Hub and registry requests
    /// </summary>
    private readonly SemaphoreSlim _requestThrottle;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="httpClient">Configured HTTP client</param>
    /// <param name="logger">Logger</param>
    /// <param name="optionsMonitor">Options monitor</param>
    /// <param name="loginRetryAfterRegistry">Registry of the login Retry-After windows</param>
    /// <param name="accessTokenCache">Shared cache of the Docker Hub access token</param>
    public DockerHubClient(HttpClient httpClient,
                           ILogger<DockerHubClient> logger,
                           IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor,
                           LoginRetryAfterRegistry loginRetryAfterRegistry,
                           DockerHubAccessTokenCache accessTokenCache)
    {
        _httpClient = httpClient;
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _loginRetryAfterRegistry = loginRetryAfterRegistry;
        _accessTokenCache = accessTokenCache;

        var maxParallelRequests = Math.Max(1, optionsMonitor.CurrentValue.DockerHub.MaxParallelRequests);
        _requestThrottle = new SemaphoreSlim(maxParallelRequests, maxParallelRequests);
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

        var apiBaseUri = new Uri(options.ApiBaseUrl);

        if (Uri.TryCreate(options.Registry, UriKind.Absolute, out var absoluteUri))
        {
            if (IsSupportedDockerHubHost(absoluteUri.Host))
            {
                return apiBaseUri;
            }

            return absoluteUri;
        }

        return apiBaseUri;
    }

    /// <summary>
    /// Determine whether the registry is served by Docker Hub
    /// </summary>
    /// <param name="registry">Registry value</param>
    /// <returns>True when the registry belongs to Docker Hub</returns>
    public static bool SupportsRegistry(string registry)
    {
        return IsSupportedRegistry(registry);
    }

    /// <summary>
    /// Build the message of a login that was not sent because a Retry-After window is open
    /// </summary>
    /// <param name="userName">Docker Hub user name</param>
    /// <param name="retryAt">End of the window</param>
    /// <returns>Failure message</returns>
    internal static string CreateLoginDeferredMessage(string userName, DateTimeOffset retryAt)
    {
        return $"Docker Hub authentication token request for '{userName}' not sent: Docker Hub answered HTTP 429 and asked to wait until {LoginRetryAfterRegistry.FormatRetryAt(retryAt)}";
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
        var errorBody = await ResponseBodyExcerpt.ReadAsync(response.Content, cancellationToken)
                                                 .ConfigureAwait(false);

        return ExternalOperationResult.Failed<T>($"{message}. Status code: {(int)response.StatusCode}. Body: {errorBody}");
    }

    /// <summary>
    /// Parse a Docker Hub tag payload
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <returns>Tag data or null</returns>
    private static DockerHubTagData? ParseTag(JsonElement element)
    {
        var name = TryGetString(element, "name");

        if (RegistryReferenceValidator.IsValidTag(name) == false)
        {
            return null;
        }

        return CreateTagData(element, name);
    }

    /// <summary>
    /// Create the tag data of a Docker Hub tag payload
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <param name="tag">Tag name</param>
    /// <returns>Tag data</returns>
    private static DockerHubTagData CreateTagData(JsonElement element, string tag)
    {
        return new DockerHubTagData
               {
                   Tag = tag,
                   Digest = RegistryReferenceValidator.ValidDigestOrNull(ResolveDockerHubDigest(element)),
                   PublishedAtUtc = TryGetDateTimeOffset(element, "last_pushed"),
               };
    }

    /// <summary>
    /// Resolve the Docker Hub tag digest from the top-level payload
    /// </summary>
    /// <param name="element">Tag JSON element</param>
    /// <returns>Resolved digest</returns>
    private static string? ResolveDockerHubDigest(JsonElement element)
    {
        return TryGetString(element, DigestPropertyName);
    }

    /// <summary>
    /// Select the platform manifest digest from a multi-arch manifest list
    /// </summary>
    /// <param name="manifestsElement">Manifest list "manifests" array element</param>
    /// <returns>Resolved platform manifest digest or null</returns>
    private static string? SelectManifestDigest(JsonElement manifestsElement)
    {
        var linuxDigest = default(string);
        var fallbackDigest = default(string);

        foreach (var manifest in manifestsElement.EnumerateArray())
        {
            var digest = TryGetString(manifest, DigestPropertyName);

            if (string.IsNullOrWhiteSpace(digest))
            {
                continue;
            }

            if (manifest.TryGetProperty("platform", out var platformElement) == false)
            {
                fallbackDigest ??= digest;

                continue;
            }

            var manifestOperatingSystem = TryGetString(platformElement, "os");
            var manifestArchitecture = TryGetString(platformElement, "architecture");

            if (string.Equals(manifestOperatingSystem, "unknown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(manifestArchitecture, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            fallbackDigest ??= digest;

            if (string.Equals(manifestOperatingSystem, "linux", StringComparison.OrdinalIgnoreCase))
            {
                linuxDigest ??= digest;
            }
        }

        return linuxDigest ?? fallbackDigest;
    }

    /// <summary>
    /// Parse a Docker Hub repository payload
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <returns>Repository data</returns>
    private static DockerHubRepositoryData ParseRepository(JsonElement element)
    {
        var namespaceName = ResponseBodyExcerpt.ReplaceControlCharacters(TryGetString(element, "namespace"));
        var repositoryName = ResponseBodyExcerpt.ReplaceControlCharacters(TryGetString(element, "name"));
        var normalizedRepository = string.IsNullOrWhiteSpace(namespaceName) || string.IsNullOrWhiteSpace(repositoryName)
                                       ? string.Empty
                                       : $"{namespaceName.Trim().ToLowerInvariant()}/{repositoryName.Trim().ToLowerInvariant()}";

        if (RegistryReferenceValidator.IsValidRepository(normalizedRepository) == false)
        {
            normalizedRepository = string.Empty;
        }

        return new DockerHubRepositoryData
               {
                   Registry = "docker.io",
                   Repository = normalizedRepository,
                   Description = ResponseBodyExcerpt.ReplaceControlCharacters(TryGetString(element, "description")),
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

        if (Uri.TryCreate(registry, UriKind.Absolute, out var registryUri) && string.IsNullOrEmpty(registryUri.Host) == false)
        {
            return IsSupportedDockerHubHost(registryUri.Host);
        }

        return IsSupportedDockerHubHost(registry);
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
    /// Create the escaped namespace and repository path of a Docker Hub repository
    /// </summary>
    /// <param name="repository">Repository path</param>
    /// <returns>Escaped path or null when the repository has a dot segment</returns>
    private static string? CreateRepositoryPath(string repository)
    {
        if (RegistryReferenceValidator.HasDotSegment(repository))
        {
            return null;
        }

        var (namespaceName, repositoryName) = SplitRepository(repository);

        return $"{Uri.EscapeDataString(namespaceName)}/{EscapeRepository(repositoryName)}";
    }

    /// <summary>
    /// Create the web API path segment pair of a repository, namespace and repositories keyword included
    /// </summary>
    /// <param name="repositoryPath">Escaped namespace and repository path</param>
    /// <returns>Path below the namespaces resource</returns>
    private static string CreateRepositoryApiPath(string repositoryPath)
    {
        return repositoryPath.Insert(repositoryPath.IndexOf('/', StringComparison.Ordinal) + 1, "repositories/");
    }

    /// <summary>
    /// Get the tag or, when present, the digest to request the manifest with
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <returns>Digest or tag</returns>
    private static string GetManifestReference(ImageReference imageReference)
    {
        return string.IsNullOrWhiteSpace(imageReference.Digest) ? imageReference.Tag : imageReference.Digest;
    }

    /// <summary>
    /// Create a Docker Registry request URI for a manifest or blob
    /// </summary>
    /// <param name="repository">Repository path</param>
    /// <param name="resource">Resource kind, manifests or blobs</param>
    /// <param name="reference">Tag or digest</param>
    /// <returns>Request URI or null when the repository or reference has a dot segment</returns>
    private static Uri? CreateRegistryRequestUri(string repository, string resource, string reference)
    {
        var repositoryPath = CreateRepositoryPath(repository);

        if (repositoryPath is null || RegistryReferenceValidator.HasDotSegment(reference))
        {
            return null;
        }

        return new Uri($"https://registry-1.docker.io/v2/{repositoryPath}/{resource}/{Uri.EscapeDataString(reference)}");
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

        if (DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture, out var timestamp))
        {
            return timestamp;
        }

        return null;
    }

    /// <summary>
    /// Check whether a tag is a concrete version tag of the current digest
    /// </summary>
    /// <param name="tagData">Tag metadata</param>
    /// <param name="queryOptions">Tag query options</param>
    /// <returns>True when the tag is a displayable specific version tag with the current digest</returns>
    private static bool IsResolvedCurrentVersionTag(DockerHubTagData tagData, RegistryTagQueryOptions? queryOptions)
    {
        return string.IsNullOrWhiteSpace(queryOptions?.CurrentDigest) == false
               && string.Equals(tagData.Digest,
                                queryOptions.CurrentDigest,
                                StringComparison.OrdinalIgnoreCase)
               && VersionTagResolutionHelper.IsDisplayableSpecificVersionTag(tagData.Tag);
    }

    /// <summary>
    /// Append the tags of a Docker Hub tag listing page to the collected results
    /// </summary>
    /// <param name="rootElement">Root element of the tag listing page</param>
    /// <param name="queryOptions">Tag query options</param>
    /// <param name="results">Collected tag metadata</param>
    /// <param name="resolvedCurrentVersionTag">True when a concrete version tag of the current digest has been seen</param>
    /// <returns>True when further pages should be requested</returns>
    private static bool AppendTagPage(JsonElement rootElement,
                                      RegistryTagQueryOptions? queryOptions,
                                      List<DockerHubTagData> results,
                                      ref bool resolvedCurrentVersionTag)
    {
        if (rootElement.TryGetProperty("results", out var resultsElement) == false
            || resultsElement.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        foreach (var resultElement in resultsElement.EnumerateArray())
        {
            var tagData = ParseTag(resultElement);

            if (tagData is null)
            {
                continue;
            }

            resolvedCurrentVersionTag |= IsResolvedCurrentVersionTag(tagData, queryOptions);

            if (RegistryTagQueryHelper.ShouldKeepTag(tagData, queryOptions))
            {
                results.Add(tagData);

                if (queryOptions is not null && results.Count >= queryOptions.MaximumTags)
                {
                    return false;
                }
            }

            if (RegistryTagQueryHelper.CanStopDockerHubScan(tagData,
                                                            queryOptions,
                                                            resolvedCurrentVersionTag))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parse the repositories of one listing page and append them to the result
    /// </summary>
    /// <param name="rootElement">Root element of the page</param>
    /// <param name="repositories">Repositories read so far</param>
    private static void AppendRepositoryPage(JsonElement rootElement, List<DockerHubRepositoryData> repositories)
    {
        if (rootElement.TryGetProperty("results", out var resultsElement) == false
            || resultsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var resultElement in resultsElement.EnumerateArray())
        {
            repositories.Add(ParseRepository(resultElement));
        }
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Request a pull token from the Docker Registry authentication service
    /// </summary>
    /// <param name="imageReference">Image reference to request scope for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Registry bearer token or null when the request fails</returns>
    private async Task<string?> GetRegistryTokenAsync(ImageReference imageReference, CancellationToken cancellationToken)
    {
        var (namespaceName, repositoryName) = SplitRepository(imageReference.Repository);

        var scope = $"repository:{Uri.EscapeDataString(namespaceName)}/{EscapeRepository(repositoryName)}:pull";
        var tokenUri = new Uri($"https://auth.docker.io/token?service=registry.docker.io&scope={scope}");

        using var request = new HttpRequestMessage(HttpMethod.Get, tokenUri);

        var options = _optionsMonitor.CurrentValue.DockerHub;

        if (string.IsNullOrWhiteSpace(options.UserName) == false
            && string.IsNullOrWhiteSpace(options.Pat) == false)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.UserName.Trim()}:{options.Pat.Trim()}"));

            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        using var response = await SendThrottledAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode == false)
        {
            _logger.DockerHubRegistryTokenFailed(imageReference.FullReference, (int)response.StatusCode);

            return null;
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await RemoteJsonDocument.ParseAsync(responseStream, cancellationToken).ConfigureAwait(false);

            return TryGetString(jsonDocument.RootElement, "token")
                       ?? TryGetString(jsonDocument.RootElement, "access_token");
        }
    }

    /// <summary>
    /// Fetch the image manifest and return the config blob digest
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="manifestUri">Manifest request URI</param>
    /// <param name="registryToken">Registry bearer token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Config digest or null when the manifest cannot be resolved</returns>
    private async Task<string?> GetImageConfigDigestAsync(ImageReference imageReference,
                                                          Uri manifestUri,
                                                          string registryToken,
                                                          CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, manifestUri);

        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, registryToken);
        request.Headers.Accept.ParseAdd("application/vnd.docker.distribution.manifest.list.v2+json");
        request.Headers.Accept.ParseAdd("application/vnd.oci.image.index.v1+json");
        request.Headers.Accept.ParseAdd("application/vnd.docker.distribution.manifest.v2+json");
        request.Headers.Accept.ParseAdd("application/vnd.oci.image.manifest.v1+json");

        using var response = await SendThrottledAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode == false)
        {
            _logger.DockerHubManifestFetchFailed(imageReference.FullReference, (int)response.StatusCode);

            return null;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await RemoteJsonDocument.ParseAsync(responseStream, cancellationToken).ConfigureAwait(false);

            if (string.Equals(contentType, "application/vnd.docker.distribution.manifest.list.v2+json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(contentType, "application/vnd.oci.image.index.v1+json", StringComparison.OrdinalIgnoreCase))
            {
                return await GetConfigDigestFromManifestListAsync(imageReference, jsonDocument.RootElement, registryToken, cancellationToken).ConfigureAwait(false);
            }

            if (jsonDocument.RootElement.TryGetProperty(ConfigPropertyName, out var configElement))
            {
                return TryGetString(configElement, DigestPropertyName);
            }
        }

        return null;
    }

    /// <summary>
    /// Resolve a platform-specific manifest from a multi-arch manifest list and return its config digest
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="manifestListElement">Manifest list root element</param>
    /// <param name="registryToken">Registry bearer token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Config digest or null when no suitable platform manifest is found</returns>
    private async Task<string?> GetConfigDigestFromManifestListAsync(ImageReference imageReference,
                                                                     JsonElement manifestListElement,
                                                                     string registryToken,
                                                                     CancellationToken cancellationToken)
    {
        if (manifestListElement.TryGetProperty("manifests", out var manifestsElement) == false
            || manifestsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var targetDigest = SelectManifestDigest(manifestsElement);

        if (string.IsNullOrWhiteSpace(targetDigest))
        {
            return null;
        }

        var singleManifestUri = CreateRegistryRequestUri(imageReference.Repository, ManifestsResource, targetDigest);

        if (singleManifestUri is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, singleManifestUri);

        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, registryToken);
        request.Headers.Accept.ParseAdd("application/vnd.docker.distribution.manifest.v2+json");
        request.Headers.Accept.ParseAdd("application/vnd.oci.image.manifest.v1+json");

        using var response = await SendThrottledAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode == false)
        {
            _logger.DockerHubManifestFetchFailed(imageReference.FullReference, (int)response.StatusCode);

            return null;
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await RemoteJsonDocument.ParseAsync(responseStream, cancellationToken).ConfigureAwait(false);

            if (jsonDocument.RootElement.TryGetProperty(ConfigPropertyName, out var configElement))
            {
                return TryGetString(configElement, DigestPropertyName);
            }
        }

        return null;
    }

    /// <summary>
    /// Fetch the image config blob and extract reduced configuration metadata
    /// </summary>
    /// <param name="imageReference">Image reference to fetch the blob for</param>
    /// <param name="configDigest">Config blob digest</param>
    /// <param name="registryToken">Registry bearer token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Configuration metadata or null when the blob could not be parsed</returns>
    private async Task<RegistryImageConfigurationData?> ExtractImageConfigurationAsync(ImageReference imageReference,
                                                                                       string configDigest,
                                                                                       string registryToken,
                                                                                       CancellationToken cancellationToken)
    {
        var blobUri = CreateRegistryRequestUri(imageReference.Repository, BlobsResource, configDigest);

        if (blobUri is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, blobUri);

        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, registryToken);

        using var response = await SendThrottledAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode == false)
        {
            return null;
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await RemoteJsonDocument.ParseAsync(responseStream, cancellationToken).ConfigureAwait(false);
            var rootElement = jsonDocument.RootElement;
            var environmentVariables = rootElement.TryGetProperty(ConfigPropertyName, out var configElement)
                                       && configElement.ValueKind == JsonValueKind.Object
                                       && configElement.TryGetProperty("Env", out var envElement)
                                       && envElement.ValueKind == JsonValueKind.Array
                                           ? envElement.EnumerateArray()
                                                       .Select(element => ResponseBodyExcerpt.ReplaceControlCharacters(element.GetString()))
                                                       .Where(value => string.IsNullOrWhiteSpace(value) == false)
                                                       .Cast<string>()
                                                       .ToArray()
                                           : [];
            var labels = rootElement.TryGetProperty(ConfigPropertyName, out configElement)
                         && configElement.ValueKind == JsonValueKind.Object
                         && configElement.TryGetProperty("Labels", out var labelsElement)
                         && labelsElement.ValueKind == JsonValueKind.Object
                             ? ImageConfigurationLabelReader.ReadLabels(labelsElement)
                             : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            return new RegistryImageConfigurationData
                   {
                       EnvironmentVariables = environmentVariables,
                       Labels = labels,
                       CreatedAtUtc = DateTimeOffset.TryParse(TryGetString(rootElement, "created"), CultureInfo.InvariantCulture, out var createdAtUtc) ? createdAtUtc : null,
                       OperatingSystem = ResponseBodyExcerpt.ReplaceControlCharacters(TryGetString(rootElement, "os")),
                       Architecture = ResponseBodyExcerpt.ReplaceControlCharacters(TryGetString(rootElement, "architecture")),
                   };
        }
    }

    /// <summary>
    /// Send a GET request to Docker Hub
    /// </summary>
    /// <param name="relativeUri">Relative request URI</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response</returns>
    private async Task<HttpResponseMessage> SendGetAsync(string relativeUri, CancellationToken cancellationToken)
    {
        return await SendGetAsync(new Uri(relativeUri, UriKind.Relative), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Send a GET request to an already resolved Docker Hub URI
    /// </summary>
    /// <param name="requestUri">Request URI</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response</returns>
    private async Task<HttpResponseMessage> SendGetAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        var authenticationResult = await ApplyAuthenticationAsync(request, cancellationToken).ConfigureAwait(false);

        if (authenticationResult.Status == ExternalOperationStatus.Failed)
        {
            return CreateAuthenticationFailureResponse(authenticationResult.Message);
        }

        return await SendThrottledAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Log a rejected next-page link and create the failed result of the listing
    /// </summary>
    /// <typeparam name="T">Result payload type</typeparam>
    /// <param name="operationName">Listing operation name</param>
    /// <param name="targetName">Account or repository the listing belongs to</param>
    /// <param name="listingDescription">Description of the listing for the failure message</param>
    /// <param name="currentRequestUri">URI of the request that returned the link</param>
    /// <param name="nextLink">Rejected raw link</param>
    /// <returns>Failed result</returns>
    private ExternalOperationResult<T> RejectPaginationLink<T>(string operationName,
                                                               string targetName,
                                                               string listingDescription,
                                                               Uri currentRequestUri,
                                                               string nextLink)
    {
        var rejectedOrigin = PaginationLinkGuard.DescribeOrigin(currentRequestUri, nextLink);

        _logger.DockerHubPaginationLinkRejected(operationName, targetName, rejectedOrigin);

        return ExternalOperationResult.Failed<T>(PaginationLinkGuard.CreateRejectionMessage(listingDescription, rejectedOrigin));
    }

    /// <summary>
    /// Build the absolute URL of the Docker Hub login
    /// </summary>
    /// <returns>Login URL</returns>
    private Uri CreateLoginUri()
    {
        return new Uri(GetApiBaseUri(), LoginPath);
    }

    /// <summary>
    /// Determine the origin that Docker Hub API requests and their pagination links must stay on
    /// </summary>
    /// <returns>API base URI</returns>
    private Uri GetApiBaseUri()
    {
        return _httpClient.BaseAddress ?? GetBaseUri(_optionsMonitor.CurrentValue.DockerHub);
    }

    /// <summary>
    /// Send an HTTP request while honoring the configured Docker Hub request parallelism limit
    /// </summary>
    /// <param name="request">Outbound HTTP request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response</returns>
    private async Task<HttpResponseMessage> SendThrottledAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _requestThrottle.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await _httpClient.SendAsync(request,
                                               HttpCompletionOption.ResponseHeadersRead,
                                               cancellationToken)
                                    .ConfigureAwait(false);
        }
        finally
        {
            _requestThrottle.Release();
        }
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
            return ExternalOperationResult.Succeeded<bool>(true);
        }

        if (string.IsNullOrWhiteSpace(options.UserName))
        {
            _logger.DockerHubAuthenticationUserNameMissing();

            return ExternalOperationResult.Failed<bool>("Docker Hub authentication requires 'DockerUpdateGuard:DockerHub:UserName' when a PAT is configured");
        }

        var accessTokenResult = await GetAccessTokenAsync(options.UserName.Trim(),
                                                          options.Pat.Trim(),
                                                          cancellationToken).ConfigureAwait(false);

        if (accessTokenResult.Status != ExternalOperationStatus.Succeeded
            || string.IsNullOrWhiteSpace(accessTokenResult.Data))
        {
            return ExternalOperationResult.Failed<bool>(accessTokenResult.Message ?? "Docker Hub authentication token request failed");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, accessTokenResult.Data);

        return ExternalOperationResult.Succeeded<bool>(true);
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
        var loginUri = CreateLoginUri();

        return await _accessTokenCache.GetOrRequestAsync(loginUri,
                                                         userName,
                                                         personalAccessToken,
                                                         _httpClient.Timeout,
                                                         loginToken => RequestAccessTokenOrDeferAsync(loginUri,
                                                                                                      userName,
                                                                                                      personalAccessToken,
                                                                                                      loginToken),
                                                         cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Request an access token unless a Retry-After window defers the login
    /// </summary>
    /// <param name="loginUri">Login address</param>
    /// <param name="userName">Docker Hub user name</param>
    /// <param name="personalAccessToken">Docker Hub PAT</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Access token result</returns>
    private async Task<ExternalOperationResult<string>> RequestAccessTokenOrDeferAsync(Uri loginUri,
                                                                                       string userName,
                                                                                       string personalAccessToken,
                                                                                       CancellationToken cancellationToken)
    {
        if (_loginRetryAfterRegistry.IsLoginDeferred(loginUri, userName, out var retryAt))
        {
            return ExternalOperationResult.Failed<string>(CreateLoginDeferredMessage(userName, retryAt));
        }

        return await RequestAccessTokenAsync(userName,
                                             personalAccessToken,
                                             cancellationToken).ConfigureAwait(false);
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
        using var request = new HttpRequestMessage(HttpMethod.Post, LoginPath)
                            {
                                Content = new StringContent(payload,
                                                            Encoding.UTF8,
                                                            "application/json"),
                            };
        using var response = await SendThrottledAsync(request, cancellationToken).ConfigureAwait(false);

        _loginRetryAfterRegistry.RecordLoginResponse(CreateLoginUri(), userName, response);

        if (response.IsSuccessStatusCode == false)
        {
            _logger.DockerHubRequestFailed(nameof(RequestAccessTokenAsync),
                                           userName,
                                           (int)response.StatusCode);

            return await CreateFailureResultAsync<string>(response,
                                                          $"Docker Hub authentication token request failed for '{userName}'",
                                                          cancellationToken).ConfigureAwait(false);
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                                                   .ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await RemoteJsonDocument.ParseAsync(responseStream, cancellationToken)
                                                             .ConfigureAwait(false);
            var token = TryGetString(jsonDocument.RootElement, "token");

            return string.IsNullOrWhiteSpace(token)
                       ? ExternalOperationResult.Failed<string>("Docker Hub authentication token response did not contain a token")
                       : ExternalOperationResult.Succeeded<string>(token);
        }
    }

    /// <summary>
    /// Run an operation and answer an unexpected exception as a failed result
    /// </summary>
    /// <typeparam name="T">Result value type</typeparam>
    /// <param name="operationName">Operation name for the log entry</param>
    /// <param name="targetName">Target the operation belongs to</param>
    /// <param name="failureMessage">Failure message of the failed result</param>
    /// <param name="operation">Operation to run</param>
    /// <returns>Result of the operation or a failed result</returns>
    private async Task<ExternalOperationResult<T>> ExecuteAsync<T>(string operationName,
                                                                   string targetName,
                                                                   string failureMessage,
                                                                   Func<Task<ExternalOperationResult<T>>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            _logger.DockerHubOperationFailed(exception, operationName, targetName);

            return ExternalOperationResult.Failed<T>($"{failureMessage}: {FailureReason.InvalidResponse}");
        }
    }

    /// <summary>
    /// Read every tag page of a repository
    /// </summary>
    /// <param name="repository">Repository name</param>
    /// <param name="repositoryPath">Validated repository path</param>
    /// <param name="queryOptions">Optional tag query options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tag listing result</returns>
    private async Task<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>> GetTagsCoreAsync(string repository, string repositoryPath, RegistryTagQueryOptions? queryOptions, CancellationToken cancellationToken)
    {
        var apiBaseUri = GetApiBaseUri();
        var requestUri = new Uri(apiBaseUri, $"v2/namespaces/{CreateRepositoryApiPath(repositoryPath)}/tags?page_size=100");
        var results = new List<DockerHubTagData>();
        var resolvedCurrentVersionTag = false;

        while (requestUri is not null)
        {
            using var response = await SendGetAsync(requestUri, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.DockerHubTargetNotFound(nameof(GetTagsAsync), repository);

                return ExternalOperationResult.NotFound<IReadOnlyList<DockerHubTagData>>($"Repository '{repository}' was not found in Docker Hub");
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

            string? nextLink;
            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                                                       .ConfigureAwait(false);

            await using (responseStream.ConfigureAwait(false))
            {
                using var jsonDocument = await RemoteJsonDocument.ParseAsync(responseStream, cancellationToken)
                                                                 .ConfigureAwait(false);

                nextLink = AppendTagPage(jsonDocument.RootElement,
                                         queryOptions,
                                         results,
                                         ref resolvedCurrentVersionTag)
                               ? TryGetString(jsonDocument.RootElement, "next")
                               : null;
            }

            if (string.IsNullOrWhiteSpace(nextLink))
            {
                requestUri = null;

                continue;
            }

            var nextUri = PaginationLinkGuard.ResolveNextLink(requestUri, apiBaseUri, nextLink);

            if (nextUri is null)
            {
                return RejectPaginationLink<IReadOnlyList<DockerHubTagData>>(nameof(GetTagsAsync),
                                                                             repository,
                                                                             $"Docker Hub tag listing for '{repository}'",
                                                                             requestUri,
                                                                             nextLink);
            }

            requestUri = nextUri;
        }

        return ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>(results);
    }

    #endregion // Methods

    #region IDockerHubClient

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<DockerHubAuthenticatedUserData>> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (IsSupportedRegistry(_optionsMonitor.CurrentValue.DockerHub.Registry) == false)
        {
            _logger.DockerHubRegistryUnsupported(_optionsMonitor.CurrentValue.DockerHub.Registry, nameof(GetCurrentUserAsync));

            return ExternalOperationResult.Unsupported<DockerHubAuthenticatedUserData>($"Registry '{_optionsMonitor.CurrentValue.DockerHub.Registry}' is not supported by the Docker Hub adapter");
        }

        return await ExecuteAsync(nameof(GetCurrentUserAsync),
                                  "current-user",
                                  "Docker Hub current user lookup failed",
                                  async () =>
                                  {
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
                                          using var jsonDocument = await RemoteJsonDocument.ParseAsync(responseStream, cancellationToken)
                                                                                           .ConfigureAwait(false);
                                          var userName = ResponseBodyExcerpt.ReplaceControlCharacters(TryGetString(jsonDocument.RootElement, "username"));

                                          if (string.IsNullOrWhiteSpace(userName))
                                          {
                                              return ExternalOperationResult.Failed<DockerHubAuthenticatedUserData>("Docker Hub current user response did not contain a username");
                                          }

                                          _logger.DockerHubAuthenticatedAccountResolved(userName);

                                          return ExternalOperationResult.Succeeded<DockerHubAuthenticatedUserData>(new DockerHubAuthenticatedUserData
                                                                                                                   {
                                                                                                                       UserName = userName,
                                                                                                                   });
                                      }
                                  }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<IReadOnlyList<DockerHubRepositoryData>>> GetRepositoriesAsync(string accountName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        if (IsSupportedRegistry(_optionsMonitor.CurrentValue.DockerHub.Registry) == false)
        {
            _logger.DockerHubRegistryUnsupported(_optionsMonitor.CurrentValue.DockerHub.Registry, nameof(GetRepositoriesAsync));

            return ExternalOperationResult.Unsupported<IReadOnlyList<DockerHubRepositoryData>>($"Registry '{_optionsMonitor.CurrentValue.DockerHub.Registry}' is not supported by the Docker Hub adapter");
        }

        return await ExecuteAsync(nameof(GetRepositoriesAsync),
                                  accountName,
                                  $"Docker Hub repository listing failed for '{accountName}'",
                                  async () =>
                                  {
                                      var repositories = new List<DockerHubRepositoryData>();
                                      var apiBaseUri = GetApiBaseUri();
                                      var requestUri = new Uri(apiBaseUri, $"v2/repositories/{Uri.EscapeDataString(accountName)}/?page_size=100");

                                      while (requestUri is not null)
                                      {
                                          using var response = await SendGetAsync(requestUri, cancellationToken).ConfigureAwait(false);

                                          if (response.StatusCode == HttpStatusCode.NotFound)
                                          {
                                              _logger.DockerHubTargetNotFound(nameof(GetRepositoriesAsync), accountName);

                                              return ExternalOperationResult.NotFound<IReadOnlyList<DockerHubRepositoryData>>($"Docker Hub account '{accountName}' was not found");
                                          }

                                          if (response.IsSuccessStatusCode == false)
                                          {
                                              _logger.DockerHubRequestFailed(nameof(GetRepositoriesAsync),
                                                                             accountName,
                                                                             (int)response.StatusCode);

                                              return await CreateFailureResultAsync<IReadOnlyList<DockerHubRepositoryData>>(response,
                                                                                                                            $"Docker Hub repository listing failed for '{accountName}'",
                                                                                                                            cancellationToken).ConfigureAwait(false);
                                          }

                                          string? nextLink;
                                          var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                                                                                     .ConfigureAwait(false);

                                          await using (responseStream.ConfigureAwait(false))
                                          {
                                              using var jsonDocument = await RemoteJsonDocument.ParseAsync(responseStream, cancellationToken)
                                                                                               .ConfigureAwait(false);

                                              AppendRepositoryPage(jsonDocument.RootElement, repositories);

                                              nextLink = TryGetString(jsonDocument.RootElement, "next");
                                          }

                                          if (string.IsNullOrWhiteSpace(nextLink))
                                          {
                                              requestUri = null;

                                              continue;
                                          }

                                          var nextUri = PaginationLinkGuard.ResolveNextLink(requestUri, apiBaseUri, nextLink);

                                          if (nextUri is null)
                                          {
                                              return RejectPaginationLink<IReadOnlyList<DockerHubRepositoryData>>(nameof(GetRepositoriesAsync),
                                                                                                                  accountName,
                                                                                                                  $"Docker Hub repository listing for '{accountName}'",
                                                                                                                  requestUri,
                                                                                                                  nextLink);
                                          }

                                          requestUri = nextUri;
                                      }

                                      _logger.DockerHubRepositoriesListed(accountName, repositories.Count);

                                      return ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubRepositoryData>>(repositories);
                                  }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<DockerHubRepositoryData>> GetRepositoryAsync(ImageReference imageReference, CancellationToken cancellationToken = default)
    {
        if (IsSupportedRegistry(imageReference.Registry) == false)
        {
            _logger.DockerHubRegistryUnsupported(imageReference.Registry, nameof(GetRepositoryAsync));

            return ExternalOperationResult.Unsupported<DockerHubRepositoryData>($"Registry '{imageReference.Registry}' is not supported by the Docker Hub adapter");
        }

        var repositoryPath = CreateRepositoryPath(imageReference.Repository);

        if (repositoryPath is null)
        {
            return ExternalOperationResult.NotFound<DockerHubRepositoryData>(RegistryReferenceValidator.DotSegmentMessage);
        }

        using var activity = DockerUpdateGuardTelemetry.ActivitySource.StartActivity(TelemetryActivityNames.DockerHubRequest, ActivityKind.Client);

        activity?.SetTag(TelemetryTagNames.ImageReference, imageReference.FullReference);

        return await ExecuteAsync(nameof(GetRepositoryAsync),
                                  imageReference.Repository,
                                  $"Repository metadata lookup failed for '{imageReference.Repository}'",
                                  async () =>
                                  {
                                      using var response = await SendGetAsync($"v2/namespaces/{CreateRepositoryApiPath(repositoryPath)}", cancellationToken).ConfigureAwait(false);

                                      if (response.StatusCode == HttpStatusCode.NotFound)
                                      {
                                          _logger.DockerHubTargetNotFound(nameof(GetRepositoryAsync), imageReference.Repository);

                                          return ExternalOperationResult.NotFound<DockerHubRepositoryData>($"Repository '{imageReference.Repository}' was not found in Docker Hub");
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
                                          using var jsonDocument = await RemoteJsonDocument.ParseAsync(responseStream, cancellationToken)
                                                                                           .ConfigureAwait(false);
                                          var root = jsonDocument.RootElement;
                                          var repositoryData = new DockerHubRepositoryData
                                                               {
                                                                   Registry = imageReference.Registry,
                                                                   Repository = imageReference.Repository,
                                                                   Description = ResponseBodyExcerpt.ReplaceControlCharacters(TryGetString(root, "description")),
                                                                   LastUpdatedAtUtc = TryGetDateTimeOffset(root, "last_updated"),
                                                               };

                                          return ExternalOperationResult.Succeeded<DockerHubRepositoryData>(repositoryData);
                                      }
                                  }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<DockerHubTagData>> GetTagAsync(ImageReference imageReference,
                                                                             string? operatingSystem = null,
                                                                             string? architecture = null,
                                                                             CancellationToken cancellationToken = default)
    {
        if (IsSupportedRegistry(imageReference.Registry) == false)
        {
            _logger.DockerHubRegistryUnsupported(imageReference.Registry, nameof(GetTagAsync));

            return ExternalOperationResult.Unsupported<DockerHubTagData>($"Registry '{imageReference.Registry}' is not supported by the Docker Hub adapter");
        }

        if (RegistryReferenceValidator.IsValidTag(imageReference.Tag) == false)
        {
            return ExternalOperationResult.NotFound<DockerHubTagData>(RegistryReferenceValidator.InvalidTagMessage);
        }

        var repositoryPath = CreateRepositoryPath(imageReference.Repository);

        if (repositoryPath is null)
        {
            return ExternalOperationResult.NotFound<DockerHubTagData>(RegistryReferenceValidator.DotSegmentMessage);
        }

        using var activity = DockerUpdateGuardTelemetry.ActivitySource.StartActivity(TelemetryActivityNames.DockerHubRequest, ActivityKind.Client);

        activity?.SetTag(TelemetryTagNames.ImageReference, imageReference.FullReference);

        return await ExecuteAsync(nameof(GetTagAsync),
                                  imageReference.FullReference,
                                  $"Tag metadata lookup failed for '{imageReference.FullReference}'",
                                  async () =>
                                  {
                                      using var response = await SendGetAsync($"v2/namespaces/{CreateRepositoryApiPath(repositoryPath)}/tags/{Uri.EscapeDataString(imageReference.Tag)}", cancellationToken).ConfigureAwait(false);

                                      if (response.StatusCode == HttpStatusCode.NotFound)
                                      {
                                          _logger.DockerHubTargetNotFound(nameof(GetTagAsync), imageReference.FullReference);

                                          return ExternalOperationResult.NotFound<DockerHubTagData>($"Tag '{imageReference.Tag}' was not found for '{imageReference.Repository}'");
                                      }

                                      if (response.IsSuccessStatusCode == false)
                                      {
                                          _logger.DockerHubRequestFailed(nameof(GetTagAsync),
                                                                         imageReference.FullReference,
                                                                         (int)response.StatusCode);

                                          return await CreateFailureResultAsync<DockerHubTagData>(response,
                                                                                                  $"Tag metadata lookup failed for '{imageReference.FullReference}'",
                                                                                                  cancellationToken).ConfigureAwait(false);
                                      }

                                      var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                                                                                 .ConfigureAwait(false);

                                      await using (responseStream.ConfigureAwait(false))
                                      {
                                          using var jsonDocument = await RemoteJsonDocument.ParseAsync(responseStream, cancellationToken)
                                                                                           .ConfigureAwait(false);

                                          return ExternalOperationResult.Succeeded<DockerHubTagData>(CreateTagData(jsonDocument.RootElement, imageReference.Tag));
                                      }
                                  }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>> GetTagsAsync(string registry,
                                                                                             string repository,
                                                                                             string? operatingSystem = null,
                                                                                             string? architecture = null,
                                                                                             RegistryTagQueryOptions? queryOptions = null,
                                                                                             CancellationToken cancellationToken = default)
    {
        if (IsSupportedRegistry(registry) == false)
        {
            _logger.DockerHubRegistryUnsupported(registry, nameof(GetTagsAsync));

            return ExternalOperationResult.Unsupported<IReadOnlyList<DockerHubTagData>>($"Registry '{registry}' is not supported by the Docker Hub adapter");
        }

        var repositoryPath = CreateRepositoryPath(repository);

        if (repositoryPath is null)
        {
            return ExternalOperationResult.NotFound<IReadOnlyList<DockerHubTagData>>(RegistryReferenceValidator.DotSegmentMessage);
        }

        return await ExecuteAsync(nameof(GetTagsAsync),
                                  repository,
                                  $"Tag lookup failed for '{repository}'",
                                  () => GetTagsCoreAsync(repository, repositoryPath, queryOptions, cancellationToken)).ConfigureAwait(false);
    }

    #endregion // IDockerHubClient

    #region IRegistryMetadataClient

    /// <inheritdoc/>
    public bool CanHandle(string registry)
    {
        return SupportsRegistry(registry);
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<RegistryImageConfigurationData>> GetImageConfigurationAsync(ImageReference imageReference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageReference);

        if (IsSupportedRegistry(imageReference.Registry) == false)
        {
            _logger.DockerHubRegistryUnsupported(imageReference.Registry, nameof(GetImageConfigurationAsync));

            return ExternalOperationResult.Unsupported<RegistryImageConfigurationData>($"Registry '{imageReference.Registry}' is not supported by the Docker Hub adapter");
        }

        var manifestUri = CreateRegistryRequestUri(imageReference.Repository, ManifestsResource, GetManifestReference(imageReference));

        if (manifestUri is null)
        {
            return ExternalOperationResult.NotFound<RegistryImageConfigurationData>(RegistryReferenceValidator.DotSegmentMessage);
        }

        try
        {
            var registryToken = await GetRegistryTokenAsync(imageReference, cancellationToken).ConfigureAwait(false);

            if (registryToken is null)
            {
                return ExternalOperationResult.Failed<RegistryImageConfigurationData>($"Registry token acquisition failed for '{imageReference.FullReference}'");
            }

            var configDigest = await GetImageConfigDigestAsync(imageReference, manifestUri, registryToken, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(configDigest))
            {
                return ExternalOperationResult.NotFound<RegistryImageConfigurationData>($"Config blob digest could not be resolved for '{imageReference.FullReference}'");
            }

            var imageConfiguration = await ExtractImageConfigurationAsync(imageReference,
                                                                          configDigest,
                                                                          registryToken,
                                                                          cancellationToken).ConfigureAwait(false);

            return imageConfiguration is null
                       ? ExternalOperationResult.Failed<RegistryImageConfigurationData>($"Image configuration blob could not be read for '{imageReference.FullReference}'")
                       : ExternalOperationResult.Succeeded<RegistryImageConfigurationData>(imageConfiguration);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.DockerHubImageConfigurationLookupFailed(exception, imageReference.FullReference);

            return ExternalOperationResult.Failed<RegistryImageConfigurationData>($"Image configuration lookup failed for '{imageReference.FullReference}': {FailureReason.FromException(exception, null, cancellationToken)}");
        }
    }

    #endregion // IRegistryMetadataClient

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        _requestThrottle.Dispose();
    }

    #endregion // IDisposable
}