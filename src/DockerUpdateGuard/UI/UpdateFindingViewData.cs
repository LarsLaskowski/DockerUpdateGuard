namespace DockerUpdateGuard.UI;

/// <summary>
/// Update finding view data
/// </summary>
public class UpdateFindingViewData
{
    #region Properties

    public string Type { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string? Details { get; set; }

    public string? RecommendedImage { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset DetectedAtUtc { get; set; }

    #endregion // Properties
}