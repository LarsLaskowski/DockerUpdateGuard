using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Helper;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Generic OCI registry metadata adapter
/// </summary>
public partial class OciRegistryClient : IRegistryMetadataClient
{
    #region Constants

    /// <summary>
    /// Maximum supported base-image depth
    /// </summary>
    private const int MaxBaseImageDepth = 5;

    /// <summary>
    /// Docker-content-digest header name
    /// </summary>
    private const string DockerContentDigestHeaderName = "Docker-Content-Digest";

    /// <summary>
    /// OCI base-image name label
    /// </summary>
    private const string OciBaseImageNameLabel = "org.opencontainers.image.base.name";

    /// <summary>
    /// OCI base-image digest label
    /// </summary>
    private const string OciBaseImageDigestLabel = "org.opencontainers.image.base.digest";

    /// <summary>
    /// Assumed bearer token lifetime when the token response omits "expires_in"
    /// </summary>
    private static readonly TimeSpan _defaultTokenLifetime = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Safety margin subtracted from a cached token's expiration to avoid using an almost-expired token
    /// </summary>
    private static readonly TimeSpan _tokenExpiryBuffer = TimeSpan.FromSeconds(5);

    #endregion // Constants

    #region Fields

    /// <summary>
    /// HTTP client
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Logger
    /// </summary>
    private readonly ILogger<OciRegistryClient> _logger;

    /// <summary>
    /// Cached bearer tokens keyed by registry authority and repository, reused across a scan
    /// </summary>
    private readonly ConcurrentDictionary<string, CachedBearerToken> _tokenCache = new(StringComparer.Ordinal);

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="httpClient">Configured HTTP client</param>
    /// <param name="logger">Logger</param>
    public OciRegistryClient(HttpClient httpClient, ILogger<OciRegistryClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Create a token request URI from an authentication challenge
    /// </summary>
    /// <param name="response">Unauthorized response</param>
    /// <param name="repository">Repository path</param>
    /// <returns>Token request URI or null</returns>
    private static Uri? CreateTokenRequestUri(HttpResponseMessage response, string repository)
    {
        var bearerChallenge = response.Headers.WwwAuthenticate.FirstOrDefault(entity => string.Equals(entity.Scheme,
                                                                                                      "Bearer",
                                                                                                      StringComparison.OrdinalIgnoreCase));

        if (bearerChallenge is null || string.IsNullOrWhiteSpace(bearerChallenge.Parameter))
        {
            return null;
        }

        var parameters = ParseAuthenticationParameters(bearerChallenge.Parameter);

        if (parameters.TryGetValue("realm", out var realm) == false
            || Uri.TryCreate(realm, UriKind.Absolute, out var tokenUri) == false)
        {
            return null;
        }

        var builder = new UriBuilder(tokenUri);
        var queryParameters = ParseQueryParameters(builder.Query);

        if (parameters.TryGetValue("service", out var service) && string.IsNullOrWhiteSpace(service) == false)
        {
            queryParameters["service"] = service;
        }

        if (parameters.TryGetValue("scope", out var scope) && string.IsNullOrWhiteSpace(scope) == false)
        {
            queryParameters["scope"] = scope;
        }
        else
        {
            queryParameters["scope"] = $"repository:{repository}:pull";
        }

        builder.Query = string.Join("&",
                                    queryParameters.Select(entity => $"{Uri.EscapeDataString(entity.Key)}={Uri.EscapeDataString(entity.Value)}"));

        return builder.Uri;
    }

    /// <summary>
    /// Parse the authentication challenge parameters
    /// </summary>
    /// <param name="parameter">Header parameter string</param>
    /// <returns>Parsed parameters</returns>
    private static Dictionary<string, string> ParseAuthenticationParameters(string parameter)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var current = new List<char>();
        var segments = new List<string>();
        var inQuotes = false;

        foreach (var character in parameter)
        {
            if (character == '"')
            {
                inQuotes = inQuotes == false;
            }

            if (character == ',' && inQuotes == false)
            {
                if (current.Count > 0)
                {
                    segments.Add(new string([.. current]));
                    current.Clear();
                }

                continue;
            }

            current.Add(character);
        }

        if (current.Count > 0)
        {
            segments.Add(new string([.. current]));
        }

        foreach (var segment in segments)
        {
            var separatorIndex = segment.IndexOf('=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = segment[..separatorIndex].Trim();
            var value = segment[(separatorIndex + 1)..].Trim().Trim('"');

            if (string.IsNullOrWhiteSpace(key) == false)
            {
                parameters[key] = value;
            }
        }

        return parameters;
    }

    /// <summary>
    /// Normalize a platform value
    /// </summary>
    /// <param name="value">Platform value</param>
    /// <returns>Normalized value</returns>
    private static string? NormalizePlatformValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
                   ? null
                   : value.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Select a manifest digest for a preferred platform
    /// </summary>
    /// <param name="manifestsElement">Manifest list element</param>
    /// <param name="operatingSystem">Preferred operating system</param>
    /// <param name="architecture">Preferred architecture</param>
    /// <returns>Resolved manifest digest or null</returns>
    private static string? SelectPlatformManifestDigest(JsonElement manifestsElement,
                                                        string? operatingSystem,
                                                        string? architecture)
    {
        var preferredOperatingSystem = NormalizePlatformValue(operatingSystem);
        var preferredArchitecture = NormalizePlatformValue(architecture);
        var fallbackDigest = default(string);

        foreach (var manifestElement in manifestsElement.EnumerateArray())
        {
            var digest = TryGetString(manifestElement, "digest");

            if (string.IsNullOrWhiteSpace(digest))
            {
                continue;
            }

            fallbackDigest ??= digest;

            if (manifestElement.TryGetProperty("platform", out var platformElement) == false)
            {
                continue;
            }

            var manifestOperatingSystem = NormalizePlatformValue(TryGetString(platformElement, "os"));
            var manifestArchitecture = NormalizePlatformValue(TryGetString(platformElement, "architecture"));

            if (string.IsNullOrWhiteSpace(preferredOperatingSystem) == false
                && string.IsNullOrWhiteSpace(preferredArchitecture) == false
                && string.Equals(manifestOperatingSystem, preferredOperatingSystem, StringComparison.OrdinalIgnoreCase)
                && string.Equals(manifestArchitecture, preferredArchitecture, StringComparison.OrdinalIgnoreCase))
            {
                return digest;
            }
        }

        if (string.IsNullOrWhiteSpace(preferredArchitecture) == false)
        {
            foreach (var manifestElement in manifestsElement.EnumerateArray())
            {
                if (manifestElement.TryGetProperty("platform", out var platformElement) == false)
                {
                    continue;
                }

                var digest = TryGetString(manifestElement, "digest");
                var manifestArchitecture = NormalizePlatformValue(TryGetString(platformElement, "architecture"));

                if (string.IsNullOrWhiteSpace(digest) == false
                    && string.Equals(manifestArchitecture, preferredArchitecture, StringComparison.OrdinalIgnoreCase))
                {
                    return digest;
                }
            }
        }

        return fallbackDigest;
    }

    /// <summary>
    /// Parse query parameters into a mutable dictionary
    /// </summary>
    /// <param name="query">URI query string</param>
    /// <returns>Parsed query parameters</returns>
    private static Dictionary<string, string> ParseQueryParameters(string query)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmedQuery = query.TrimStart('?');

        if (string.IsNullOrWhiteSpace(trimmedQuery))
        {
            return parameters;
        }

        foreach (var pair in trimmedQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            parameters[Uri.UnescapeDataString(pair[..separatorIndex])] = Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);
        }

