namespace DockerUpdateGuard.UI;

/// <summary>
/// Tag candidate view data
/// </summary>
public class TagCandidateViewData
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
    /// Timestamp when the tag was published
    /// </summary>
    public DateTimeOffset? PublishedAtUtc { get; set; }

    /// <summary>
    /// Recommendation reason
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Whether the tag is recommended
    /// </summary>
    public bool IsRecommended { get; set; }

    /// <summary>
    /// Whether the tag is selected
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// Resolved version tag
    /// </summary>
    public string? ResolvedVersionTag { get; set; }

    #endregion // Properties
}