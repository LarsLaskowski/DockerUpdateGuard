using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Reads .NET release metadata from the official Microsoft feed
/// </summary>
public partial class DotNetReleaseMetadataService : IDotNetReleaseMetadataService
{
    #region Fields

    /// <summary>
    /// Cached channel data
    /// </summary>
    private readonly Dictionary<string, DotNetChannelReleaseData> _channelCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// HTTP client
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Logger
    /// </summary>
    private readonly ILogger<DotNetReleaseMetadataService> _logger;

    /// <summary>
    /// Whether the feed index was loaded already
    /// </summary>
    private bool _indexLoaded;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="httpClient">HTTP client</param>
    /// <param name="logger">Logger</param>
    public DotNetReleaseMetadataService(HttpClient httpClient, ILogger<DotNetReleaseMetadataService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Read an optional string property
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <param name="propertyName">Property name</param>
    /// <returns>String value</returns>
    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.String
                   ? property.GetString()
                   : null;
    }

    /// <summary>
    /// Read an optional boolean property
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <param name="propertyName">Property name</param>
    /// <returns>Boolean value</returns>
    private static bool TryGetBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Parse a timestamp
    /// </summary>
    /// <param name="value">Timestamp value</param>
    /// <returns>Parsed timestamp</returns>
    private static DateTimeOffset? TryParseTimestamp(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, out var timestamp) ? timestamp : null;
    }

    /// <summary>
    /// Parse a .NET runtime version
    /// </summary>
    /// <param name="rawVersion">Raw version string</param>
    /// <param name="version">Parsed version</param>
    /// <returns>True when parsing succeeded</returns>
    private static bool TryParseRuntimeVersion(string? rawVersion, out Version? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return false;
        }

        var match = RuntimeVersionRegex().Match(rawVersion);

        if (match.Success == false)
        {
            return false;
        }

        if (Version.TryParse(match.Value, out var parsedVersion) == false)
        {
            return false;
        }

        version = new Version(parsedVersion.Major, parsedVersion.Minor, parsedVersion.Build);

        return true;
    }

    /// <summary>
    /// Regex for stable runtime versions
    /// </summary>
    /// <returns>Compiled regex</returns>
    [GeneratedRegex(@"\d+\.\d+\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeVersionRegex();

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Load the releases index into the channel cache
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Operation result</returns>
    private async Task<ExternalOperationResult<bool>> LoadIndexAsync(CancellationToken cancellationToken)
    {
        if (_indexLoaded)
        {
            return ExternalOperationResult<bool>.Succeeded(true);
        }

        try
        {
            using var response = await _httpClient.GetAsync("releases-index.json", cancellationToken)
                                                  .ConfigureAwait(false);

            if (response.IsSuccessStatusCode == false)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken)
                                                 .ConfigureAwait(false);

                return ExternalOperationResult<bool>.Failed($"The .NET release index returned {(int)response.StatusCode}: {body}");
            }

            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                                                       .ConfigureAwait(false);

            await using (responseStream.ConfigureAwait(false))
            {
                using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken)
                                                           .ConfigureAwait(false);

                if (jsonDocument.RootElement.TryGetProperty("releases-index", out var releasesIndexElement) == false
                    || releasesIndexElement.ValueKind != JsonValueKind.Array)
                {
                    return ExternalOperationResult<bool>.Failed("The .NET release index does not contain a valid releases-index array");
                }

                foreach (var releaseElement in releasesIndexElement.EnumerateArray())
                {
                    var channelVersion = TryGetString(releaseElement, "channel-version");
                    var latestRuntime = TryGetString(releaseElement, "latest-runtime");

                    if (string.IsNullOrWhiteSpace(channelVersion)
                        || TryParseRuntimeVersion(latestRuntime, out var latestRuntimeVersion) == false
                        || latestRuntimeVersion is null)
                    {
                        continue;
                    }

                    _channelCache[channelVersion] = new DotNetChannelReleaseData
                                                    {
                                                        ChannelVersion = channelVersion,
                                                        LatestRuntimeVersion = latestRuntimeVersion,
                                                        LatestReleaseDateUtc = TryParseTimestamp(TryGetString(releaseElement, "latest-release-date")),
                                                        IsSecurityRelease = TryGetBoolean(releaseElement, "security"),
                                                        SupportPhase = TryGetString(releaseElement, "support-phase"),
                                                        EndOfLifeDateUtc = TryParseTimestamp(TryGetString(releaseElement, "eol-date")),
                                                        ReleasesJsonUrl = TryGetString(releaseElement, "releases.json"),
                                                    };
                }
            }

            _indexLoaded = true;

            return ExternalOperationResult<bool>.Succeeded(true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Loading .NET release metadata failed");

            return ExternalOperationResult<bool>.Failed($"Loading .NET release metadata failed: {exception.Message}");
        }
    }

    #endregion // Methods

    #region IDotNetReleaseMetadataService

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<DotNetChannelReleaseData>> GetChannelReleaseAsync(string channelVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelVersion);

        if (_channelCache.TryGetValue(channelVersion, out var cachedChannelData))
        {
            return ExternalOperationResult<DotNetChannelReleaseData>.Succeeded(cachedChannelData);
        }

        var indexResult = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);

        if (indexResult.Status != ExternalOperationStatus.Succeeded)
        {
            return indexResult.Status switch
                   {
                       ExternalOperationStatus.NotConfigured => ExternalOperationResult<DotNetChannelReleaseData>.NotConfigured(indexResult.Message ?? "The .NET release metadata source is not configured"),
                       ExternalOperationStatus.Unsupported => ExternalOperationResult<DotNetChannelReleaseData>.Unsupported(indexResult.Message ?? "The .NET release metadata source is unsupported"),
                       ExternalOperationStatus.NotFound => ExternalOperationResult<DotNetChannelReleaseData>.NotFound(indexResult.Message ?? "The .NET release metadata source could not be found"),
                       ExternalOperationStatus.Unknown => ExternalOperationResult<DotNetChannelReleaseData>.Unknown(indexResult.Message ?? "The .NET release metadata source returned an unknown result"),
                       _ => ExternalOperationResult<DotNetChannelReleaseData>.Failed(indexResult.Message ?? "Loading .NET release metadata failed"),
                   };
        }

        return _channelCache.TryGetValue(channelVersion, out var channelData)
                   ? ExternalOperationResult<DotNetChannelReleaseData>.Succeeded(channelData)
                   : ExternalOperationResult<DotNetChannelReleaseData>.NotFound($"No .NET release metadata is available for channel '{channelVersion}'");
    }

    #endregion // IDotNetReleaseMetadataService
}