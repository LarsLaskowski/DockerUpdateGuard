namespace DockerUpdateGuard.UI;

/// <summary>
/// Scan history list item
/// </summary>
public class ScanHistoryItemData
{
    #region Properties

    /// <summary>
    /// Scan history identifier
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Scan type
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Scan status
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Trigger source
    /// </summary>
    public string TriggerSource { get; set; } = string.Empty;

    /// <summary>
    /// Subject name
    /// </summary>
    public string? SubjectName { get; set; }

    /// <summary>
    /// Timestamp when the scan started
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>
    /// Timestamp when the scan completed
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// Error message
    /// </summary>
    public string? ErrorMessage { get; set; }

    #endregion // Properties
}