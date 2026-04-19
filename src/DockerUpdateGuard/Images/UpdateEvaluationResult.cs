namespace DockerUpdateGuard.Images;

/// <summary>
/// Update evaluation result
/// </summary>
public class UpdateEvaluationResult
{
    #region Properties

    /// <summary>
    /// Evaluation status
    /// </summary>
    public UpdateEvaluationStatus Status { get; set; }

    /// <summary>
    /// Summary message
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Optional details
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Optional recommended tag
    /// </summary>
    public string? RecommendedTag { get; set; }

    /// <summary>
    /// Optional recommended digest
    /// </summary>
    public string? RecommendedDigest { get; set; }

    /// <summary>
    /// Candidate list
    /// </summary>
    public IReadOnlyList<UpdateCandidateData> Candidates { get; set; } = [];

    #endregion // Properties
}