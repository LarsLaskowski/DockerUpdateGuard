namespace DockerUpdateGuard.Portainer.Data;

/// <summary>
/// Portainer container action outcome
/// </summary>
public class PortainerActionOutcomeData
{
    #region Properties

    /// <summary>
    /// Indicates whether the container was already in the requested state
    /// </summary>
    public bool AlreadyInRequestedState { get; set; }

    /// <summary>
    /// Outcome message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    #endregion // Properties
}