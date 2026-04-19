using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Images;

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