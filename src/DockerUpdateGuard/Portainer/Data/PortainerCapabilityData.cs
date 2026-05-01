namespace DockerUpdateGuard.Portainer.Data;

/// <summary>
/// Portainer capability summary
/// </summary>
public class PortainerCapabilityData
{
    #region Properties

    /// <summary>
    /// Indicates whether Portainer is configured for the instance
    /// </summary>
    public bool IsConfigured { get; set; }

    /// <summary>
    /// Indicates whether Portainer actions are supported
    /// </summary>
    public bool SupportsActions { get; set; }

    /// <summary>
    /// Capability message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    #endregion // Properties
}