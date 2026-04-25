namespace DockerUpdateGuard.UI;

/// <summary>
/// Observed image list item
/// </summary>
public class ObservedImageListItemData
{
    #region Properties

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string ImageReference { get; set; } = string.Empty;

    public string LatestScanStatus { get; set; } = "NotScanned";

    public string? LatestScanMessage { get; set; }

    public int ActiveUpdateFindingCount { get; set; }

    public int ActiveVulnerabilityFindingCount { get; set; }

    public string VulnerabilityStatus { get; set; } = "Not scanned";

    public string? VulnerabilityMessage { get; set; }

    #endregion // Properties
}