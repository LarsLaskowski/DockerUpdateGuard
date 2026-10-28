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
        if (string.IsNullOrWhiteSpace(baseImageName))
        {
            return null;
        }

        ImageReference parsedReference;

        try
        {
            parsedReference = new ImageReferenceParser().Parse(baseImageName);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(parsedReference.Digest)
            && string.IsNullOrWhiteSpace(baseImageDigest) == false)
        {
            parsedReference.Digest = ImageReferenceParser.NormalizeDigest(baseImageDigest);
        }

        return RegistryReferenceValidator.ValidReferenceOrNull(parsedReference);
    }

    #endregion // Methods
}