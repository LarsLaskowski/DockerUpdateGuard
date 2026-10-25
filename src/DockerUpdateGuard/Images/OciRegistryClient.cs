using System.Collections.Concurrent;
using System.Globalization;
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
    /// Name of the digest property in registry payloads
    /// </summary>
    private const string DigestPropertyName = "digest";

    /// <summary>
    /// Name of the config property in registry payloads
    /// </summary>
    private const string ConfigPropertyName = "config";

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
        var exactPlatformDigest = SelectExactPlatformManifestDigest(manifestsElement,
                                                                    preferredOperatingSystem,
                                                                    preferredArchitecture);

        if (exactPlatformDigest is not null)
        {
            return exactPlatformDigest;
        }

        if (string.IsNullOrWhiteSpace(preferredArchitecture) == false)
        {
            var architectureDigest = SelectArchitectureManifestDigest(manifestsElement, preferredArchitecture);

            if (architectureDigest is not null)
            {
                return architectureDigest;
            }
        }

        return SelectFirstManifestDigest(manifestsElement);
    }

    /// <summary>
    /// Select the manifest digest matching both the preferred operating system and architecture
    /// </summary>
    /// <param name="manifestsElement">Manifest list element</param>
    /// <param name="preferredOperatingSystem">Normalized preferred operating system</param>
    /// <param name="preferredArchitecture">Normalized preferred architecture</param>
    /// <returns>Matching manifest digest or null</returns>
    private static string? SelectExactPlatformManifestDigest(JsonElement manifestsElement,
                                                             string? preferredOperatingSystem,
                                                             string? preferredArchitecture)
    {
        if (string.IsNullOrWhiteSpace(preferredOperatingSystem)
            || string.IsNullOrWhiteSpace(preferredArchitecture))
        {
            return null;
        }

        foreach (var manifestElement in manifestsElement.EnumerateArray())
        {
            var digest = TryGetString(manifestElement, DigestPropertyName);

            if (string.IsNullOrWhiteSpace(digest)
                || manifestElement.TryGetProperty("platform", out var platformElement) == false)
            {
                continue;
            }

            var manifestOperatingSystem = NormalizePlatformValue(TryGetString(platformElement, "os"));
            var manifestArchitecture = NormalizePlatformValue(TryGetString(platformElement, "architecture"));

            if (string.Equals(manifestOperatingSystem, preferredOperatingSystem, StringComparison.OrdinalIgnoreCase)
                && string.Equals(manifestArchitecture, preferredArchitecture, StringComparison.OrdinalIgnoreCase))
            {
                return digest;
            }
        }

        return null;
    }

    /// <summary>
    /// Select the manifest digest matching the preferred architecture regardless of the operating system
    /// </summary>
    /// <param name="manifestsElement">Manifest list element</param>
    /// <param name="preferredArchitecture">Normalized preferred architecture</param>
    /// <returns>Matching manifest digest or null</returns>
    private static string? SelectArchitectureManifestDigest(JsonElement manifestsElement, string preferredArchitecture)
    {
        foreach (var manifestElement in manifestsElement.EnumerateArray())
        {
            if (manifestElement.TryGetProperty("platform", out var platformElement) == false)
            {
                continue;
            }

            var digest = TryGetString(manifestElement, DigestPropertyName);
            var manifestArchitecture = NormalizePlatformValue(TryGetString(platformElement, "architecture"));

            if (string.IsNullOrWhiteSpace(digest) == false
                && string.Equals(manifestArchitecture, preferredArchitecture, StringComparison.OrdinalIgnoreCase))
            {
                return digest;
            }
        }

        return null;
    }

    /// <summary>
    /// Select the first manifest digest of a manifest list
    /// </summary>
    /// <param name="manifestsElement">Manifest list element</param>
    /// <returns>First manifest digest or null</returns>
    private static string? SelectFirstManifestDigest(JsonElement manifestsElement)
    {
        foreach (var manifestElement in manifestsElement.EnumerateArray())
        {
            var digest = TryGetString(manifestElement, DigestPropertyName);

            if (string.IsNullOrWhiteSpace(digest) == false)
            {
                return digest;
            }
        }

        return null;
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
    /// <param name="refusalMessage">Message of the refusal, empty when the URI was created</param>
    /// <returns>Manifest URI or null when the repository or reference has a dot segment or the registry is not a valid host</returns>
    private static Uri? CreateManifestUri(string registry, string repository, string reference, out string refusalMessage)
    {
        if (RegistryReferenceValidator.HasDotSegment(repository)
            || RegistryReferenceValidator.HasDotSegment(reference))
        {
            refusalMessage = RegistryReferenceValidator.DotSegmentMessage;

            return null;
        }

        var manifestUri = CreateRegistryUri(registry, $"v2/{EscapeRepository(repository)}/manifests/{Uri.EscapeDataString(reference)}");

        refusalMessage = manifestUri is null ? RegistryReferenceValidator.InvalidRegistryMessage : string.Empty;

        return manifestUri;
    }

    /// <summary>
    /// Create a blob URI for a registry repository and digest
    /// </summary>
    /// <param name="registry">Registry host</param>
    /// <param name="repository">Repository path</param>
    /// <param name="digest">Blob digest</param>
    /// <param name="refusalMessage">Message of the refusal, empty when the URI was created</param>
    /// <returns>Blob URI or null when the repository or digest has a dot segment or the registry is not a valid host</returns>
    private static Uri? CreateBlobUri(string registry, string repository, string digest, out string refusalMessage)
    {
        if (RegistryReferenceValidator.HasDotSegment(repository)
            || RegistryReferenceValidator.HasDotSegment(digest))
        {
            refusalMessage = RegistryReferenceValidator.DotSegmentMessage;

            return null;
        }

        var blobUri = CreateRegistryUri(registry, $"v2/{EscapeRepository(repository)}/blobs/{Uri.EscapeDataString(digest)}");

        refusalMessage = blobUri is null ? RegistryReferenceValidator.InvalidRegistryMessage : string.Empty;

        return blobUri;
    }

    /// <summary>
    /// Create a tags endpoint URI
    /// </summary>
    /// <param name="registry">Registry host</param>
    /// <param name="repository">Repository path</param>
    /// <param name="lastTag">Optional pagination marker</param>
    /// <param name="refusalMessage">Message of the refusal, empty when the URI was created</param>
    /// <returns>Tags URI or null when the repository has a dot segment or the registry is not a valid host</returns>
    private static Uri? CreateTagsUri(string registry, string repository, string? lastTag, out string refusalMessage)
    {
        if (RegistryReferenceValidator.HasDotSegment(repository))
        {
            refusalMessage = RegistryReferenceValidator.DotSegmentMessage;

            return null;
        }

        var relativePath = $"v2/{EscapeRepository(repository)}/tags/list?n=100";

        if (string.IsNullOrWhiteSpace(lastTag) == false)
        {
            relativePath += $"&last={Uri.EscapeDataString(lastTag)}";
        }

        var tagsUri = CreateRegistryUri(registry, relativePath);

        refusalMessage = tagsUri is null ? RegistryReferenceValidator.InvalidRegistryMessage : string.Empty;

        return tagsUri;
    }

    /// <summary>
    /// Create an absolute registry URI over HTTPS from a host with an optional port
    /// </summary>
    /// <param name="registry">Registry host with an optional port</param>
    /// <param name="relativePath">Relative path</param>
    /// <returns>Absolute URI or null when the registry is not a valid host with an optional port in 1-65535</returns>
    private static Uri? CreateRegistryUri(string registry, string relativePath)
    {
        if (RegistryReferenceValidator.IsValidRegistry(registry) == false
            || Uri.TryCreate($"https://{registry}/", UriKind.Absolute, out var baseUri) == false
            || baseUri.Port == 0)
        {
            return null;
        }

        var querySeparatorIndex = relativePath.IndexOf('?');
        var path = querySeparatorIndex >= 0 ? relativePath[..querySeparatorIndex] : relativePath;
        var query = querySeparatorIndex >= 0 ? relativePath[(querySeparatorIndex + 1)..] : string.Empty;
        var registryBuilder = new UriBuilder(baseUri)
                              {
                                  Path = path,
                                  Query = query,
                              };

        return registryBuilder.Uri;
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
    /// Normalize a registry value to a stable host representation that keeps its port
    /// </summary>
    /// <param name="registry">Raw registry value</param>
    /// <returns>Normalized registry value</returns>
    private static string NormalizeRegistry(string registry)
    {
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
        var issuedAtUtc = DateTimeOffset.TryParse(issuedAtValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedIssuedAtUtc)
                              ? parsedIssuedAtUtc
                              : DateTimeOffset.UtcNow;
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
    private static string[] TryReadEnvironmentVariables(JsonElement rootElement)
    {
        if (rootElement.TryGetProperty(ConfigPropertyName, out var configElement) == false
            || configElement.ValueKind != JsonValueKind.Object
            || configElement.TryGetProperty("Env", out var envElement) == false
            || envElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return envElement.EnumerateArray()
                         .Select(element => ResponseBodyExcerpt.ReplaceControlCharacters(element.GetString()))
                         .Where(value => string.IsNullOrWhiteSpace(value) == false)
                         .Cast<string>()
                         .ToArray();
    }

    /// <summary>
    /// Read optional labels from a config blob
    /// </summary>
    /// <param name="rootElement">Config blob root element</param>
    /// <returns>Labels</returns>
    private static Dictionary<string, string> TryReadLabels(JsonElement rootElement)
    {
        if (rootElement.TryGetProperty(ConfigPropertyName, out var configElement) == false
            || configElement.ValueKind != JsonValueKind.Object
            || configElement.TryGetProperty("Labels", out var labelsElement) == false
            || labelsElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return labelsElement.EnumerateObject()
                            .Where(property => property.Value.ValueKind == JsonValueKind.String)
                            .ToDictionary(property => property.Name,
                                          property => ResponseBodyExcerpt.ReplaceControlCharacters(property.Value.GetString()) ?? string.Empty,
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
                   OperatingSystem = ResponseBodyExcerpt.ReplaceControlCharacters(TryGetString(rootElement, "os")),
                   Architecture = ResponseBodyExcerpt.ReplaceControlCharacters(TryGetString(rootElement, "architecture")),
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

        return DateTimeOffset.TryParse(createdValue, CultureInfo.InvariantCulture, out var createdAtUtc) ? createdAtUtc : null;
    }

    /// <summary>
    /// Read the raw next-page link target from the Link header
    /// </summary>
    /// <param name="response">Current response</param>
    /// <returns>Raw link target or null</returns>
    private static string? GetNextTagsLink(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Link", out var linkValues) == false)
        {
            return null;
        }

        foreach (var linkValue in linkValues)
        {
            foreach (var linkEntry in SplitLinkHeaderEntries(linkValue))
            {
                if (HasNextRelation(linkEntry) == false)
                {
                    continue;
                }

                var start = linkEntry.IndexOf('<');
                var end = linkEntry.IndexOf('>');

                if (start < 0 || end <= start)
                {
                    continue;
                }

                var linkTarget = linkEntry[(start + 1)..end];

                if (string.IsNullOrWhiteSpace(linkTarget) == false)
                {
                    return linkTarget;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Split Link header entries without splitting inside a URI target or quoted parameter
    /// </summary>
    /// <param name="linkValue">Raw Link header value</param>
    /// <returns>Link entries</returns>
    private static IEnumerable<string> SplitLinkHeaderEntries(string linkValue)
    {
        var current = new List<char>();
        var inUriTarget = false;
        var inQuotes = false;
        var escaped = false;

        foreach (var character in linkValue)
        {
            UpdateLinkHeaderState(character, ref inUriTarget, ref inQuotes, ref escaped);

            if (character == ',' && inUriTarget == false && inQuotes == false)
            {
                if (current.Count > 0)
                {
                    yield return new string([.. current]);
                    current.Clear();
                }

                continue;
            }

            current.Add(character);
        }

        if (current.Count > 0)
        {
            yield return new string([.. current]);
        }
    }

    /// <summary>
    /// Update the state used while parsing a Link header
    /// </summary>
    /// <param name="character">Current header character</param>
    /// <param name="inUriTarget">Whether parsing an angle-bracketed URI target</param>
    /// <param name="inQuotes">Whether parsing a quoted parameter</param>
    /// <param name="escaped">Whether the previous character escaped the current character</param>
    private static void UpdateLinkHeaderState(char character,
                                              ref bool inUriTarget,
                                              ref bool inQuotes,
                                              ref bool escaped)
    {
        if (character == '<' && inQuotes == false)
        {
            inUriTarget = true;
        }
        else if (character == '>' && inQuotes == false)
        {
            inUriTarget = false;
        }
        else if (character == '"' && inUriTarget == false && escaped == false)
        {
            inQuotes = inQuotes == false;
        }

        escaped = character == '\\' && inQuotes && escaped == false;
    }

    /// <summary>
    /// Determine whether a Link entry declares the next relation
    /// </summary>
    /// <param name="linkEntry">Link entry</param>
    /// <returns>True when the entry has an exact next relation</returns>
    private static bool HasNextRelation(string linkEntry)
    {
        var end = linkEntry.IndexOf('>');

        if (end < 0)
        {
            return false;
        }

        foreach (var parameter in SplitLinkParameters(linkEntry, end + 1))
        {
            var separatorIndex = parameter.IndexOf('=');

            if (separatorIndex <= 0
                || string.Equals(parameter[..separatorIndex].Trim(), "rel", StringComparison.OrdinalIgnoreCase) == false)
            {
                continue;
            }

            var relationValue = parameter[(separatorIndex + 1)..].Trim().Trim('"');

            if (relationValue.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                             .Any(relation => string.Equals(relation, "next", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Split Link parameters without splitting inside a quoted value
    /// </summary>
    /// <param name="linkEntry">Link entry</param>
    /// <param name="start">Parameter start index</param>
    /// <returns>Link parameters</returns>
    private static IEnumerable<string> SplitLinkParameters(string linkEntry, int start)
    {
        var current = new List<char>();
        var inQuotes = false;
        var escaped = false;

        for (var index = start; index < linkEntry.Length; index++)
        {
            var character = linkEntry[index];

            if (character == '"' && escaped == false)
            {
                inQuotes = inQuotes == false;
            }

            if (character == '\\' && inQuotes && escaped == false)
            {
                escaped = true;
            }
            else
            {
                escaped = false;
            }

            if (character == ';' && inQuotes == false)
            {
                yield return new string([.. current]);
                current.Clear();

                continue;
            }

            current.Add(character);
        }

        if (current.Count > 0)
        {
            yield return new string([.. current]);
        }
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
        var body = await ResponseBodyExcerpt.ReadAsync(response.Content, cancellationToken).ConfigureAwait(false);

        return ExternalOperationResult<T>.Failed($"{messagePrefix}: {(int)response.StatusCode} {ResponseBodyExcerpt.Truncate(response.ReasonPhrase, ResponseBodyExcerpt.MaxReasonPhraseLength)}{(string.IsNullOrWhiteSpace(body) ? string.Empty : $" - {body}")}");
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

    /// <summary>
    /// Read the tag names of a registry tag listing response
    /// </summary>
    /// <param name="response">Registry response</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tag names reported by the registry</returns>
    private static async Task<IReadOnlyList<string>> ReadTagNamesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var tagNames = new List<string>();
        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (responseStream.ConfigureAwait(false))
        {
            using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (jsonDocument.RootElement.TryGetProperty("tags", out var tagsElement) == false
                || tagsElement.ValueKind != JsonValueKind.Array)
            {
                return tagNames;
            }

            foreach (var tagElement in tagsElement.EnumerateArray())
            {
                var tagName = tagElement.GetString();

                if (RegistryReferenceValidator.IsValidTag(tagName))
                {
                    tagNames.Add(tagName);
                }
            }
        }

        return tagNames;
    }

    /// <summary>
    /// Append the result of a single tag inspection to the collected tag metadata
    /// </summary>
    /// <param name="tagName">Inspected tag name</param>
    /// <param name="tagResult">Tag lookup result</param>
    /// <param name="queryOptions">Tag query options</param>
    /// <param name="tags">Collected tag metadata</param>
    private static void AppendTagResult(string tagName,
                                        ExternalOperationResult<DockerHubTagData> tagResult,
                                        RegistryTagQueryOptions? queryOptions,
                                        List<DockerHubTagData> tags)
    {
        if (tagResult.Status == ExternalOperationStatus.Succeeded && tagResult.Data is not null)
        {
            if (RegistryTagQueryHelper.ShouldKeepTag(tagResult.Data, queryOptions))
            {
                tags.Add(tagResult.Data);
            }

            return;
        }

        tags.Add(new DockerHubTagData
                 {
                     Tag = tagName,
                 });
    }

    #endregion // Static methods

    #region Methods

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
        var manifestUri = CreateManifestUri(imageReference.Registry, imageReference.Repository, reference, out var refusalMessage);

        if (manifestUri is null)
        {
            return ExternalOperationResult<ManifestMetadata>.NotFound(refusalMessage);
        }

        using var response = await SendManifestRequestAsync(manifestUri,
                                                            imageReference.Repository,
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
                                                                           ConfigDigest = jsonDocument.RootElement.TryGetProperty(ConfigPropertyName, out var configElement)
                                                                                              ? TryGetString(configElement, DigestPropertyName)
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
        var blobUri = CreateBlobUri(imageReference.Registry, imageReference.Repository, configDigest, out _);

        if (blobUri is null)
        {
            return null;
        }

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

            if (jsonDocument.RootElement.TryGetProperty(ConfigPropertyName, out var configElement) == false
                || configElement.TryGetProperty("Labels", out var labelsElement) == false
                || labelsElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var baseImageName = ResponseBodyExcerpt.ReplaceControlCharacters(TryGetString(labelsElement, OciBaseImageNameLabel));

            if (string.IsNullOrWhiteSpace(baseImageName))
            {
                return null;
            }

            var baseImageDigest = ResponseBodyExcerpt.ReplaceControlCharacters(TryGetString(labelsElement, OciBaseImageDigestLabel));

            return BaseImageLabelReader.ReadReference(baseImageName, baseImageDigest);
        }
    }

    /// <summary>
    /// Send a manifest request with the OCI accept headers
    /// </summary>
    /// <param name="manifestUri">Manifest URI</param>
    /// <param name="repository">Repository path</param>
    /// <param name="method">HTTP method</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response</returns>
    private Task<HttpResponseMessage> SendManifestRequestAsync(Uri manifestUri,
                                                               string repository,
                                                               HttpMethod method,
                                                               CancellationToken cancellationToken)
    {
        return SendRegistryRequestAsync(manifestUri,
                                        repository,
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

    /// <summary>
    /// Append the tag metadata of a tag listing page until the configured inspection limit is reached
    /// </summary>
    /// <param name="tagNames">Tag names of the current page</param>
    /// <param name="lookupContext">Shared registry tag lookup parameters</param>
    /// <param name="tags">Collected tag metadata</param>
    /// <param name="inspectedTagCount">Number of tags inspected so far</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of tags inspected after the current page</returns>
    private async Task<int> AppendInspectedTagsAsync(IReadOnlyList<string> tagNames,
                                                     RegistryTagLookupContext lookupContext,
                                                     List<DockerHubTagData> tags,
                                                     int inspectedTagCount,
                                                     CancellationToken cancellationToken)
    {
        foreach (var tagName in tagNames)
        {
            if (RegistryTagQueryHelper.ShouldInspectTagName(tagName, lookupContext.QueryOptions) == false)
            {
                continue;
            }

            if (lookupContext.QueryOptions is not null && inspectedTagCount >= lookupContext.QueryOptions.MaximumTags)
            {
                break;
            }

            var tagReference = new ImageReference
                               {
                                   Registry = lookupContext.Registry,
                                   Repository = lookupContext.Repository,
                                   Tag = tagName,
                               };
            var tagResult = await GetTagAsync(tagReference,
                                              lookupContext.OperatingSystem,
                                              lookupContext.Architecture,
                                              cancellationToken).ConfigureAwait(false);

            inspectedTagCount++;

            AppendTagResult(tagName, tagResult, lookupContext.QueryOptions, tags);
        }

        return inspectedTagCount;
    }

    #endregion // Methods

    #region IRegistryMetadataClient

    /// <inheritdoc/>
    public bool CanHandle(string registry)
    {
        return string.IsNullOrWhiteSpace(registry) == false
               && DockerHubClient.SupportsRegistry(registry) == false;
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<DockerHubTagData>> GetTagAsync(ImageReference imageReference,
                                                                             string? operatingSystem = null,
                                                                             string? architecture = null,
                                                                             CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageReference);

        if (CanHandle(imageReference.Registry) == false)
        {
            return ExternalOperationResult<DockerHubTagData>.Unsupported($"Registry '{imageReference.Registry}' is not supported by the OCI registry adapter");
        }

        if (RegistryReferenceValidator.IsValidTag(imageReference.Tag) == false)
        {
            return ExternalOperationResult<DockerHubTagData>.NotFound(RegistryReferenceValidator.InvalidTagMessage);
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
                                                                       Digest = RegistryReferenceValidator.ValidDigestOrNull(manifestResult.Data.Digest),
                                                                       PublishedAtUtc = manifestResult.Data.PublishedAtUtc,
                                                                   });
    }

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>> GetTagsAsync(string registry,
                                                                                             string repository,
                                                                                             string? operatingSystem = null,
                                                                                             string? architecture = null,
                                                                                             RegistryTagQueryOptions? queryOptions = null,
                                                                                             CancellationToken cancellationToken = default)
    {
        if (CanHandle(registry) == false)
        {
            return ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Unsupported($"Registry '{registry}' is not supported by the OCI registry adapter");
        }

        if (RegistryReferenceValidator.IsValidRegistry(registry) == false)
        {
            return ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.NotFound(RegistryReferenceValidator.InvalidRegistryMessage);
        }

        var normalizedRegistry = NormalizeRegistry(registry);
        var allowedOrigin = CreateTagsUri(normalizedRegistry, repository, lastTag: null, out var refusalMessage);

        if (allowedOrigin is null)
        {
            return ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.NotFound(refusalMessage);
        }

        Uri requestUri = allowedOrigin;
        var tags = new List<DockerHubTagData>();
        var inspectedTagCount = 0;
        var lookupContext = new RegistryTagLookupContext
                            {
                                Registry = normalizedRegistry,
                                Repository = repository,
                                OperatingSystem = operatingSystem,
                                Architecture = architecture,
                                QueryOptions = queryOptions,
                            };

        while (true)
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

            var tagNames = await ReadTagNamesAsync(response, cancellationToken).ConfigureAwait(false);

            inspectedTagCount = await AppendInspectedTagsAsync(tagNames,
                                                               lookupContext,
                                                               tags,
                                                               inspectedTagCount,
                                                               cancellationToken).ConfigureAwait(false);

            if (queryOptions is not null && inspectedTagCount >= queryOptions.MaximumTags)
            {
                break;
            }

            var nextLink = GetNextTagsLink(response);

            if (nextLink is null)
            {
                break;
            }

            var nextUri = PaginationLinkGuard.ResolveNextLink(requestUri, allowedOrigin, nextLink);

            if (nextUri is null)
            {
                var rejectedOrigin = PaginationLinkGuard.DescribeOrigin(requestUri, nextLink);

                _logger.OciRegistryPaginationLinkRejected(normalizedRegistry, repository, rejectedOrigin);

                return ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Failed(PaginationLinkGuard.CreateRejectionMessage($"Registry tag listing for '{normalizedRegistry}/{repository}'", rejectedOrigin));
            }

            requestUri = nextUri;
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

            return ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Failed($"Base image chain resolution failed for '{imageReference.FullReference}': {FailureReason.FromException(exception, null, cancellationToken)}");
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

        var blobUri = CreateBlobUri(imageReference.Registry, imageReference.Repository, manifestResult.Data.ConfigDigest, out var refusalMessage);

        if (blobUri is null)
        {
            return ExternalOperationResult<RegistryImageConfigurationData>.NotFound(refusalMessage);
        }

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

    #endregion // IRegistryMetadataClient
}