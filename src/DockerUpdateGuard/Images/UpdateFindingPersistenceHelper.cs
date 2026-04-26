namespace DockerUpdateGuard.Images;

/// <summary>
/// Helper methods for persisting update findings
/// </summary>
internal static class UpdateFindingPersistenceHelper
{
    #region Methods

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