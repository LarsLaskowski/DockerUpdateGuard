using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images.Data;

namespace DockerUpdateGuard.Images.Interfaces;

/// <summary>
/// Observed image registration contract
/// </summary>
public interface IImageRegistrationService
{
    #region Methods

    /// <summary>
    /// Register or update an observed image
    /// </summary>
    /// <param name="request">Registration request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed image</returns>
    Task<ObservedImage> RegisterAsync(ObservedImageRegistrationRequest request, CancellationToken cancellationToken = default);

    #endregion // Methods
}