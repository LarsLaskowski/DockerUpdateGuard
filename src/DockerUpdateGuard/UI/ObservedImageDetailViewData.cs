namespace DockerUpdateGuard.UI;

/// <summary>
/// Observed image detail view data
/// </summary>
public class ObservedImageDetailViewData
{
    #region Properties

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string ImageReference { get; set; } = string.Empty;

    public string LatestScanStatus { get; set; } = "NotScanned";

    public string? LatestScanMessage { get; set; }

    public IReadOnlyList<BaseImageRelationshipData> BaseImages { get; set; } = [];

    public IReadOnlyList<UpdateFindingViewData> UpdateFindings { get; set; } = [];

    public IReadOnlyList<VulnerabilityFindingViewData> VulnerabilityFindings { get; set; } = [];

    public IReadOnlyList<ScanHistoryItemData> ScanHistory { get; set; } = [];

    #endregion // Properties
}