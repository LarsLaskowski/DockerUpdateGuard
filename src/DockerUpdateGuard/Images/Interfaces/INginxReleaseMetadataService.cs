using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Images.Interfaces;

/// <summary>
/// Reads NGINX channel release metadata
/// </summary>
public interface INginxReleaseMetadataService
{
    #region Methods

    /// <summary>
    /// Read NGINX release metadata for a specific channel
    /// </summary>
    /// <param name="channelVersion">Channel version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Channel release metadata</returns>
    Task<ExternalOperationResult<NginxChannelReleaseData>> GetChannelReleaseAsync(string channelVersion, CancellationToken cancellationToken = default);

    #endregion // Methods
}