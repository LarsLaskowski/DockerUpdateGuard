using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images.Data;

namespace DockerUpdateGuard.Images.Interfaces;

/// <summary>
/// Image reference parser contract
/// </summary>
public interface IImageReferenceParser
{
    #region Methods

    /// <summary>
    /// Parse a Docker image reference
    /// </summary>
    /// <param name="value">Raw image reference</param>
    /// <returns>Parsed reference</returns>
    ImageReference Parse(string value);

    /// <summary>
    /// Format a persisted image version as a displayable reference
    /// </summary>
    /// <param name="imageVersion">Image version</param>
    /// <returns>Image reference string</returns>
    string Format(ImageVersion imageVersion);

    #endregion // Methods
}