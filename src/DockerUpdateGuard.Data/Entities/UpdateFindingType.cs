namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Type of update finding, stored as its integer number;
/// number 1 (base image update) is retired and never reused
/// </summary>
public enum UpdateFindingType
{
    /// <summary>
    /// No finding type set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Update for a runtime container image
    /// </summary>
    RuntimeImageUpdate = 2,

    /// <summary>
    /// Alternative tag suggestion
    /// </summary>
    TagRecommendation = 3,

    /// <summary>
    /// Derived local base runtime update
    /// </summary>
    DerivedBaseRuntimeUpdate = 4
}