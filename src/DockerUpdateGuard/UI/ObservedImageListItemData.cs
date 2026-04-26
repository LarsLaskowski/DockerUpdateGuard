namespace DockerUpdateGuard.UI;

/// <summary>
/// Observed image list item
/// </summary>
public class ObservedImageListItemData
{
    #region Properties

    /// <summary>
    /// Observed image identifier
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Observed image name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Observed image description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Image reference
    /// </summary>
    public string ImageReference { get; set; } = string.Empty;

    /// <summary>
    /// Latest scan status
    /// </summary>
    public string LatestScanStatus { get; set; } = "NotScanned";

    /// <summary>
    /// Latest scan message
    /// </summary>
    public string? LatestScanMessage { get; set; }

    /// <summary>
    /// Number of active update findings
    /// </summary>
    public int ActiveUpdateFindingCount { get; set; }

    /// <summary>
    /// Number of active vulnerability findings
    /// </summary>
    public int ActiveVulnerabilityFindingCount { get; set; }

    /// <summary>
    /// Vulnerability status
    /// </summary>
    public string VulnerabilityStatus { get; set; } = "Not scanned";

    /// <summary>
    /// Vulnerability message
    /// </summary>
    public string? VulnerabilityMessage { get; set; }

    /// <summary>
    /// Whether the image is owned by this environment
    /// </summary>
    public bool IsOwnImage { get; set; }

    /// <summary>
    /// Number of linked runtime containers
    /// </summary>
    public int LinkedRuntimeContainerCount { get; set; }

    /// <summary>
    /// Base-runtime alert summary
    /// </summary>
    public string? BaseRuntimeAlertSummary { get; set; }

    /// <summary>
    /// Base-runtime alert details
    /// </summary>
    public string? BaseRuntimeAlertDetails { get; set; }

    #endregion // Properties
}