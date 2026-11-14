namespace DockerUpdateGuard.UI;

/// <summary>
/// Container action run shown in the action history of a runtime container
/// </summary>
public class ContainerActionRunViewData
{
    #region Properties

    /// <summary>
    /// Action display name
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Run status display name
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Time the action was requested
    /// </summary>
    public DateTimeOffset RequestedAtUtc { get; set; }

    /// <summary>
    /// Time the action completed
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// Requester of the action
    /// </summary>
    public string? RequestedBy { get; set; }

    /// <summary>
    /// Outcome message
    /// </summary>
    public string? Message { get; set; }

    #endregion // Properties
}