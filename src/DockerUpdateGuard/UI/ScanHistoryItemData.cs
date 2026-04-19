namespace DockerUpdateGuard.UI;

/// <summary>
/// Scan history list item
/// </summary>
public class ScanHistoryItemData
{
    #region Properties

    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string TriggerSource { get; set; } = string.Empty;

    public string? SubjectName { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? ErrorMessage { get; set; }

    #endregion // Properties
}