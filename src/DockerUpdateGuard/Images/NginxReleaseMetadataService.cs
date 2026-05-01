using System.Text.RegularExpressions;

using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Reads NGINX release metadata from nginx.org download listings
/// </summary>
public partial class NginxReleaseMetadataService : INginxReleaseMetadataService
{
    #region Fields

    /// <summary>
    /// Cached channel data
    /// </summary>
    private readonly Dictionary<string, NginxChannelReleaseData> _channelCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// HTTP client
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Logger
    /// </summary>
    private readonly ILogger<NginxReleaseMetadataService> _logger;

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
    public NginxReleaseMetadataService(HttpClient httpClient, ILogger<NginxReleaseMetadataService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Parse a stable NGINX version
    /// </summary>
    /// <param name="rawVersion">Raw version string</param>
    /// <param name="version">Parsed version</param>
    /// <returns>True when parsing succeeded</returns>
    private static bool TryParseVersion(string? rawVersion, out Version? version)
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

        if (Version.TryParse(match.Value, out var parsedVersion) == false || parsedVersion is null)
        {
            return false;
        }

        version = new Version(parsedVersion.Major, parsedVersion.Minor, parsedVersion.Build);

        return true;
    }

    /// <summary>
    /// Regex for stable version strings
    /// </summary>
    /// <returns>Compiled regex</returns>
    [GeneratedRegex(@"\d+\.\d+\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeVersionRegex();

    /// <summary>
    /// Regex for NGINX tarball entries
    /// </summary>
    /// <returns>Compiled regex</returns>
    [GeneratedRegex(@"nginx-(?<version>\d+\.\d+\.\d+)\.tar\.gz", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NginxVersionRegex();

    #endregion // Static methods

    #region Methods

    /// <inheritdoc/>
    public async Task<ExternalOperationResult<NginxChannelReleaseData>> GetChannelReleaseAsync(string channelVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelVersion);

        if (_channelCache.TryGetValue(channelVersion, out var cachedChannelData))
        {
            return ExternalOperationResult<NginxChannelReleaseData>.Succeeded(cachedChannelData);
        }

        var indexResult = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);

        if (indexResult.Status != ExternalOperationStatus.Succeeded)
        {
            return indexResult.Status switch
                   {
                       ExternalOperationStatus.NotConfigured => ExternalOperationResult<NginxChannelReleaseData>.NotConfigured(indexResult.Message ?? "The NGINX release metadata source is not configured"),
                       ExternalOperationStatus.Unsupported => ExternalOperationResult<NginxChannelReleaseData>.Unsupported(indexResult.Message ?? "The NGINX release metadata source is unsupported"),
                       ExternalOperationStatus.NotFound => ExternalOperationResult<NginxChannelReleaseData>.NotFound(indexResult.Message ?? "The NGINX release metadata source could not be found"),
                       ExternalOperationStatus.Unknown => ExternalOperationResult<NginxChannelReleaseData>.Unknown(indexResult.Message ?? "The NGINX release metadata source returned an unknown result"),
                       _ => ExternalOperationResult<NginxChannelReleaseData>.Failed(indexResult.Message ?? "Loading NGINX release metadata failed"),
                   };
        }

        return _channelCache.TryGetValue(channelVersion, out var channelData)
                   ? ExternalOperationResult<NginxChannelReleaseData>.Succeeded(channelData)
                   : ExternalOperationResult<NginxChannelReleaseData>.NotFound($"No NGINX release metadata is available for channel '{channelVersion}'");
    }

    /// <summary>
    /// Load the NGINX download listing into the channel cache
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
            using var response = await _httpClient.GetAsync("download/", cancellationToken)
                                                  .ConfigureAwait(false);

            if (response.IsSuccessStatusCode == false)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken)
                                                 .ConfigureAwait(false);

                return ExternalOperationResult<bool>.Failed($"The NGINX download listing returned {(int)response.StatusCode}: {body}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken)
                                                .ConfigureAwait(false);
            var versions = NginxVersionRegex().Matches(content)
                                              .Select(match => match.Groups["version"].Value)
                                              .Distinct(StringComparer.OrdinalIgnoreCase)
                                              .Select(value => TryParseVersion(value, out var version) ? version : null)
                                              .Where(value => value is not null)
                                              .Cast<Version>()
                                              .ToList();

            foreach (var versionGroup in versions.GroupBy(version => $"{version.Major}.{version.Minor}", StringComparer.OrdinalIgnoreCase))
            {
                var latestVersion = versionGroup.OrderByDescending(version => version)
                                                .FirstOrDefault();

                if (latestVersion is null)
                {
                    continue;
                }

                _channelCache[versionGroup.Key] = new NginxChannelReleaseData
                                                  {
                                                      ChannelVersion = versionGroup.Key,
                                                      LatestVersion = latestVersion,
                                                  };
            }

            _indexLoaded = true;

            return ExternalOperationResult<bool>.Succeeded(true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Loading NGINX release metadata failed");

            return ExternalOperationResult<bool>.Failed($"Loading NGINX release metadata failed: {exception.Message}");
        }
    }

    #endregion // Methods
}