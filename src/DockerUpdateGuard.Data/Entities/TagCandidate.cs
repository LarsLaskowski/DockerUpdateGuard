namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Candidate tag for an update finding
/// </summary>
public class TagCandidate
{
    #region Properties

    /// <summary>
    /// Entity identifier
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Related update finding identifier
    /// </summary>
    public Guid UpdateFindingId { get; set; }

    /// <summary>
    /// Candidate tag
    /// </summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// Optional candidate digest
    /// </summary>
    public string? Digest { get; set; }

    /// <summary>
    /// Ranking score within a finding
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// Indicates whether the candidate is the recommended one
    /// </summary>
    public bool IsRecommended { get; set; }

    /// <summary>
    /// Optional publication timestamp
    /// </summary>
    public DateTimeOffset? PublishedAtUtc { get; set; }

    /// <summary>
    /// Optional reasoning text
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Related update finding
    /// </summary>
    public UpdateFinding UpdateFinding { get; set; } = null!;

    #endregion // Properties
}