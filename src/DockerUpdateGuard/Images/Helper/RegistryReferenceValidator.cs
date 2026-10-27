using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

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
    /// Maximum length of a digest
    /// </summary>
    public const int MaxDigestLength = 255;

    /// <summary>
    /// Message of a lookup for a tag that is not valid
    /// </summary>
    public const string InvalidTagMessage = "Requested tag is not a valid registry tag";

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

    #endregion // Regular expressions

    #region Methods

    /// <summary>
    /// Check whether a string is a valid tag
    /// </summary>
    /// <param name="tag">Tag to check</param>
    /// <returns>True when the tag is valid</returns>
    public static bool IsValidTag([NotNullWhen(true)] string? tag)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    /// <summary>
    /// Return the digest when it is valid
    /// </summary>
    /// <param name="digest">Digest to check</param>
    /// <returns>The digest or null</returns>
    public static string? ValidDigestOrNull(string? digest)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Methods
}