namespace DockerUpdateGuard.Images;

/// <summary>
/// Candidate update suggestion
/// </summary>
public class UpdateCandidateData
{
    #region Properties

    /// <summary>
    /// Candidate tag
    /// </summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// Optional digest
    /// </summary>
    public string? Digest { get; set; }

    /// <summary>
    /// Optional publication timestamp
    /// </summary>
    public DateTimeOffset? PublishedAtUtc { get; set; }

    #endregion // Properties
}