namespace DockerUpdateGuard.Images;

/// <summary>
/// Helper for resolving semantic version tags behind alias tags
/// </summary>
public static class VersionTagResolutionHelper
{
    #region Methods

    /// <summary>
    /// Resolve the semantic version tag behind an alias tag with the same digest
    /// </summary>
    /// <param name="currentTag">Current tag</param>
    /// <param name="currentDigest">Current digest</param>
    /// <param name="candidates">Available candidates</param>
    /// <returns>Resolved semantic version tag or null</returns>
    public static string? ResolveAliasVersionTag(string currentTag,
                                                 string? currentDigest,
                                                 IEnumerable<VersionTagCandidateData> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentTag);
        ArgumentNullException.ThrowIfNull(candidates);

        if (TryParseVersionTag(currentTag, out _))
        {
            return null;
        }

        var candidateList = candidates.Where(entity => string.IsNullOrWhiteSpace(entity.Tag) == false)
                                      .ToList();
        var currentTagCandidate = candidateList.FirstOrDefault(entity => string.Equals(entity.Tag,
                                                                                       currentTag,
                                                                                       StringComparison.OrdinalIgnoreCase)
                                                                         && string.Equals(entity.Digest ?? string.Empty,
                                                                                          currentDigest ?? string.Empty,
                                                                                          StringComparison.OrdinalIgnoreCase))
                                      ?? candidateList.FirstOrDefault(entity => string.Equals(entity.Tag,
                                                                                              currentTag,
                                                                                              StringComparison.OrdinalIgnoreCase));
        var digest = string.IsNullOrWhiteSpace(currentDigest) == false
                         ? currentDigest
                         : currentTagCandidate?.Digest;

        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        return candidateList.Where(entity => string.Equals(entity.Digest,
                                                           digest,
                                                           StringComparison.OrdinalIgnoreCase)
                                             && string.Equals(entity.Tag,
                                                              currentTag,
                                                              StringComparison.OrdinalIgnoreCase) == false
                                             && TryParseVersionTag(entity.Tag,
                                                                   out _))
                            .Select(entity => new
                                              {
                                                  Candidate = entity,
                                                  Version = ParseVersionTag(entity.Tag),
                                              })
                            .OrderByDescending(entity => entity.Candidate.PublishedAtUtc)
                            .ThenByDescending(entity => entity.Version)
                            .Select(entity => entity.Candidate.Tag)
                            .FirstOrDefault();
    }

    /// <summary>
    /// Resolve a version tag suitable for UI display
    /// </summary>
    /// <param name="tag">Tag to display</param>
    /// <param name="digest">Digest to display</param>
    /// <param name="candidates">Available candidates</param>
    /// <returns>Displayable semantic version tag or null</returns>
    public static string? ResolveDisplayVersionTag(string tag,
                                                   string? digest,
                                                   IEnumerable<VersionTagCandidateData> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentNullException.ThrowIfNull(candidates);

        return TryParseVersionTag(tag, out _)
                   ? tag
                   : ResolveAliasVersionTag(tag, digest, candidates);
    }

    /// <summary>
    /// Attempt to parse a semantic version tag
    /// </summary>
    /// <param name="value">Candidate tag value</param>
    /// <param name="version">Parsed version</param>
    /// <returns>True when parsing succeeded</returns>
    public static bool TryParseVersionTag(string value, out Version version)
    {
        version = new Version();

        var normalized = value.Trim().TrimStart('v', 'V');

        if (Version.TryParse(normalized, out var parsedVersion)
            && parsedVersion is not null)
        {
            version = parsedVersion;

            return true;
        }

        version = new Version();

        return false;
    }

    /// <summary>
    /// Parse a semantic version tag
    /// </summary>
    /// <param name="value">Candidate tag value</param>
    /// <returns>Parsed version</returns>
    public static Version ParseVersionTag(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V');

        return Version.Parse(normalized);
    }

    #endregion // Methods
}