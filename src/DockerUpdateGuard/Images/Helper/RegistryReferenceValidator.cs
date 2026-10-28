using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using DockerUpdateGuard.Images.Data;

namespace DockerUpdateGuard.Images.Helper;

/// <summary>
/// Validation of tag names and digests read from a registry
/// </summary>
internal static partial class RegistryReferenceValidator
{
    #region Const fields

    /// <summary>
    /// Maximum length of a tag
    /// </summary>
    public const int MaxTagLength = 128;

    /// <summary>
    /// Maximum length of a registry host with its port
    /// </summary>
    public const int MaxRegistryLength = 200;

    /// <summary>
    /// Maximum length of a repository path
    /// </summary>
    public const int MaxRepositoryLength = 255;

    /// <summary>
    /// Maximum length of a digest
    /// </summary>
    public const int MaxDigestLength = 255;

    /// <summary>
    /// Message of a lookup for a tag that is not valid
    /// </summary>
    public const string InvalidTagMessage = "Requested tag is not a valid registry tag";

    /// <summary>
    /// Message of a lookup whose request path would contain a dot segment
    /// </summary>
    public const string DotSegmentMessage = "Registry request path would contain a '.' or '..' segment";

    #endregion // Const fields

    #region Regular expressions

    /// <summary>
    /// Tag grammar pattern
    /// </summary>
    /// <returns>Compiled regular expression</returns>
    [GeneratedRegex(@"\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\z",
                    RegexOptions.CultureInvariant,
                    100)]
    private static partial Regex TagExpression();

    /// <summary>
    /// Digest grammar pattern
    /// </summary>
    /// <returns>Compiled regular expression</returns>
    [GeneratedRegex(@"\A[a-z0-9]+(?:[+._-][a-z0-9]+)*:[a-zA-Z0-9=_-]+\z",
                    RegexOptions.CultureInvariant,
                    100)]
    private static partial Regex DigestExpression();

    /// <summary>
    /// Registry grammar pattern
    /// </summary>
    /// <returns>Compiled regular expression</returns>
    [GeneratedRegex(@"\A(?:(?:[a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9-]*[a-zA-Z0-9])(?:\.(?:[a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9-]*[a-zA-Z0-9]))*|\[[a-fA-F0-9:]+\])(?::[0-9]+)?\z",
                    RegexOptions.CultureInvariant,
                    100)]
    private static partial Regex RegistryExpression();

    /// <summary>
    /// Repository grammar pattern
    /// </summary>
    /// <returns>Compiled regular expression</returns>
    [GeneratedRegex(@"\A[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*(?:/[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*)*\z",
                    RegexOptions.CultureInvariant,
                    100)]
    private static partial Regex RepositoryExpression();

    #endregion // Regular expressions

    #region Methods

    /// <summary>
    /// Check whether a slash-separated path contains a segment equal to '.' or '..'
    /// </summary>
    /// <param name="path">Path to check</param>
    /// <returns>True when the path contains a dot segment</returns>
    public static bool HasDotSegment(string path)
    {
        return false;
    }

    /// <summary>
    /// Check whether a string is a valid registry host with an optional port
    /// </summary>
    /// <param name="registry">Registry to check</param>
    /// <returns>True when the registry is valid</returns>
    public static bool IsValidRegistry([NotNullWhen(true)] string? registry)
    {
        return string.IsNullOrEmpty(registry) == false
               && registry.Length <= MaxRegistryLength
               && RegistryExpression().IsMatch(registry);
    }

    /// <summary>
    /// Check whether a string is a valid repository path
    /// </summary>
    /// <param name="repository">Repository to check</param>
    /// <returns>True when the repository is valid</returns>
    public static bool IsValidRepository([NotNullWhen(true)] string? repository)
    {
        return string.IsNullOrEmpty(repository) == false
               && repository.Length <= MaxRepositoryLength
               && RepositoryExpression().IsMatch(repository);
    }

    /// <summary>
    /// Return the reference when registry, repository and tag are valid, with an invalid digest removed; the given instance is changed
    /// </summary>
    /// <param name="reference">Reference to check</param>
    /// <returns>The same reference or null</returns>
    public static ImageReference? ValidReferenceOrNull(ImageReference reference)
    {
        if (IsValidRegistry(reference.Registry) == false
            || IsValidRepository(reference.Repository) == false
            || IsValidTag(reference.Tag) == false)
        {
            return null;
        }

        reference.Digest = ValidDigestOrNull(reference.Digest);

        return reference;
    }

    /// <summary>
    /// Check whether a string is a valid tag
    /// </summary>
    /// <param name="tag">Tag to check</param>
    /// <returns>True when the tag is valid</returns>
    public static bool IsValidTag([NotNullWhen(true)] string? tag)
    {
        return string.IsNullOrEmpty(tag) == false
               && tag.Length <= MaxTagLength
               && TagExpression().IsMatch(tag);
    }

    /// <summary>
    /// Return the digest when it is valid
    /// </summary>
    /// <param name="digest">Digest to check</param>
    /// <returns>The digest or null</returns>
    public static string? ValidDigestOrNull(string? digest)
    {
        if (string.IsNullOrEmpty(digest)
            || digest.Length > MaxDigestLength
            || DigestExpression().IsMatch(digest) == false)
        {
            return null;
        }

        return digest;
    }

    #endregion // Methods
}