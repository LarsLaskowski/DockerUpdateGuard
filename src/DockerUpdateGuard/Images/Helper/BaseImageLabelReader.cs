using DockerUpdateGuard.Images.Data;

namespace DockerUpdateGuard.Images.Helper;

/// <summary>
/// Reads the base image reference of an image's labels
/// </summary>
internal static class BaseImageLabelReader
{
    #region Methods

    /// <summary>
    /// Parse and validate the base image reference of the labels
    /// </summary>
    /// <param name="baseImageName">Value of the base image name label</param>
    /// <param name="baseImageDigest">Value of the base image digest label</param>
    /// <returns>The valid reference or null</returns>
    public static ImageReference? ReadReference(string? baseImageName, string? baseImageDigest)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Methods
}