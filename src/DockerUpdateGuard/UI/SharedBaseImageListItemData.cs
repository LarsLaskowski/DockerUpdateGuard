namespace DockerUpdateGuard.UI;

/// <summary>
/// Shared base image view data
/// </summary>
public class SharedBaseImageListItemData
{
    #region Properties

    /// <summary>
    /// Base image version identifier
    /// </summary>
    public Guid BaseImageVersionId { get; set; }

    /// <summary>
    /// Image reference
    /// </summary>
    public string ImageReference { get; set; } = string.Empty;

    /// <summary>
    /// Grouped base-image version identifiers
    /// </summary>
    public IReadOnlyList<Guid> BaseImageVersionIds { get; set; } = [];

    /// <summary>
    /// Number of observed images
    /// </summary>
    public int ObservedImageCount { get; set; }

    /// <summary>
    /// Related parent images
    /// </summary>
    public IReadOnlyList<string> ParentImageReferences { get; set; } = [];

    /// <summary>
    /// Related runtime containers
    /// </summary>
    public IReadOnlyList<LinkedRuntimeContainerViewData> RuntimeContainers { get; set; } = [];

    /// <summary>
    /// Vulnerability assessment summary
    /// </summary>
    public VulnerabilityAssessmentViewData VulnerabilityAssessment { get; set; } = new();

    #endregion // Properties
}