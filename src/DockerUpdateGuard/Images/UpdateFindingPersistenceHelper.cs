namespace DockerUpdateGuard.Images;

/// <summary>
/// Helper methods for persisting update findings
/// </summary>
internal static class UpdateFindingPersistenceHelper
{
    #region Methods

    /// <summary>
    /// Determine whether a candidate digest can be persisted
    /// </summary>
    /// <param name="digest">Candidate digest</param>
    /// <returns>True when the digest is present</returns>
    public static bool HasPersistableDigest(string? digest)
    {
        return string.IsNullOrWhiteSpace(digest) == false;
    }

    /// <summary>
    /// Normalize an optional candidate digest for persistence into non-null database columns
    /// </summary>
    /// <param name="digest">Candidate digest</param>
    /// <returns>Normalized digest</returns>
    public static string NormalizeCandidateDigest(string? digest)
    {
        return string.IsNullOrWhiteSpace(digest) ? string.Empty : digest.Trim();
    }

    #endregion // Methods
}