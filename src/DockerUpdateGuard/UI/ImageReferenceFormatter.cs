namespace DockerUpdateGuard.UI;

/// <summary>
/// Display helpers for Docker image references
/// </summary>
internal static class ImageReferenceFormatter
{
    #region Methods

    /// <summary>
    /// Return the image reference without the digest part
    /// </summary>
    /// <param name="fullRef">Full image reference</param>
    /// <returns>Reference without digest</returns>
    internal static string GetReference(string fullRef)
    {
        if (string.IsNullOrWhiteSpace(fullRef))
        {
            return fullRef;
        }

        var atIndex = fullRef.IndexOf('@');

        return atIndex >= 0 ? fullRef[..atIndex] : fullRef;
    }

    /// <summary>
    /// Return a shortened digest for compact display, or null when no digest is present.
    /// Returns only the first 12 characters of the hash, without algorithm prefix.
    /// </summary>
    /// <param name="fullRef">Full image reference</param>
    /// <returns>Shortened digest or null</returns>
    internal static string? GetShortDigest(string fullRef)
    {
        if (string.IsNullOrWhiteSpace(fullRef))
        {
            return null;
        }

        var atIndex = fullRef.IndexOf('@');

        if (atIndex < 0)
        {
            return null;
        }

        var digest = fullRef[(atIndex + 1)..];
        var colonIndex = digest.IndexOf(':');
        var hash = colonIndex >= 0 ? digest[(colonIndex + 1)..] : digest;

        return hash.Length > 12 ? hash[..12] + "…" : hash;
    }

    /// <summary>
    /// Return the full digest, or null when no digest is present
    /// </summary>
    /// <param name="fullRef">Full image reference</param>
    /// <returns>Full digest or null</returns>
    internal static string? GetFullDigest(string fullRef)
    {
        if (string.IsNullOrWhiteSpace(fullRef))
        {
            return null;
        }

        var atIndex = fullRef.IndexOf('@');

        return atIndex >= 0 ? fullRef[(atIndex + 1)..] : null;
    }

    #endregion // Methods
}