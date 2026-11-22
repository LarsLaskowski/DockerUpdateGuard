using DockerUpdateGuard.Images.Data;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Limits and checks for the manual registration of an observed image
/// </summary>
internal static class ObservedImageRegistrationRules
{
    #region Constants

    /// <summary>
    /// Maximum length of an image name
    /// </summary>
    internal const int MaxNameLength = 200;

    /// <summary>
    /// Maximum length of a registry host
    /// </summary>
    internal const int MaxRegistryLength = 200;

    /// <summary>
    /// Maximum length of a repository
    /// </summary>
    internal const int MaxRepositoryLength = 300;

    /// <summary>
    /// Maximum length of a tag
    /// </summary>
    internal const int MaxTagLength = 200;

    /// <summary>
    /// Maximum length of a digest
    /// </summary>
    internal const int MaxDigestLength = 255;

    /// <summary>
    /// Message of the error raised when the image name is empty
    /// </summary>
    internal const string EmptyNameMessage = "The name must not be empty";

    /// <summary>
    /// Message of the error raised when the image name is too long
    /// </summary>
    internal const string NameTooLongMessage = "The name must not be longer than 200 characters";

    /// <summary>
    /// Message of the error raised when the registry is too long
    /// </summary>
    internal const string RegistryTooLongMessage = "The registry must not be longer than 200 characters";

    /// <summary>
    /// Message of the error raised when the repository is too long
    /// </summary>
    internal const string RepositoryTooLongMessage = "The repository must not be longer than 300 characters";

    /// <summary>
    /// Message of the error raised when the tag is too long
    /// </summary>
    internal const string TagTooLongMessage = "The tag must not be longer than 200 characters";

    /// <summary>
    /// Message of the error raised when the digest is too long
    /// </summary>
    internal const string DigestTooLongMessage = "The digest must not be longer than 255 characters";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Check the trimmed image name
    /// </summary>
    /// <param name="name">Image name</param>
    /// <returns>Error text or null</returns>
    internal static string? GetNameError(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return EmptyNameMessage;
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return NameTooLongMessage;
        }

        return null;
    }

    /// <summary>
    /// Check the parts of a parsed image reference against the catalog limits
    /// </summary>
    /// <param name="reference">Parsed image reference</param>
    /// <returns>Error text of the first part that is too long or null</returns>
    internal static string? GetPartsError(ImageReference reference)
    {
        if (reference.Registry.Length > MaxRegistryLength)
        {
            return RegistryTooLongMessage;
        }

        if (reference.Repository.Length > MaxRepositoryLength)
        {
            return RepositoryTooLongMessage;
        }

        if (reference.Tag.Length > MaxTagLength)
        {
            return TagTooLongMessage;
        }

        if (reference.Digest?.Length > MaxDigestLength)
        {
            return DigestTooLongMessage;
        }

        return null;
    }

    /// <summary>
    /// Parse a raw image reference and check its parts against the catalog limits
    /// </summary>
    /// <param name="imageReference">Raw image reference</param>
    /// <returns>Error text or null, also for a blank or unparsable reference</returns>
    internal static string? GetReferenceError(string? imageReference)
    {
        if (string.IsNullOrWhiteSpace(imageReference))
        {
            return null;
        }

        ImageReference parsedReference;

        try
        {
            parsedReference = new ImageReferenceParser().Parse(imageReference);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return GetPartsError(parsedReference);
    }

    #endregion // Methods
}