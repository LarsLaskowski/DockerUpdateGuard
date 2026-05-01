using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Images.Interfaces;

/// <summary>
/// Reads .NET channel release metadata
/// </summary>
public interface IDotNetReleaseMetadataService
{
    #region Methods

    /// <summary>
    /// Read .NET release metadata for a specific channel
    /// </summary>
    /// <param name="channelVersion">Channel version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Channel release metadata</returns>
    Task<ExternalOperationResult<DotNetChannelReleaseData>> GetChannelReleaseAsync(string channelVersion, CancellationToken cancellationToken = default);

    #endregion // Methods
}