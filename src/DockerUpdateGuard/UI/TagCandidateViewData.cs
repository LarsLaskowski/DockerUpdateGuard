namespace DockerUpdateGuard.UI;

/// <summary>
/// Tag candidate view data
/// </summary>
public class TagCandidateViewData
{
    #region Properties

    public string Tag { get; set; } = string.Empty;

    public string? Digest { get; set; }

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public string? Reason { get; set; }

    public bool IsRecommended { get; set; }

    public bool IsSelected { get; set; }

    public string? ResolvedVersionTag { get; set; }

    #endregion // Properties
}