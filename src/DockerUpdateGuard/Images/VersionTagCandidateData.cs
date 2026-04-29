namespace DockerUpdateGuard.Images;

/// <summary>
/// Minimal tag candidate data for semantic version resolution
/// </summary>
public class VersionTagCandidateData
{
    #region Properties

    /// <summary>
    /// Tag value
    /// </summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// Tag digest
    /// </summary>
    public string? Digest { get; set; }

    /// <summary>
    /// Publication timestamp
    /// </summary>
    public DateTimeOffset? PublishedAtUtc { get; set; }

    #endregion // Properties
}