using DockerUpdateGuard.DockerHub;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Default update detection service
/// </summary>
public class UpdateDetectionService : IUpdateDetectionService
{
    #region Methods

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

        if (TryParseVersion(currentImage.Tag, out var currentVersion))
        {
            var versionCandidates = orderedTags.Where(tag => TryParseVersion(tag.Tag, out var tagVersion) && tagVersion > currentVersion)
                                               .Select(tag => new
                                                              {
                                                                  Tag = tag,
                                                                  Version = ParseVersion(tag.Tag),
                                                              })
                                               .OrderByDescending(entity => entity.Version)
                                               .ToList();

            if (versionCandidates.Count > 0)
            {
                var recommended = versionCandidates[0].Tag;

                return new UpdateEvaluationResult
                       {
                           Status = UpdateEvaluationStatus.UpdateAvailable,
                           Summary = $"Newer tag '{recommended.Tag}' is available",
                           RecommendedTag = recommended.Tag,
                           RecommendedDigest = recommended.Digest,
                           Candidates = versionCandidates.Take(5)
                                                         .Select(entity => new UpdateCandidateData
                                                                           {
                                                                               Tag = entity.Tag.Tag,
                                                                               Digest = entity.Tag.Digest,
                                                                               PublishedAtUtc = entity.Tag.PublishedAtUtc,
                                                                           })
                                                         .ToList(),
                       };
            }

            if (TryCreateDigestUpdateResult(currentImage,
                                           currentTagData,
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

        if (TryCreateDigestUpdateResult(currentImage,
                                       currentTagData,
                                       out var currentTagDigestUpdate))
        {
            return currentTagDigestUpdate;
        }

        var reviewCandidates = orderedTags.Where(tag => string.Equals(tag.Tag,
                                                                      currentImage.Tag,
                                                                      StringComparison.OrdinalIgnoreCase) == false)
                                          .Take(5)
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

    /// <summary>
    /// Parse a semantic version string
    /// </summary>
    /// <param name="value">Candidate version string</param>
    /// <param name="version">Parsed version</param>
    /// <returns>True when parsing succeeded</returns>
    private static bool TryParseVersion(string value, out Version version)
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
    /// Parse a semantic version string
    /// </summary>
    /// <param name="value">Candidate version string</param>
    /// <returns>Parsed version</returns>
    private static Version ParseVersion(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V');

        return Version.Parse(normalized);
    }

    /// <summary>
    /// Create an update result for a changed digest on the current tag
    /// </summary>
    /// <param name="currentImage">Current image reference</param>
    /// <param name="currentTagData">Current tag metadata</param>
    /// <param name="result">Update result</param>
    /// <returns>True when the digest changed</returns>
    private static bool TryCreateDigestUpdateResult(ImageReference currentImage,
                                                    DockerHubTagData? currentTagData,
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

        result = new UpdateEvaluationResult
                 {
                     Status = UpdateEvaluationStatus.UpdateAvailable,
                     Summary = $"Digest for tag '{currentImage.Tag}' changed",
                     Details = $"The registry currently reports digest '{currentTagData.Digest}' for tag '{currentImage.Tag}'",
                     RecommendedTag = currentImage.Tag,
                     RecommendedDigest = currentTagData.Digest,
                     Candidates =
                     [
                         new UpdateCandidateData
                         {
                             Tag = currentTagData.Tag,
                             Digest = currentTagData.Digest,
                             PublishedAtUtc = currentTagData.PublishedAtUtc,
                         },
                     ],
                 };

        return true;
    }

    #endregion // Methods
}