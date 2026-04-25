namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Runtime update assessment status
/// </summary>
public enum UpdateAssessmentStatus
{
    /// <summary>
    /// No update assessment has been stored yet
    /// </summary>
    NotEvaluated = 0,

    /// <summary>
    /// The runtime image is up to date
    /// </summary>
    UpToDate = 1,

    /// <summary>
    /// A direct update is available
    /// </summary>
    UpdateAvailable = 2,

    /// <summary>
    /// Alternative tags are available and require manual review
    /// </summary>
    ManualReviewRequired = 3,

    /// <summary>
    /// Registry lookup returned no usable tag data
    /// </summary>
    NoTagData = 4,

    /// <summary>
    /// The current registry adapter does not support this image
    /// </summary>
    Unsupported = 5,

    /// <summary>
    /// Update evaluation failed
    /// </summary>
    Failed = 6
}