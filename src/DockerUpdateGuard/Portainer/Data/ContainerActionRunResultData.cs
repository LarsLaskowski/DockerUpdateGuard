using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Portainer.Data;

/// <summary>
/// Result of a container action run
/// </summary>
public class ContainerActionRunResultData
{
    #region Properties

    /// <summary>
    /// Run status
    /// </summary>
    public ContainerActionStatus Status { get; set; }

    /// <summary>
    /// Result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    #endregion // Properties
}