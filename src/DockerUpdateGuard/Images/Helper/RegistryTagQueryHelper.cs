using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Helper;

namespace DockerUpdateGuard.Images.Helper;

/// <summary>
/// Helper for bounded registry tag scans
/// </summary>
public static class RegistryTagQueryHelper
{
    #region Methods

    /// <summary>
    /// Determine whether a Docker Hub page scan can stop after the current tag
    /// </summary>
    /// <param name="tagData">Current tag data</param>
    /// <param name="queryOptions">Query options</param>
    /// <param name="resolvedCurrentVersionTag">Whether a version tag for the current digest was found</param>
    /// <returns>True when the scan can stop</returns>
    public static bool CanStopDockerHubScan(DockerHubTagData tagData,
                                            RegistryTagQueryOptions? queryOptions,
                                            bool resolvedCurrentVersionTag)
    {
        ArgumentNullException.ThrowIfNull(tagData);

        if (queryOptions?.PublishedSinceUtc is null
            || tagData.PublishedAtUtc is null
            || tagData.PublishedAtUtc >= queryOptions.PublishedSinceUtc.Value)
        {
            return false;
        }

        return string.Equals(queryOptions.CurrentTag,
                             "latest",
                             StringComparison.OrdinalIgnoreCase) == false
               || string.IsNullOrWhiteSpace(queryOptions.CurrentDigest)
               || resolvedCurrentVersionTag;
    }

    /// <summary>
    /// Determine whether a tag can be skipped before detailed metadata is requested
    /// </summary>
    /// <param name="tag">Candidate tag</param>
    /// <param name="queryOptions">Query options</param>
    /// <returns>True when metadata should be requested</returns>
    public static bool ShouldInspectTagName(string tag, RegistryTagQueryOptions? queryOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        if (queryOptions is null)
        {
            return true;
        }

        if (string.Equals(tag,
                          queryOptions.CurrentTag,
                          StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(queryOptions.VersionLineTag) == false
            && VersionTagResolutionHelper.IsMatchingVersionLineTag(queryOptions.VersionLineTag,
                                                                   tag) == false)
        {
            return false;
        }

        return ShouldKeepVersionCandidate(tag, queryOptions.MinimumVersionTag);
    }

    /// <summary>
    /// Determine whether tag metadata remains relevant after retrieval
    /// </summary>
    /// <param name="tagData">Candidate tag metadata</param>
    /// <param name="queryOptions">Query options</param>
    /// <returns>True when the tag should be kept</returns>
    public static bool ShouldKeepTag(DockerHubTagData tagData, RegistryTagQueryOptions? queryOptions)
    {
        ArgumentNullException.ThrowIfNull(tagData);

        if (queryOptions is null)
        {
            return true;
        }

        if (string.Equals(tagData.Tag,
                          queryOptions.CurrentTag,
                          StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(queryOptions.VersionLineTag) == false
            && VersionTagResolutionHelper.IsMatchingVersionLineTag(queryOptions.VersionLineTag,
                                                                   tagData.Tag) == false)
        {
            return false;
        }

        var matchesCurrentDigest = string.IsNullOrWhiteSpace(queryOptions.CurrentDigest) == false
                                   && string.Equals(tagData.Digest,
                                                    queryOptions.CurrentDigest,
                                                    StringComparison.OrdinalIgnoreCase);

        if (queryOptions.PublishedSinceUtc is not null
            && tagData.PublishedAtUtc is not null
            && tagData.PublishedAtUtc < queryOptions.PublishedSinceUtc.Value
            && matchesCurrentDigest == false)
        {
            return false;
        }

        if (ShouldKeepVersionCandidate(tagData.Tag,
                                       queryOptions.MinimumVersionTag) == false
            && matchesCurrentDigest == false)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determine whether a display-version candidate is not below the configured minimum
    /// </summary>
    /// <param name="tag">Candidate tag</param>
    /// <param name="minimumVersionTag">Minimum version tag</param>
    /// <returns>True when the candidate should be kept</returns>
    public static bool ShouldKeepVersionCandidate(string? tag, string? minimumVersionTag)
    {
        if (string.IsNullOrWhiteSpace(tag)
            || string.IsNullOrWhiteSpace(minimumVersionTag)
            || VersionTagResolutionHelper.TryCompareDisplayVersionTags(tag,
                                                                       minimumVersionTag,
                                                                       out var comparison) == false)
        {
            return true;
        }

        return comparison >= 0;
    }

    #endregion // Methods
}