        return parameters;
    }

    /// <summary>
    /// Determine whether a content type is a manifest list/index
    /// </summary>
    /// <param name="contentType">HTTP content type</param>
    /// <returns>True when the content type is a manifest list or OCI index</returns>
    private static bool IsManifestListContentType(string contentType)
    {
        return string.Equals(contentType,
                             "application/vnd.docker.distribution.manifest.list.v2+json",
                             StringComparison.OrdinalIgnoreCase)
               || string.Equals(contentType,
                                "application/vnd.oci.image.index.v1+json",
                                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Create a manifest URI for a registry repository and reference
    /// </summary>
    /// <param name="registry">Registry host</param>
    /// <param name="repository">Repository path</param>
    /// <param name="reference">Tag or digest</param>
    /// <returns>Manifest URI</returns>
    private static Uri CreateManifestUri(string registry, string repository, string reference)
    {
        return CreateRegistryUri(registry, $"v2/{EscapeRepository(repository)}/manifests/{Uri.EscapeDataString(reference)}");
    }

    /// <summary>
    /// Create a blob URI for a registry repository and digest
    /// </summary>
    /// <param name="registry">Registry host</param>
    /// <param name="repository">Repository path</param>
    /// <param name="digest">Blob digest</param>
    /// <returns>Blob URI</returns>
    private static Uri CreateBlobUri(string registry, string repository, string digest)
    {
        return CreateRegistryUri(registry, $"v2/{EscapeRepository(repository)}/blobs/{Uri.EscapeDataString(digest)}");
    }

    /// <summary>
    /// Create a tags endpoint URI
    /// </summary>
    /// <param name="registry">Registry host</param>
    /// <param name="repository">Repository path</param>
    /// <param name="lastTag">Optional pagination marker</param>
    /// <returns>Tags URI</returns>
    private static Uri CreateTagsUri(string registry, string repository, string? lastTag)
    {
        var relativePath = $"v2/{EscapeRepository(repository)}/tags/list?n=100";

        if (string.IsNullOrWhiteSpace(lastTag) == false)
        {
            relativePath += $"&last={Uri.EscapeDataString(lastTag)}";
        }

        return CreateRegistryUri(registry, relativePath);
    }

    /// <summary>
    /// Create an absolute registry URI
    /// </summary>
    /// <param name="registry">Registry host or base URI</param>
    /// <param name="relativePath">Relative path</param>
    /// <returns>Absolute URI</returns>
    private static Uri CreateRegistryUri(string registry, string relativePath)
    {
        var querySeparatorIndex = relativePath.IndexOf('?');
        var path = querySeparatorIndex >= 0 ? relativePath[..querySeparatorIndex] : relativePath;
        var query = querySeparatorIndex >= 0 ? relativePath[(querySeparatorIndex + 1)..] : string.Empty;

        if (Uri.TryCreate(registry, UriKind.Absolute, out var absoluteRegistryUri))
        {
            var absoluteBuilder = new UriBuilder(new Uri(EnsureTrailingSlash(absoluteRegistryUri), path))
                                  {
                                      Query = query,
                                  };

            return absoluteBuilder.Uri;
        }

        var registryBuilder = new UriBuilder(Uri.UriSchemeHttps, registry)
                              {
                                  Path = path,
                                  Query = query,
                              };

        return registryBuilder.Uri;
    }

    /// <summary>
    /// Ensure a base URI ends with a trailing slash
    /// </summary>
    /// <param name="uri">Original URI</param>
    /// <returns>Normalized URI</returns>
    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var builder = new UriBuilder(uri);

        if (builder.Path.EndsWith('/') == false)
        {
            builder.Path = $"{builder.Path.TrimEnd('/')}/";
        }

        return builder.Uri;
    }

    /// <summary>
    /// Escape a repository path for registry APIs
    /// </summary>
    /// <param name="repository">Repository path</param>
    /// <returns>Escaped repository path</returns>
    private static string EscapeRepository(string repository)
    {
        return string.Join('/',
                           repository.Split('/',
                                            StringSplitOptions.RemoveEmptyEntries)
                                     .Select(Uri.EscapeDataString));
    }

    /// <summary>
    /// Normalize a registry value to a stable host representation
    /// </summary>
    /// <param name="registry">Raw registry value</param>
    /// <returns>Normalized registry value</returns>
    private static string NormalizeRegistry(string registry)
    {
        if (Uri.TryCreate(registry, UriKind.Absolute, out var absoluteRegistryUri))
        {
            return absoluteRegistryUri.IsDefaultPort
                       ? absoluteRegistryUri.Host.ToLowerInvariant()
                       : $"{absoluteRegistryUri.Host.ToLowerInvariant()}:{absoluteRegistryUri.Port}";
        }

        return registry.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Read the digest header from a manifest response
    /// </summary>
    /// <param name="response">HTTP response</param>
    /// <returns>Resolved content digest or null</returns>
    private static string? GetContentDigest(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues(DockerContentDigestHeaderName, out var values)
                   ? values.FirstOrDefault()
                   : null;
    }

    /// <summary>
    /// Build the bearer-token cache key for a registry authority and repository
    /// </summary>
    /// <param name="requestUri">Request URI</param>
    /// <param name="repository">Repository path</param>
    /// <returns>Token cache key</returns>
    private static string GetTokenCacheKey(Uri requestUri, string repository)
    {
        return $"{requestUri.Authority}|{repository}";
    }

    /// <summary>
    /// Resolve the effective expiration for a bearer token response
    /// </summary>
    /// <param name="rootElement">Token response root element</param>
    /// <returns>Expiration timestamp, reduced by a safety margin</returns>
    private static DateTimeOffset GetTokenExpirationUtc(JsonElement rootElement)
    {
        var issuedAtValue = TryGetString(rootElement, "issued_at");
        var issuedAtUtc = DateTimeOffset.TryParse(issuedAtValue, out var parsedIssuedAtUtc) ? parsedIssuedAtUtc : DateTimeOffset.UtcNow;
        var expiresInSeconds = rootElement.TryGetProperty("expires_in", out var expiresInElement) && expiresInElement.TryGetInt32(out var parsedExpiresInSeconds)
                                   ? parsedExpiresInSeconds
                                   : (int)_defaultTokenLifetime.TotalSeconds;

        return issuedAtUtc.AddSeconds(expiresInSeconds) - _tokenExpiryBuffer;
    }

    /// <summary>
    /// Read an optional string property from a JSON element
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <param name="propertyName">Property name</param>
    /// <returns>String value or null</returns>
    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var propertyElement) && propertyElement.ValueKind == JsonValueKind.String
                   ? propertyElement.GetString()
                   : null;
    }

    /// <summary>
    /// Read an optional environment variable list from a config blob
    /// </summary>
    /// <param name="rootElement">Config blob root element</param>
    /// <returns>Environment variables</returns>
    private static IReadOnlyList<string> TryReadEnvironmentVariables(JsonElement rootElement)
    {
        if (rootElement.TryGetProperty("config", out var configElement) == false
            || configElement.ValueKind != JsonValueKind.Object
            || configElement.TryGetProperty("Env", out var envElement) == false
            || envElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return envElement.EnumerateArray()
                         .Select(element => element.GetString())
                         .Where(value => string.IsNullOrWhiteSpace(value) == false)
                         .Cast<string>()
                         .ToArray();
    }

    /// <summary>
    /// Read optional labels from a config blob
    /// </summary>
    /// <param name="rootElement">Config blob root element</param>
    /// <returns>Labels</returns>
    private static IReadOnlyDictionary<string, string> TryReadLabels(JsonElement rootElement)
    {
        if (rootElement.TryGetProperty("config", out var configElement) == false
            || configElement.ValueKind != JsonValueKind.Object
            || configElement.TryGetProperty("Labels", out var labelsElement) == false
            || labelsElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return labelsElement.EnumerateObject()
                            .Where(property => property.Value.ValueKind == JsonValueKind.String)
                            .ToDictionary(property => property.Name,
                                          property => property.Value.GetString() ?? string.Empty,
                                          StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parse reduced registry image configuration metadata
    /// </summary>
    /// <param name="rootElement">Config blob root element</param>
    /// <returns>Parsed configuration metadata</returns>
    private static RegistryImageConfigurationData ParseImageConfiguration(JsonElement rootElement)
    {
        return new RegistryImageConfigurationData
               {
                   EnvironmentVariables = TryReadEnvironmentVariables(rootElement),
                   Labels = TryReadLabels(rootElement),
                   CreatedAtUtc = TryReadCreatedAtUtc(rootElement),
                   OperatingSystem = TryGetString(rootElement, "os"),
                   Architecture = TryGetString(rootElement, "architecture"),
               };
    }

    /// <summary>
    /// Read the created timestamp from a manifest or config blob
    /// </summary>
    /// <param name="element">JSON root element</param>
    /// <returns>Created timestamp or null</returns>
    private static DateTimeOffset? TryReadCreatedAtUtc(JsonElement element)
    {
        var createdValue = TryGetString(element, "created");

        return DateTimeOffset.TryParse(createdValue, out var createdAtUtc) ? createdAtUtc : null;
    }

    /// <summary>
    /// Determine the next tags page URI
    /// </summary>
    /// <param name="response">Current response</param>
    /// <param name="registry">Registry host</param>
    /// <param name="repository">Repository path</param>
    /// <param name="tags">Accumulated tags</param>
    /// <returns>Next page URI or null</returns>
    private static Uri? GetNextTagsUri(HttpResponseMessage response,
                                       string registry,
                                       string repository,
                                       IReadOnlyList<DockerHubTagData> tags)
    {
        if (response.Headers.TryGetValues("Link", out var linkValues))
        {
            var nextLink = linkValues.SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                                     .Select(value => value.Trim())
                                     .FirstOrDefault(value => value.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(nextLink) == false)
            {
                var start = nextLink.IndexOf('<');
                var end = nextLink.IndexOf('>');

                if (start >= 0 && end > start)
                {
                    var linkTarget = nextLink[(start + 1)..end];

                    if (Uri.TryCreate(linkTarget, UriKind.Absolute, out var absoluteUri))
                    {
                        return absoluteUri;
                    }

                    if (Uri.TryCreate(CreateRegistryUri(registry, string.Empty), linkTarget, out var relativeUri))
                    {
                        return relativeUri;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Create a structured failure result from an HTTP response
    /// </summary>
    /// <typeparam name="T">Result payload type</typeparam>
    /// <param name="response">HTTP response</param>
    /// <param name="messagePrefix">Message prefix</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Failure result</returns>
    private static async Task<ExternalOperationResult<T>> CreateFailureResultAsync<T>(HttpResponseMessage response,
                                                                                      string messagePrefix,
                                                                                      CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return ExternalOperationResult<T>.Failed($"{messagePrefix}: {(int)response.StatusCode} {response.ReasonPhrase}{(string.IsNullOrWhiteSpace(body) ? string.Empty : $" - {body}")}");
    }

    /// <summary>
    /// Create a failure-shaped response when token exchange could not be completed
    /// </summary>
    /// <param name="message">Failure message</param>
    /// <returns>Failure response</returns>
    private static HttpResponseMessage CreateAuthenticationFailureResponse(string message)
    {
        return new HttpResponseMessage(HttpStatusCode.Unauthorized)
               {
                   Content = new StringContent(message),
               };
    }

    #endregion // Static methods

    #region Methods

    /// <inheritdoc/>
    public bool CanHandle(string registry)
    {
        return string.IsNullOrWhiteSpace(registry) == false
               && DockerHubClient.SupportsRegistry(registry) == false;
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<DockerHubTagData>> GetTagAsync(ImageReference imageReference,
                                                                             CancellationToken cancellationToken = default,
                                                                             string? operatingSystem = null,
                                                                             string? architecture = null)
    {
        ArgumentNullException.ThrowIfNull(imageReference);

        if (CanHandle(imageReference.Registry) == false)
        {
            return ExternalOperationResult<DockerHubTagData>.Unsupported($"Registry '{imageReference.Registry}' is not supported by the OCI registry adapter");
        }

        var manifestResult = await GetManifestMetadataAsync(imageReference,
                                                            imageReference.Tag,
                                                            cancellationToken,
                                                            operatingSystem,
                                                            architecture).ConfigureAwait(false);

        if (manifestResult.Status != ExternalOperationStatus.Succeeded || manifestResult.Data is null)
        {
            return manifestResult.Status switch
                   {
                       ExternalOperationStatus.Unsupported => ExternalOperationResult<DockerHubTagData>.Unsupported(manifestResult.Message ?? "Registry lookup is unsupported"),
                       ExternalOperationStatus.NotFound => ExternalOperationResult<DockerHubTagData>.NotFound(manifestResult.Message ?? "Registry tag was not found"),
                       ExternalOperationStatus.NotConfigured => ExternalOperationResult<DockerHubTagData>.NotConfigured(manifestResult.Message ?? "Registry lookup is not configured"),
                       ExternalOperationStatus.Unknown => ExternalOperationResult<DockerHubTagData>.Unknown(manifestResult.Message ?? "Registry lookup status is unknown"),
                       _ => ExternalOperationResult<DockerHubTagData>.Failed(manifestResult.Message ?? "Registry tag lookup failed"),
                   };
        }

        return ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                   {
                                                                       Tag = imageReference.Tag,
                                                                       Digest = manifestResult.Data.Digest,
                                                                       PublishedAtUtc = manifestResult.Data.PublishedAtUtc,
                                                                   });
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>> GetTagsAsync(string registry,
                                                                                             string repository,
                                                                                             CancellationToken cancellationToken = default,
                                                                                             string? operatingSystem = null,
                                                                                             string? architecture = null,
                                                                                             RegistryTagQueryOptions? queryOptions = null)
    {
        if (CanHandle(registry) == false)
        {
            return ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Unsupported($"Registry '{registry}' is not supported by the OCI registry adapter");
        }

        var normalizedRegistry = NormalizeRegistry(registry);
        var requestUri = CreateTagsUri(normalizedRegistry, repository, lastTag: null);
        var tags = new List<DockerHubTagData>();
        var inspectedTagCount = 0;

        while (requestUri is not null)
        {
            using var response = await SendRegistryRequestAsync(requestUri,
                                                                repository,
                                                                HttpMethod.Get,
                                                                configureRequest: null,
                                                                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.NotFound($"Repository '{repository}' was not found in registry '{normalizedRegistry}'");
            }

            if (response.IsSuccessStatusCode == false)
            {
                return await CreateFailureResultAsync<IReadOnlyList<DockerHubTagData>>(response,
                                                                                       $"Tag lookup failed for '{normalizedRegistry}/{repository}'",
                                                                                       cancellationToken).ConfigureAwait(false);
            }

            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            await using (responseStream.ConfigureAwait(false))
            {
                using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (jsonDocument.RootElement.TryGetProperty("tags", out var tagsElement)
                    && tagsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tagElement in tagsElement.EnumerateArray())
                    {
                        var tagName = tagElement.GetString();

                        if (string.IsNullOrWhiteSpace(tagName))
                        {
                            continue;
                        }

                        if (RegistryTagQueryHelper.ShouldInspectTagName(tagName, queryOptions) == false)
                        {
                            continue;
                        }

                        if (queryOptions is not null && inspectedTagCount >= queryOptions.MaximumTags)
                        {
                            requestUri = null;

                            break;
                        }

                        var tagReference = new ImageReference
                                           {
                                               Registry = normalizedRegistry,
                                               Repository = repository,
                                               Tag = tagName,
                                           };
                        var tagResult = await GetTagAsync(tagReference,
                                                          cancellationToken,
                                                          operatingSystem,
                                                          architecture).ConfigureAwait(false);

                        inspectedTagCount++;

                        if (tagResult.Status == ExternalOperationStatus.Succeeded && tagResult.Data is not null)
                        {
                            if (RegistryTagQueryHelper.ShouldKeepTag(tagResult.Data, queryOptions))
                            {
                                tags.Add(tagResult.Data);
                            }
                        }
                        else
                        {
                            tags.Add(new DockerHubTagData
                                     {
                                         Tag = tagName,
                                     });
                        }
                    }
                }
            }

            if (requestUri is not null)
            {
                requestUri = GetNextTagsUri(response, normalizedRegistry, repository, tags);
            }
        }

        return ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded(tags);
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>> ResolveBaseImagesAsync(ImageReference imageReference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageReference);

        if (CanHandle(imageReference.Registry) == false)
        {
            return ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Unsupported($"Registry '{imageReference.Registry}' is not supported by the OCI registry adapter");
        }

        var results = new List<BaseImageDescriptor>();

        try
        {
            await ResolveBaseImageChainAsync(imageReference, results, depth: 1, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception,
                               "OCI base image chain resolution failed for {ImageReference}",
                               imageReference.FullReference);

            return ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Failed($"Base image chain resolution failed for '{imageReference.FullReference}': {exception.Message}");
        }

        return ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Succeeded(results);
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<RegistryImageConfigurationData>> GetImageConfigurationAsync(ImageReference imageReference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageReference);

        if (CanHandle(imageReference.Registry) == false)
        {
            return ExternalOperationResult<RegistryImageConfigurationData>.Unsupported($"Registry '{imageReference.Registry}' is not supported by the OCI registry adapter");
        }

        var manifestResult = await GetManifestMetadataAsync(imageReference,
                                                            string.IsNullOrWhiteSpace(imageReference.Digest) ? imageReference.Tag : imageReference.Digest,
                                                            cancellationToken).ConfigureAwait(false);

        if (manifestResult.Status != ExternalOperationStatus.Succeeded || manifestResult.Data is null)
        {
            return manifestResult.Status switch
                   {
                       ExternalOperationStatus.Unsupported => ExternalOperationResult<RegistryImageConfigurationData>.Unsupported(manifestResult.Message ?? "Registry lookup is unsupported"),
                       ExternalOperationStatus.NotFound => ExternalOperationResult<RegistryImageConfigurationData>.NotFound(manifestResult.Message ?? "Registry image was not found"),
                       ExternalOperationStatus.NotConfigured => ExternalOperationResult<RegistryImageConfigurationData>.NotConfigured(manifestResult.Message ?? "Registry lookup is not configured"),
                       ExternalOperationStatus.Unknown => ExternalOperationResult<RegistryImageConfigurationData>.Unknown(manifestResult.Message ?? "Registry lookup status is unknown"),
                       _ => ExternalOperationResult<RegistryImageConfigurationData>.Failed(manifestResult.Message ?? "Registry image configuration lookup failed"),
                   };
        }

        if (string.IsNullOrWhiteSpace(manifestResult.Data.ConfigDigest))
        {
            return ExternalOperationResult<RegistryImageConfigurationData>.NotFound($"Registry image '{imageReference.FullReference}' did not expose a config digest");
        }

        var blobUri = CreateBlobUri(imageReference.Registry, imageReference.Repository, manifestResult.Data.ConfigDigest);

        using var response = await SendRegistryRequestAsync(blobUri,
                                                            imageReference.Repository,
                                                            HttpMethod.Get,
                                                            configureRequest: null,
                                                            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ExternalOperationResult<RegistryImageConfigurationData>.NotFound($"Config blob '{manifestResult.Data.ConfigDigest}' was not found for '{imageReference.FullReference}'");
        }

        if (response.IsSuccessStatusCode == false)
        {
            return await CreateFailureResultAsync<RegistryImageConfigurationData>(response,
                                                                                  $"Registry image configuration lookup failed for '{imageReference.FullReference}'",
                                                                                  cancellationToken).ConfigureAwait(false);
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            return ExternalOperationResult<RegistryImageConfigurationData>.Succeeded(ParseImageConfiguration(jsonDocument.RootElement));
        }
    }

    /// <summary>
    /// Resolve the base image chain recursively
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="results">Result collection</param>
    /// <param name="depth">Current depth</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task ResolveBaseImageChainAsync(ImageReference imageReference,
                                                  List<BaseImageDescriptor> results,
                                                  int depth,
                                                  CancellationToken cancellationToken)
    {
        if (depth > MaxBaseImageDepth)
        {
            return;
        }

        var manifestResult = await GetManifestMetadataAsync(imageReference,
                                                            string.IsNullOrWhiteSpace(imageReference.Digest) ? imageReference.Tag : imageReference.Digest,
                                                            cancellationToken).ConfigureAwait(false);

        if (manifestResult.Status != ExternalOperationStatus.Succeeded
            || manifestResult.Data is null
            || string.IsNullOrWhiteSpace(manifestResult.Data.ConfigDigest))
        {
            return;
        }

        var baseImageReference = await ExtractBaseImageFromConfigAsync(imageReference,
                                                                       manifestResult.Data.ConfigDigest,
                                                                       cancellationToken).ConfigureAwait(false);

        if (baseImageReference is null)
        {
            return;
        }

        results.Add(new BaseImageDescriptor
                    {
                        Registry = baseImageReference.Registry,
                        Repository = baseImageReference.Repository,
                        Tag = baseImageReference.Tag,
                        Digest = baseImageReference.Digest,
                        Depth = depth,
                        SourceReference = imageReference.FullReference,
                    });

        await ResolveBaseImageChainAsync(baseImageReference, results, depth + 1, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetch manifest metadata for a reference
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="reference">Tag or digest reference</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="operatingSystem">Preferred operating system</param>
    /// <param name="architecture">Preferred architecture</param>
    /// <returns>Manifest metadata result</returns>
    private async Task<ExternalOperationResult<ManifestMetadata>> GetManifestMetadataAsync(ImageReference imageReference,
                                                                                           string reference,
                                                                                           CancellationToken cancellationToken,
                                                                                           string? operatingSystem = null,
                                                                                           string? architecture = null)
    {
        using var response = await SendManifestRequestAsync(imageReference,
                                                            reference,
                                                            HttpMethod.Get,
                                                            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ExternalOperationResult<ManifestMetadata>.NotFound($"Reference '{reference}' was not found for '{imageReference.Registry}/{imageReference.Repository}'");
        }

        if (response.IsSuccessStatusCode == false)
        {
            return await CreateFailureResultAsync<ManifestMetadata>(response,
                                                                    $"Manifest lookup failed for '{imageReference.Registry}/{imageReference.Repository}:{reference}'",
                                                                    cancellationToken).ConfigureAwait(false);
        }

        var digest = GetContentDigest(response);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (IsManifestListContentType(contentType))
            {
                return await GetConfigDigestFromManifestListAsync(imageReference,
                                                                  digest,
                                                                  jsonDocument.RootElement,
                                                                  cancellationToken,
                                                                  operatingSystem,
                                                                  architecture).ConfigureAwait(false);
            }

            return ExternalOperationResult<ManifestMetadata>.Succeeded(new ManifestMetadata
                                                                       {
                                                                           Digest = digest,
                                                                           ConfigDigest = jsonDocument.RootElement.TryGetProperty("config", out var configElement)
                                                                                              ? TryGetString(configElement, "digest")
                                                                                              : null,
                                                                           PublishedAtUtc = TryReadCreatedAtUtc(jsonDocument.RootElement),
                                                                       });
        }
    }

    /// <summary>
    /// Resolve config metadata from a multi-arch manifest list
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="digest">Resolved digest</param>
    /// <param name="manifestListElement">Manifest list element</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="operatingSystem">Preferred operating system</param>
    /// <param name="architecture">Preferred architecture</param>
    /// <returns>Manifest metadata result</returns>
    private async Task<ExternalOperationResult<ManifestMetadata>> GetConfigDigestFromManifestListAsync(ImageReference imageReference,
                                                                                                       string? digest,
                                                                                                       JsonElement manifestListElement,
                                                                                                       CancellationToken cancellationToken,
                                                                                                       string? operatingSystem = null,
                                                                                                       string? architecture = null)
    {
        if (manifestListElement.TryGetProperty("manifests", out var manifestsElement) == false
            || manifestsElement.ValueKind != JsonValueKind.Array)
        {
            return ExternalOperationResult<ManifestMetadata>.Failed($"Manifest list for '{imageReference.FullReference}' did not contain any platform manifests");
        }

        var targetDigest = SelectPlatformManifestDigest(manifestsElement, operatingSystem, architecture);

        if (string.IsNullOrWhiteSpace(targetDigest))
        {
            return ExternalOperationResult<ManifestMetadata>.Failed($"Manifest list for '{imageReference.FullReference}' did not expose a target manifest digest");
        }

        var platformManifestResult = await GetManifestMetadataAsync(imageReference,
                                                                    targetDigest,
                                                                    cancellationToken).ConfigureAwait(false);

        if (platformManifestResult.Status != ExternalOperationStatus.Succeeded || platformManifestResult.Data is null)
        {
            return platformManifestResult;
        }

        platformManifestResult.Data.Digest = string.IsNullOrWhiteSpace(digest) ? platformManifestResult.Data.Digest : digest;

        return platformManifestResult;
    }

    /// <summary>
    /// Fetch the image config blob and extract the OCI base image reference
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="configDigest">Config blob digest</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed base image reference or null</returns>
    private async Task<ImageReference?> ExtractBaseImageFromConfigAsync(ImageReference imageReference,
                                                                        string configDigest,
                                                                        CancellationToken cancellationToken)
    {
        var blobUri = CreateBlobUri(imageReference.Registry, imageReference.Repository, configDigest);

        using var response = await SendRegistryRequestAsync(blobUri,
                                                            imageReference.Repository,
                                                            HttpMethod.Get,
                                                            configureRequest: null,
                                                            cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode == false)
        {
            return null;
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (jsonDocument.RootElement.TryGetProperty("config", out var configElement) == false
                || configElement.TryGetProperty("Labels", out var labelsElement) == false
                || labelsElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var baseImageName = TryGetString(labelsElement, OciBaseImageNameLabel);

            if (string.IsNullOrWhiteSpace(baseImageName))
            {
                return null;
            }

            var parsedReference = new ImageReferenceParser().Parse(baseImageName);
            var baseImageDigest = TryGetString(labelsElement, OciBaseImageDigestLabel);

            if (string.IsNullOrWhiteSpace(parsedReference.Digest)
                && string.IsNullOrWhiteSpace(baseImageDigest) == false)
            {
                parsedReference.Digest = ImageReferenceParser.NormalizeDigest(baseImageDigest);
            }

            return parsedReference;
        }
    }

    /// <summary>
    /// Send a manifest request with the OCI accept headers
    /// </summary>
    /// <param name="imageReference">Image reference</param>
    /// <param name="reference">Tag or digest reference</param>
    /// <param name="method">HTTP method</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response</returns>
    private Task<HttpResponseMessage> SendManifestRequestAsync(ImageReference imageReference,
                                                               string reference,
                                                               HttpMethod method,
                                                               CancellationToken cancellationToken)
    {
        var manifestUri = CreateManifestUri(imageReference.Registry, imageReference.Repository, reference);

        return SendRegistryRequestAsync(manifestUri,
                                        imageReference.Repository,
                                        method,
                                        request =>
                                        {
                                            request.Headers.Accept.ParseAdd("application/vnd.docker.distribution.manifest.list.v2+json");
                                            request.Headers.Accept.ParseAdd("application/vnd.oci.image.index.v1+json");
                                            request.Headers.Accept.ParseAdd("application/vnd.docker.distribution.manifest.v2+json");
                                            request.Headers.Accept.ParseAdd("application/vnd.oci.image.manifest.v1+json");
                                        },
                                        cancellationToken);
    }

    /// <summary>
    /// Send a registry request and retry it with a bearer token challenge when required
    /// </summary>
    /// <param name="requestUri">Request URI</param>
    /// <param name="repository">Repository path</param>
    /// <param name="method">HTTP method</param>
    /// <param name="configureRequest">Optional request customization</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response</returns>
    private async Task<HttpResponseMessage> SendRegistryRequestAsync(Uri requestUri,
                                                                     string repository,
                                                                     HttpMethod method,
                                                                     Action<HttpRequestMessage>? configureRequest,
                                                                     CancellationToken cancellationToken)
    {
        var cacheKey = GetTokenCacheKey(requestUri, repository);

        if (_tokenCache.TryGetValue(cacheKey, out var cachedToken) && cachedToken.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            var cachedResponse = await SendCoreAsync(requestUri,
                                                     method,
                                                     new AuthenticationHeaderValue("Bearer", cachedToken.Token),
                                                     configureRequest,
                                                     cancellationToken).ConfigureAwait(false);

            if (cachedResponse.StatusCode != HttpStatusCode.Unauthorized)
            {
                return cachedResponse;
            }

            _tokenCache.TryRemove(cacheKey, out _);
            cachedResponse.Dispose();
        }

        var response = await SendCoreAsync(requestUri, method, authorization: null, configureRequest, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        var tokenRequestUri = CreateTokenRequestUri(response, repository);

        if (tokenRequestUri is null)
        {
            return response;
        }

        response.Dispose();

        var tokenResult = await GetBearerTokenAsync(tokenRequestUri, cancellationToken).ConfigureAwait(false);

        if (tokenResult.Status != ExternalOperationStatus.Succeeded || tokenResult.Data is null)
        {
            return CreateAuthenticationFailureResponse(tokenResult.Message ?? $"Registry token lookup failed for '{repository}'");
        }

        _tokenCache[cacheKey] = tokenResult.Data;

        return await SendCoreAsync(requestUri,
                                   method,
                                   new AuthenticationHeaderValue("Bearer", tokenResult.Data.Token),
                                   configureRequest,
                                   cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Send a raw HTTP request
    /// </summary>
    /// <param name="requestUri">Request URI</param>
    /// <param name="method">HTTP method</param>
    /// <param name="authorization">Optional authorization header</param>
    /// <param name="configureRequest">Optional request customization</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response</returns>
    private async Task<HttpResponseMessage> SendCoreAsync(Uri requestUri,
                                                          HttpMethod method,
                                                          AuthenticationHeaderValue? authorization,
                                                          Action<HttpRequestMessage>? configureRequest,
                                                          CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, requestUri);

        if (authorization is not null)
        {
            request.Headers.Authorization = authorization;
        }

        configureRequest?.Invoke(request);

        return await _httpClient.SendAsync(request,
                                           HttpCompletionOption.ResponseHeadersRead,
                                           cancellationToken)
                                .ConfigureAwait(false);
    }

    /// <summary>
    /// Request a bearer token from an OCI registry challenge endpoint
    /// </summary>
    /// <param name="tokenRequestUri">Token request URI</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Bearer token result</returns>
    private async Task<ExternalOperationResult<CachedBearerToken>> GetBearerTokenAsync(Uri tokenRequestUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(tokenRequestUri,
                                                        HttpCompletionOption.ResponseHeadersRead,
                                                        cancellationToken)
                                              .ConfigureAwait(false);

        if (response.IsSuccessStatusCode == false)
        {
            return await CreateFailureResultAsync<CachedBearerToken>(response,
                                                                     $"Registry token request failed for '{tokenRequestUri}'",
                                                                     cancellationToken).ConfigureAwait(false);
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var token = TryGetString(jsonDocument.RootElement, "token")
                            ?? TryGetString(jsonDocument.RootElement, "access_token");

            if (string.IsNullOrWhiteSpace(token))
            {
                return ExternalOperationResult<CachedBearerToken>.Failed($"Registry token response for '{tokenRequestUri}' did not contain a token");
            }

            return ExternalOperationResult<CachedBearerToken>.Succeeded(new CachedBearerToken
                                                                        {
                                                                            Token = token,
                                                                            ExpiresAtUtc = GetTokenExpirationUtc(jsonDocument.RootElement),
                                                                        });
        }
    }

    #endregion // Methods
}