namespace DockerUpdateGuard.Images;

/// <summary>
/// Update evaluation state
/// </summary>
public enum UpdateEvaluationStatus
{
    /// <summary>
    /// No state set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// No newer update found
    /// </summary>
    UpToDate = 1,

    /// <summary>
    /// Update is available
    /// </summary>
    UpdateAvailable = 2,

    /// <summary>
    /// Human review is required
    /// </summary>
    NeedsReview = 3,

    /// <summary>
    /// Update state is unknown
    /// </summary>
    Unknown = 4,
}