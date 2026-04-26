namespace DockerUpdateGuard.UI;

/// <summary>
/// Update finding view data
/// </summary>
public class UpdateFindingViewData
{
    #region Properties

    /// <summary>
    /// Finding type
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Finding summary
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Finding details
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Recommended image reference
    /// </summary>
    public string? RecommendedImage { get; set; }

    /// <summary>
    /// Whether the finding is active
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Timestamp when the finding was detected
    /// </summary>
    public DateTimeOffset DetectedAtUtc { get; set; }

    /// <summary>
    /// Tag candidates
    /// </summary>
    public IReadOnlyList<TagCandidateViewData> TagCandidates { get; set; } = [];

    #endregion // Properties
}