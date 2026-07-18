using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Enums;
using DockerUpdateGuard.Images.Helper;
using DockerUpdateGuard.Images.Interfaces;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Default update detection service
/// </summary>
public class UpdateDetectionService : IUpdateDetectionService
{
    #region Const fields

    /// <summary>
    /// Maximum number of tag candidates returned to the UI and persistence layers
    /// </summary>
    private const int MaxCandidateCount = 50;

    #endregion // Const fields

    #region Methods

    /// <summary>
    /// Get higher semantic version candidates than the current version
    /// </summary>
    /// <param name="orderedTags">Ordered available tags</param>
    /// <param name="currentTag">Current semantic tag or resolved exact tag</param>
    /// <param name="currentPublishedAtUtc">Current tag publication timestamp</param>
    /// <returns>Higher semantic version candidates</returns>
    private static List<(DockerHubTagData Tag, Version Version)> GetHigherVersionCandidates(IReadOnlyList<DockerHubTagData> orderedTags,
                                                                                            string currentTag,
                                                                                            DateTimeOffset? currentPublishedAtUtc)
    {
        var currentIsPreRelease = VersionTagResolutionHelper.IsPreReleaseVersionTag(currentTag);

        return orderedTags.Where(tag => VersionTagResolutionHelper.TryCompareVersionTags(tag.Tag,
                                                                                         currentTag,
                                                                                         out var comparison)
                                        && comparison > 0
                                        && (currentIsPreRelease
                                            || VersionTagResolutionHelper.IsPreReleaseVersionTag(tag.Tag) == false)
                                        && IsCandidatePublishedAfterBaseline(tag.PublishedAtUtc, currentPublishedAtUtc))
                          .Select(tag => (Tag: tag, Version: ParseVersion(tag.Tag)))
                          .OrderByDescending(entity => entity.Tag.Tag, Comparer<string>.Create((left, right) => VersionTagResolutionHelper.TryCompareVersionTags(left, right, out var comparison) ? comparison : 0))
                          .ThenByDescending(entity => entity.Tag.PublishedAtUtc)
                          .ToList();
    }

    /// <summary>
    /// Get higher year-prefixed candidates than the current tag
    /// </summary>
    /// <param name="orderedTags">Ordered available tags</param>
    /// <param name="currentTag">Current tag</param>
    /// <param name="currentYear">Current major year</param>
    /// <param name="currentPublishedAtUtc">Current tag publication timestamp</param>
    /// <returns>Higher year-based candidates</returns>
    private static List<(DockerHubTagData Tag, int Year, string Suffix)> GetHigherYearPrefixedCandidates(IReadOnlyList<DockerHubTagData> orderedTags,
                                                                                                         string currentTag,
                                                                                                         int currentYear,
                                                                                                         DateTimeOffset? currentPublishedAtUtc)
    {
        return orderedTags.Where(tag => TryParseYearPrefixedVersion(tag.Tag, out var tagYear, out _)
                                        && tagYear == currentYear
                                        && VersionTagResolutionHelper.CompareYearPrefixedTags(tag.Tag, currentTag) > 0
                                        && IsCandidatePublishedAfterBaseline(tag.PublishedAtUtc, currentPublishedAtUtc))
                          .Select(tag =>
                                  {
                                      TryParseYearPrefixedVersion(tag.Tag, out var tagYear, out var suffix);

                                      return (Tag: tag, Year: tagYear, Suffix: suffix);
                                  })
                          .OrderByDescending(entity => entity.Tag.Tag, Comparer<string>.Create((left, right) => VersionTagResolutionHelper.CompareYearPrefixedTags(left, right)))
                          .ThenByDescending(entity => entity.Tag.PublishedAtUtc)
                          .ToList();
    }

