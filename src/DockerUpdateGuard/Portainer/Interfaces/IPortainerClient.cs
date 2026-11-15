using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Portainer.Data;

namespace DockerUpdateGuard.Portainer.Interfaces;

/// <summary>
/// Portainer adapter contract
/// </summary>
public interface IPortainerClient
{
    #region Methods

    /// <summary>
    /// Check the Portainer capability of an instance
    /// </summary>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Capability outcome</returns>
    Task<ExternalOperationResult<PortainerCapabilityData>> CheckCapabilityAsync(DockerInstanceOptions instanceOptions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a container action through Portainer
    /// </summary>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="actionRequest">Action request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Action outcome</returns>
    Task<ExternalOperationResult<PortainerActionOutcomeData>> ExecuteContainerActionAsync(DockerInstanceOptions instanceOptions,
                                                                                          PortainerContainerActionRequest actionRequest,
                                                                                          CancellationToken cancellationToken = default);

    #endregion // Methods
}