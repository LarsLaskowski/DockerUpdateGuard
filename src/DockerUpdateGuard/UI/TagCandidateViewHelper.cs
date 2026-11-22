using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Helper;

namespace DockerUpdateGuard.UI;

/// <summary>
/// Filters and resolves tag candidates for UI views
/// </summary>
internal static class TagCandidateViewHelper
{
    #region Constants

    /// <summary>
    /// Maximum number of tag candidates exposed to detail views
    /// </summary>
    private const int MaxAvailableTagCandidates = 50;

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Filter visible tag candidates for UI display
    /// </summary>
    /// <param name="candidates">Candidates to filter</param>
    /// <param name="take">Maximum number of candidates</param>
    /// <returns>Filtered candidates</returns>
    internal static List<TagCandidateViewData> FilterVisibleTagCandidates(IEnumerable<TagCandidateViewData> candidates, int take = MaxAvailableTagCandidates)
    {
        return candidates.Where(entity => string.IsNullOrWhiteSpace(entity.Tag) == false
                                          && UpdateFindingPersistenceHelper.HasPersistableDigest(entity.Digest))
                         .GroupBy(entity => CreateTagCandidateKey(entity.Tag, entity.Digest!),
                                  StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.OrderByDescending(entity => entity.IsSelected)
                                               .ThenByDescending(entity => entity.IsRecommended)
                                               .ThenByDescending(entity => entity.PublishedAtUtc)
                                               .ThenBy(entity => entity.Tag, StringComparer.OrdinalIgnoreCase)
                                               .First())
                         .Take(take)
                         .ToList();
    }

    /// <summary>
    /// Filter runtime tag candidates against the current image baseline
    /// </summary>
    /// <param name="candidates">Visible candidates</param>
    /// <param name="currentTag">Current image tag</param>
    /// <param name="resolvedVersionTag">Resolved current version tag</param>
    /// <param name="currentPublishedAtUtc">Current image publication timestamp</param>
    /// <returns>Filtered candidates</returns>
    internal static List<TagCandidateViewData> FilterRuntimeAvailableTagCandidates(IEnumerable<TagCandidateViewData> candidates,
                                                                                   string currentTag,
                                                                                   string? resolvedVersionTag,
                                                                                   DateTimeOffset? currentPublishedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentTag);
        ArgumentNullException.ThrowIfNull(candidates);

        var currentComparableTag = ResolveComparableVersionTag(currentTag, resolvedVersionTag);