    /// <summary>
    /// Create an update result for semantic version successors
    /// </summary>
    /// <param name="versionCandidates">Higher semantic version candidates</param>
    /// <param name="resolvedCurrentVersionCandidate">Optional current resolved version candidate</param>
    /// <returns>Update evaluation result</returns>
    private static UpdateEvaluationResult CreateSemanticVersionUpdateResult(IReadOnlyList<(DockerHubTagData Tag, Version Version)> versionCandidates,
                                                                            UpdateCandidateData? resolvedCurrentVersionCandidate = null)
    {
        var recommended = versionCandidates[0].Tag;
        var candidates = versionCandidates.Take(resolvedCurrentVersionCandidate is null ? MaxCandidateCount : MaxCandidateCount - 1)
                                          .Select(entity => new UpdateCandidateData
                                                            {
                                                                Tag = entity.Tag.Tag,
                                                                Digest = entity.Tag.Digest,
                                                                PublishedAtUtc = entity.Tag.PublishedAtUtc,
                                                            })
                                          .ToList();

        if (resolvedCurrentVersionCandidate is not null
            && candidates.Any(entity => string.Equals(entity.Tag,
                                                      resolvedCurrentVersionCandidate.Tag,
                                                      StringComparison.OrdinalIgnoreCase)) == false)
        {
            candidates.Add(resolvedCurrentVersionCandidate);
        }

        return new UpdateEvaluationResult
               {
                   Status = UpdateEvaluationStatus.UpdateAvailable,
                   Summary = $"Newer tag '{recommended.Tag}' is available",
                   RecommendedTag = recommended.Tag,
                   RecommendedDigest = recommended.Digest,
                   Candidates = candidates,
               };
    }

    /// <summary>
    /// Create an update result for year-prefixed successors
    /// </summary>
    /// <param name="versionCandidates">Higher year-based candidates</param>
    /// <returns>Update evaluation result</returns>
    private static UpdateEvaluationResult CreateYearPrefixedUpdateResult(IReadOnlyList<(DockerHubTagData Tag, int Year, string Suffix)> versionCandidates)
    {
        var recommended = versionCandidates[0].Tag;

        return new UpdateEvaluationResult
               {
                   Status = UpdateEvaluationStatus.UpdateAvailable,
                   Summary = $"Newer tag '{recommended.Tag}' is available",
                   RecommendedTag = recommended.Tag,
                   RecommendedDigest = recommended.Digest,
                   Candidates = versionCandidates.Take(MaxCandidateCount)
                                                 .Select(entity => new UpdateCandidateData
                                                                   {
                                                                       Tag = entity.Tag.Tag,
                                                                       Digest = entity.Tag.Digest,
                                                                       PublishedAtUtc = entity.Tag.PublishedAtUtc,
                                                                   })
                                                 .ToList(),
               };
    }

    /// <summary>
    /// Parse a semantic version string
    /// </summary>
    /// <param name="value">Candidate version string</param>
    /// <param name="version">Parsed version</param>
    /// <returns>True when parsing succeeded</returns>
    private static bool TryParseVersion(string value, out Version version)
    {
        return VersionTagResolutionHelper.TryParseVersionTag(value, out version);
    }

    /// <summary>
    /// Parse a semantic version string
    /// </summary>
    /// <param name="value">Candidate version string</param>
    /// <returns>Parsed version</returns>
    private static Version ParseVersion(string value)
    {
        return VersionTagResolutionHelper.ParseVersionTag(value);
    }

    /// <summary>
    /// Attempt to parse a year-prefixed tag
    /// </summary>
    /// <param name="value">Candidate tag value</param>
    /// <param name="year">Parsed major year</param>
    /// <param name="suffix">Parsed suffix</param>
    /// <returns>True when parsing succeeded</returns>
    private static bool TryParseYearPrefixedVersion(string value,
                                                    out int year,
                                                    out string suffix)
    {
        return VersionTagResolutionHelper.TryParseYearPrefixedTag(value, out year, out suffix);
    }

