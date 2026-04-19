namespace DockerUpdateGuard.Portainer;

/// <summary>
/// Portainer action result
/// </summary>
public class PortainerActionResult
{
    #region Properties

    /// <summary>
    /// Indicates whether the action succeeded
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// Result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    #endregion // Properties
}