        return candidates.Where(entity => IsCandidatePublishedOnOrAfterBaseline(entity.PublishedAtUtc, currentPublishedAtUtc)
                                          && HasSameOrNewerComparableVersion(entity, currentComparableTag))
                         .ToList();
    }

    /// <summary>
    /// Populate resolved semantic version tags for alias candidates
    /// </summary>
    /// <param name="candidates">Tag candidates</param>
    internal static void PopulateResolvedVersionTags(IList<TagCandidateViewData> candidates)
    {
        var versionCandidates = candidates.Select(entity => new VersionTagCandidateData
                                                            {
                                                                Tag = entity.Tag,
                                                                Digest = entity.Digest,
                                                                PublishedAtUtc = entity.PublishedAtUtc,
                                                            })
                                          .ToList();

        foreach (var candidate in candidates)
        {
            candidate.ResolvedVersionTag = VersionTagResolutionHelper.ResolveAliasVersionTag(candidate.Tag,
                                                                                             candidate.Digest,
                                                                                             versionCandidates);
        }
    }

    /// <summary>
    /// Resolve the semantic version behind an alias tag with the same digest
    /// </summary>
    /// <param name="currentTag">Current tag</param>
    /// <param name="currentDigest">Current digest</param>
    /// <param name="candidates">Available candidates</param>
    /// <returns>Resolved semantic version tag or null</returns>
    internal static string? ResolveResolvedVersionTag(string currentTag,
                                                      string? currentDigest,
                                                      IEnumerable<TagCandidateViewData> candidates)
    {
        return VersionTagResolutionHelper.ResolveAliasVersionTag(currentTag,
                                                                 currentDigest,
                                                                 candidates.Select(entity => new VersionTagCandidateData
                                                                                             {
                                                                                                 Tag = entity.Tag,
                                                                                                 Digest = entity.Digest,
                                                                                                 PublishedAtUtc = entity.PublishedAtUtc,
                                                                                             }));
    }

    /// <summary>
    /// Resolve the available update version tag for display
    /// </summary>
    /// <param name="candidates">Tag candidates</param>
    /// <param name="preferredVariantFamilySourceTag">Optional concrete tag whose variant family is preferred among digest-matching candidates</param>
    /// <returns>Displayable version tag or null</returns>
    internal static string? ResolveAvailableUpdateVersionTag(IEnumerable<TagCandidateViewData> candidates,
                                                             string? preferredVariantFamilySourceTag = null)
    {
        var candidateList = candidates.Where(entity => string.IsNullOrWhiteSpace(entity.Tag) == false)
                                      .ToList();
        var recommendedCandidate = candidateList.FirstOrDefault(entity => entity.IsRecommended);

        if (recommendedCandidate is null)
        {
            return null;
        }

        return VersionTagResolutionHelper.ResolveDisplayVersionTag(recommendedCandidate.Tag,
                                                                   recommendedCandidate.Digest,
                                                                   candidateList.Select(entity => new VersionTagCandidateData
                                                                                                  {
                                                                                                      Tag = entity.Tag,
                                                                                                      Digest = entity.Digest,
                                                                                                      PublishedAtUtc = entity.PublishedAtUtc,
                                                                                                  }),
                                                                   preferredVariantFamilySourceTag);
    }

    /// <summary>
    /// Create a stable identity for a tag candidate
    /// </summary>
    /// <param name="tag">Tag name</param>
    /// <param name="digest">Digest value</param>
    /// <returns>Candidate identity</returns>
    private static string CreateTagCandidateKey(string tag, string digest)
    {
        return $"{tag.Trim().ToLowerInvariant()}|{digest.Trim().ToLowerInvariant()}";
    }

    /// <summary>
    /// Resolve the comparable version tag for filtering
    /// </summary>
    /// <param name="tag">Tag value</param>
    /// <param name="resolvedVersionTag">Resolved version tag</param>
    /// <returns>Comparable version tag or null</returns>
    private static string? ResolveComparableVersionTag(string tag, string? resolvedVersionTag)
    {
        if (string.IsNullOrWhiteSpace(resolvedVersionTag) == false)
        {
            return resolvedVersionTag;
        }

        return VersionTagResolutionHelper.IsDisplayableSpecificVersionTag(tag)
                   ? tag
                   : null;
    }

    /// <summary>
    /// Determine whether a candidate is published on or after the current baseline
    /// </summary>
    /// <param name="candidatePublishedAtUtc">Candidate publication timestamp</param>
    /// <param name="baselinePublishedAtUtc">Baseline publication timestamp</param>
    /// <returns>True when the candidate is not older than the baseline</returns>
    private static bool IsCandidatePublishedOnOrAfterBaseline(DateTimeOffset? candidatePublishedAtUtc, DateTimeOffset? baselinePublishedAtUtc)
    {
        return baselinePublishedAtUtc is null
               || candidatePublishedAtUtc is null
               || candidatePublishedAtUtc >= baselinePublishedAtUtc;
    }

    /// <summary>
    /// Determine whether a candidate has the same or a newer comparable version
    /// </summary>
    /// <param name="candidate">Candidate tag</param>
    /// <param name="currentComparableTag">Current comparable tag</param>
    /// <returns>True when the candidate is not older than the current version</returns>
    private static bool HasSameOrNewerComparableVersion(TagCandidateViewData candidate, string? currentComparableTag)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (string.IsNullOrWhiteSpace(currentComparableTag))
        {
            return true;
        }

        var candidateComparableTag = ResolveComparableVersionTag(candidate.Tag, candidate.ResolvedVersionTag);

        if (string.IsNullOrWhiteSpace(candidateComparableTag))
        {
            return false;
        }

        if (VersionTagResolutionHelper.TryCompareVersionTags(candidateComparableTag,
                                                             currentComparableTag,
                                                             out var versionComparison))
        {
            return versionComparison >= 0;
        }

        if (VersionTagResolutionHelper.TryParseYearPrefixedTag(currentComparableTag, out var currentYear, out _)
            && VersionTagResolutionHelper.TryParseYearPrefixedTag(candidateComparableTag, out var candidateYear, out _))
        {
            return candidateYear == currentYear
                   && VersionTagResolutionHelper.CompareYearPrefixedTags(candidateComparableTag, currentComparableTag) >= 0;
        }

        return false;
    }

    #endregion // Methods
}