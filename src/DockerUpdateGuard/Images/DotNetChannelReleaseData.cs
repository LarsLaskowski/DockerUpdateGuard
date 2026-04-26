namespace DockerUpdateGuard.Images;

/// <summary>
/// Normalized .NET channel release data
/// </summary>
public class DotNetChannelReleaseData
{
    #region Properties

    /// <summary>
    /// Channel version
    /// </summary>
    public string ChannelVersion { get; set; } = string.Empty;

    /// <summary>
    /// Latest runtime version
    /// </summary>
    public Version LatestRuntimeVersion { get; set; } = new(0, 0, 0);

    /// <summary>
    /// Latest release timestamp
    /// </summary>
    public DateTimeOffset? LatestReleaseDateUtc { get; set; }

    /// <summary>
    /// Whether the latest release carries a security update
    /// </summary>
    public bool IsSecurityRelease { get; set; }

    /// <summary>
    /// Channel support phase
    /// </summary>
    public string? SupportPhase { get; set; }

    /// <summary>
    /// Channel end-of-life timestamp
    /// </summary>
    public DateTimeOffset? EndOfLifeDateUtc { get; set; }

    /// <summary>
    /// Channel releases document URL
    /// </summary>
    public string? ReleasesJsonUrl { get; set; }

    #endregion // Properties
}