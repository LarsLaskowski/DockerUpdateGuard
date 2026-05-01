using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Images.Interfaces;

/// <summary>
/// Base image resolver contract
/// </summary>
public interface IBaseImageResolver
{
    #region Methods

    /// <summary>
    /// Resolve base images for an observed image
    /// </summary>
    /// <param name="imageReference">Observed image reference</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Base image result</returns>
    Task<ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>> ResolveAsync(ImageReference imageReference, CancellationToken cancellationToken = default);

    #endregion // Methods
}