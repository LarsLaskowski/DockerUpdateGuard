using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Portainer.Data;

namespace DockerUpdateGuard.Portainer.Interfaces;

/// <summary>
/// Container action service contract
/// </summary>
public interface IContainerActionService
{
    #region Methods

    /// <summary>
    /// Determine which container actions are available for a container
    /// </summary>
    /// <param name="dockerInstanceId">Docker instance identifier</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Availability data</returns>
    Task<ContainerActionAvailabilityData> GetAvailabilityAsync(Guid dockerInstanceId, string containerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a container action and record it as an action run
    /// </summary>
    /// <param name="dockerInstanceId">Docker instance identifier</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="actionType">Action type</param>
    /// <returns>Run result</returns>
    Task<ContainerActionRunResultData> ExecuteAsync(Guid dockerInstanceId, string containerId, ContainerActionType actionType);

    #endregion // Methods
}