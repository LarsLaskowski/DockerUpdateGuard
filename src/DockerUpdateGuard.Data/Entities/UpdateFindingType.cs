namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Type of update finding
/// </summary>
public enum UpdateFindingType
{
    /// <summary>
    /// No finding type set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Update for a base image
    /// </summary>
    BaseImageUpdate = 1,

    /// <summary>
    /// Update for a runtime container image
    /// </summary>
    RuntimeImageUpdate = 2,

    /// <summary>
    /// Alternative tag suggestion
    /// </summary>
    TagRecommendation = 3
}