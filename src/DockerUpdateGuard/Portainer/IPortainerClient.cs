using DockerUpdateGuard.Configuration;

namespace DockerUpdateGuard.Portainer;

/// <summary>
/// Portainer adapter contract
/// </summary>
public interface IPortainerClient
{
    #region Methods

    /// <summary>
    /// Read Portainer capability information for an instance
    /// </summary>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Capability data</returns>
    Task<PortainerCapabilityData> GetCapabilityAsync(DockerInstanceOptions instanceOptions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a Portainer action
    /// </summary>
    /// <param name="instanceOptions">Docker instance options</param>
    /// <param name="actionRequest">Action request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Action result</returns>
    Task<PortainerActionResult> ExecuteActionAsync(DockerInstanceOptions instanceOptions,
                                                   PortainerActionRequest actionRequest,
                                                   CancellationToken cancellationToken = default);

    #endregion // Methods
}