namespace DockerUpdateGuard.Portainer;

/// <summary>
/// Portainer action request
/// </summary>
public class PortainerActionRequest
{
    #region Properties

    /// <summary>
    /// Resource name
    /// </summary>
    public string ResourceName { get; set; } = string.Empty;

    /// <summary>
    /// Action name
    /// </summary>
    public string ActionName { get; set; } = string.Empty;

    #endregion // Properties
}