    /// <summary>
    /// Create an update result for a changed digest on the current tag
    /// </summary>
    /// <param name="currentImage">Current image reference</param>
    /// <param name="currentTagData">Current tag metadata</param>
    /// <param name="orderedTags">Ordered repository tags</param>
    /// <param name="result">Update result</param>
    /// <returns>True when the digest changed</returns>
    private static bool TryCreateDigestUpdateResult(ImageReference currentImage,
                                                    DockerHubTagData? currentTagData,
                                                    IReadOnlyList<DockerHubTagData> orderedTags,
                                                    out UpdateEvaluationResult result)
    {
        result = new UpdateEvaluationResult();

        if (currentTagData is null
            || string.IsNullOrWhiteSpace(currentImage.Digest)
            || string.IsNullOrWhiteSpace(currentTagData.Digest)
            || string.Equals(currentImage.Digest,
                             currentTagData.Digest,
                             StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidateTags = CreateDigestCandidates(currentImage.Tag,
                                                   currentTagData.Digest,
                                                   orderedTags);

        result = new UpdateEvaluationResult
                 {
                     Status = UpdateEvaluationStatus.UpdateAvailable,
                     Summary = "Update available",
                     Details = $"A newer image is available for tag '{currentImage.Tag}'",
                     RecommendedTag = currentImage.Tag,
                     RecommendedDigest = currentTagData.Digest,
                     Candidates = candidateTags,
                 };

        return true;
    }

    /// <summary>
    /// Resolve the current semantic version from the running digest
    /// </summary>
    /// <param name="currentImage">Current image reference</param>
    /// <param name="orderedTags">Ordered available tags</param>
    /// <param name="resolvedVersionTagData">Resolved semantic version tag metadata</param>
    /// <param name="resolvedVersion">Resolved semantic version value</param>
    /// <returns>True when the running digest maps to a semantic version tag</returns>
    private static bool TryResolveCurrentVersionFromDigest(ImageReference currentImage,
                                                           IReadOnlyList<DockerHubTagData> orderedTags,
                                                           out DockerHubTagData? resolvedVersionTagData,
                                                           out Version resolvedVersion)
    {
        resolvedVersionTagData = null;
        resolvedVersion = new Version();

        if (string.IsNullOrWhiteSpace(currentImage.Digest))
        {
            return false;
        }

        var matchingSemanticCandidates = orderedTags.Where(tag => string.Equals(tag.Digest,
                                                                                currentImage.Digest,
                                                                                StringComparison.OrdinalIgnoreCase)
                                                                  && TryParseVersion(tag.Tag, out _)
                                                                  && (VersionTagResolutionHelper.IsMatchingVersionLineTag(currentImage.Tag, tag.Tag)
                                                                      || TryParseVersion(currentImage.Tag, out _)
                                                                      || TryParseYearPrefixedVersion(currentImage.Tag, out _, out _)))
                                                    .Select(tag => new
                                                                   {
                                                                       Tag = tag,
                                                                       Version = ParseVersion(tag.Tag),
                                                                   })
                                                    .OrderByDescending(entity => entity.Tag.PublishedAtUtc)
                                                    .ThenByDescending(entity => entity.Version)
                                                    .ToList();

        if (matchingSemanticCandidates.Count == 0)
        {
            return false;
        }

        resolvedVersionTagData = matchingSemanticCandidates[0].Tag;
        resolvedVersion = matchingSemanticCandidates[0].Version;

        return true;
    }

    /// <summary>
    /// Create a candidate representing the currently resolved semantic version
    /// </summary>
    /// <param name="resolvedVersionTagData">Resolved semantic version tag metadata</param>
    /// <returns>Candidate or null when the tag cannot be found</returns>
    private static UpdateCandidateData? CreateResolvedCurrentVersionCandidate(DockerHubTagData? resolvedVersionTagData)
    {
        if (resolvedVersionTagData is null)
        {
            return null;
        }

        return new UpdateCandidateData
               {
                   Tag = resolvedVersionTagData.Tag,
                   Digest = resolvedVersionTagData.Digest,
                   PublishedAtUtc = resolvedVersionTagData.PublishedAtUtc,
               };
    }

    /// <summary>
    /// Determine whether the current digest matches the registry digest for the tag
    /// </summary>
    /// <param name="currentDigest">Current running digest</param>
    /// <param name="tagDigest">Registry digest for the tag</param>
    /// <returns>True when the digests match</returns>
    private static bool CurrentDigestMatches(string? currentDigest, string? tagDigest)
    {
        return string.IsNullOrWhiteSpace(currentDigest) == false
               && string.IsNullOrWhiteSpace(tagDigest) == false
               && string.Equals(currentDigest, tagDigest, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Create digest-related candidates for the updated tag
    /// </summary>
    /// <param name="currentTag">Current tag</param>
    /// <param name="digest">Updated digest</param>
    /// <param name="orderedTags">Ordered repository tags</param>
    /// <returns>Candidate list</returns>
    private static IReadOnlyList<UpdateCandidateData> CreateDigestCandidates(string currentTag,
                                                                             string digest,
                                                                             IReadOnlyList<DockerHubTagData> orderedTags)
    {
        return orderedTags.Where(tag => string.Equals(tag.Digest, digest, StringComparison.OrdinalIgnoreCase))
                          .OrderBy(tag => string.Equals(tag.Tag, currentTag, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                          .ThenBy(tag => VersionTagResolutionHelper.IsMatchingVersionLineTag(currentTag, tag.Tag) ? 0 : 1)
                          .ThenBy(tag => TryParseVersion(tag.Tag, out _) ? 0 : 1)
                          .ThenByDescending(tag => tag.PublishedAtUtc)
                          .ThenByDescending(tag => TryParseVersion(tag.Tag, out var tagVersion) ? tagVersion : new Version())
                          .Take(MaxCandidateCount)
                          .Select(tag => new UpdateCandidateData
                                         {
                                             Tag = tag.Tag,
                                             Digest = tag.Digest,
                                             PublishedAtUtc = tag.PublishedAtUtc,
                                         })
                          .ToList();
    }

    /// <summary>
    /// Determine whether a candidate was published after the current baseline
    /// </summary>
    /// <param name="candidatePublishedAtUtc">Candidate publication timestamp</param>
    /// <param name="baselinePublishedAtUtc">Current tag publication timestamp</param>
    /// <returns>True when the candidate is newer or when the timestamps cannot be compared</returns>
    private static bool IsCandidatePublishedAfterBaseline(DateTimeOffset? candidatePublishedAtUtc, DateTimeOffset? baselinePublishedAtUtc)
    {
        return baselinePublishedAtUtc is null
               || candidatePublishedAtUtc is null
               || candidatePublishedAtUtc > baselinePublishedAtUtc;
    }

    /// <summary>
    /// Evaluate an update for a semantic version tag
    /// </summary>
    /// <param name="currentImage">Current image reference</param>
    /// <param name="currentTagData">Tag metadata of the current tag</param>
    /// <param name="orderedTags">Available tags ordered by publication date</param>
    /// <returns>Evaluation result, or null when the current tag is not a semantic version</returns>
    private UpdateEvaluationResult? EvaluateSemanticVersionUpdate(ImageReference currentImage,
                                                                  DockerHubTagData? currentTagData,
                                                                  IReadOnlyList<DockerHubTagData> orderedTags)
    {
        if (TryParseVersion(currentImage.Tag, out _) == false)
        {
            return null;
        }

        var versionCandidates = GetHigherVersionCandidates(orderedTags,
                                                           currentImage.Tag,
                                                           currentTagData?.PublishedAtUtc);

        if (versionCandidates.Count > 0)
        {
            return CreateSemanticVersionUpdateResult(versionCandidates);
        }

        if (TryCreateDigestUpdateResult(currentImage,
                                        currentTagData,
                                        orderedTags,
                                        out var digestUpdateResult))
        {
            return digestUpdateResult;
        }

        return new UpdateEvaluationResult
               {
                   Status = UpdateEvaluationStatus.UpToDate,
                   Summary = "No newer semantic version was found",
               };
    }

    /// <summary>
    /// Evaluate an update for a year-prefixed version tag
    /// </summary>
    /// <param name="currentImage">Current image reference</param>
    /// <param name="currentTagData">Tag metadata of the current tag</param>
    /// <param name="orderedTags">Available tags ordered by publication date</param>
    /// <returns>Evaluation result, or null when the current tag is not a year-prefixed version</returns>
    private UpdateEvaluationResult? EvaluateYearPrefixedUpdate(ImageReference currentImage,
                                                               DockerHubTagData? currentTagData,
                                                               IReadOnlyList<DockerHubTagData> orderedTags)
    {
        if (TryParseYearPrefixedVersion(currentImage.Tag, out var currentYear, out _) == false)
        {
            return null;
        }

        var versionCandidates = GetHigherYearPrefixedCandidates(orderedTags,
                                                                currentImage.Tag,
                                                                currentYear,
                                                                currentTagData?.PublishedAtUtc);

        if (versionCandidates.Count > 0)
        {
            return CreateYearPrefixedUpdateResult(versionCandidates);
        }

        if (TryCreateDigestUpdateResult(currentImage,
                                        currentTagData,
                                        orderedTags,
                                        out var digestUpdateResult))
        {
            return digestUpdateResult;
        }

        return new UpdateEvaluationResult
               {
                   Status = UpdateEvaluationStatus.UpToDate,
                   Summary = "No newer year-based version was found",
               };
    }

    /// <summary>
    /// Evaluate an update for a tag whose running digest matches a concrete version tag
    /// </summary>
    /// <param name="currentImage">Current image reference</param>
    /// <param name="currentTagData">Tag metadata of the current tag</param>
    /// <param name="orderedTags">Available tags ordered by publication date</param>
    /// <returns>Evaluation result, or null when the running digest cannot be mapped to a version tag</returns>
    private UpdateEvaluationResult? EvaluateResolvedDigestUpdate(ImageReference currentImage,
                                                                 DockerHubTagData? currentTagData,
                                                                 IReadOnlyList<DockerHubTagData> orderedTags)
    {
        if (TryResolveCurrentVersionFromDigest(currentImage,
                                               orderedTags,
                                               out var resolvedVersionTagData,
                                               out _) == false)
        {
            return null;
        }

        var versionCandidates = GetHigherVersionCandidates(orderedTags,
                                                           resolvedVersionTagData!.Tag,
                                                           resolvedVersionTagData.PublishedAtUtc ?? currentTagData?.PublishedAtUtc);

        if (versionCandidates.Count > 0)
        {
            return CreateSemanticVersionUpdateResult(versionCandidates,
                                                     CreateResolvedCurrentVersionCandidate(resolvedVersionTagData));
        }

        return new UpdateEvaluationResult
               {
                   Status = UpdateEvaluationStatus.UpToDate,
                   Summary = $"The running digest matches version tag '{resolvedVersionTagData!.Tag}'",
                   Details = "No newer semantic version was found",
               };
    }

    #endregion // Methods

    #region IUpdateDetectionService

    /// <inheritdoc/>
    public UpdateEvaluationResult Evaluate(ImageReference currentImage, IReadOnlyList<DockerHubTagData> availableTags)
    {
        ArgumentNullException.ThrowIfNull(currentImage);
        ArgumentNullException.ThrowIfNull(availableTags);

        if (availableTags.Count == 0)
        {
            return new UpdateEvaluationResult
                   {
                       Status = UpdateEvaluationStatus.Unknown,
                       Summary = "No registry tag information is available",
                   };
        }

        var orderedTags = availableTags.Where(tag => string.IsNullOrWhiteSpace(tag.Tag) == false)
                                       .GroupBy(tag => tag.Tag, StringComparer.OrdinalIgnoreCase)
                                       .Select(group => group.OrderByDescending(item => item.PublishedAtUtc)
                                                             .First())
                                       .OrderByDescending(tag => tag.PublishedAtUtc)
                                       .ToList();
        var currentTagData = orderedTags.FirstOrDefault(tag => string.Equals(tag.Tag,
                                                                             currentImage.Tag,
                                                                             StringComparison.OrdinalIgnoreCase));

        if (string.Equals(currentImage.Tag, "latest", StringComparison.OrdinalIgnoreCase)
            && CurrentDigestMatches(currentImage.Digest, currentTagData?.Digest))
        {
            return new UpdateEvaluationResult
                   {
                       Status = UpdateEvaluationStatus.UpToDate,
                       Summary = "The running image already matches the current 'latest' tag",
                   };
        }

        var semanticVersionResult = EvaluateSemanticVersionUpdate(currentImage, currentTagData, orderedTags);

        if (semanticVersionResult is not null)
        {
            return semanticVersionResult;
        }

        var yearPrefixedResult = EvaluateYearPrefixedUpdate(currentImage, currentTagData, orderedTags);

        if (yearPrefixedResult is not null)
        {
            return yearPrefixedResult;
        }

        if (TryCreateDigestUpdateResult(currentImage,
                                        currentTagData,
                                        orderedTags,
                                        out var currentTagDigestUpdate))
        {
            return currentTagDigestUpdate;
        }

        var resolvedDigestResult = EvaluateResolvedDigestUpdate(currentImage, currentTagData, orderedTags);

        if (resolvedDigestResult is not null)
        {
            return resolvedDigestResult;
        }

        var reviewCandidates = orderedTags.Where(tag => string.Equals(tag.Tag,
                                                                      currentImage.Tag,
                                                                      StringComparison.OrdinalIgnoreCase) == false
                                                        && string.IsNullOrWhiteSpace(tag.Digest) == false
                                                        && IsCandidatePublishedAfterBaseline(tag.PublishedAtUtc, currentTagData?.PublishedAtUtc))
                                          .Take(MaxCandidateCount)
                                          .Select(tag => new UpdateCandidateData
                                                         {
                                                             Tag = tag.Tag,
                                                             Digest = tag.Digest,
                                                             PublishedAtUtc = tag.PublishedAtUtc,
                                                         })
                                          .ToList();

        if (reviewCandidates.Count > 0)
        {
            return new UpdateEvaluationResult
                   {
                       Status = UpdateEvaluationStatus.NeedsReview,
                       Summary = "Alternative tags are available and require manual review",
                       Details = currentTagData?.PublishedAtUtc is null
                                     ? "The current tag is not semantic, so an automatic successor cannot be selected reliably"
                                     : $"The current tag was last published at {currentTagData.PublishedAtUtc:O}",
                       Candidates = reviewCandidates,
                   };
        }

        return new UpdateEvaluationResult
               {
                   Status = UpdateEvaluationStatus.UpToDate,
                   Summary = "No newer tags were identified",
               };
    }

    #endregion // IUpdateDetectionService
}