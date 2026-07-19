using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Queries;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Helper;
using DockerUpdateGuard.Images.Interfaces;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.UI;

/// <summary>
/// Default UI query service
/// </summary>
public sealed class ApplicationViewService : IApplicationViewService, IDisposable
{
    #region Fields

    /// <summary>
    /// Maximum number of tag candidates exposed to detail views
    /// </summary>
    private const int MaxAvailableTagCandidates = 50;

    /// <summary>
    /// Maximum number of resource samples shown in runtime list sparklines
    /// </summary>
    private const int RuntimeListResourceHistoryCount = 12;

    /// <summary>
    /// Resource-history window
    /// </summary>
    private static readonly TimeSpan _resourceHistoryWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Database-access gate
    /// </summary>
    private readonly SemaphoreSlim _dbContextLock = new(1, 1);

    /// <summary>
    /// Database context
    /// </summary>
    private readonly DockerUpdateGuardDbContext _dbContext;

    /// <summary>
    /// Image-reference parser
    /// </summary>
    private readonly IImageReferenceParser _imageReferenceParser;

    /// <summary>
    /// Shared-base-image query service
    /// </summary>
    private readonly ISharedBaseImageQueryService _sharedBaseImageQueryService;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="imageReferenceParser">Image reference parser</param>
    /// <param name="sharedBaseImageQueryService">Shared base image query service</param>
    public ApplicationViewService(DockerUpdateGuardDbContext dbContext,
                                  IImageReferenceParser imageReferenceParser,
                                  ISharedBaseImageQueryService sharedBaseImageQueryService)
    {
        _dbContext = dbContext;
        _imageReferenceParser = imageReferenceParser;
        _sharedBaseImageQueryService = sharedBaseImageQueryService;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Map a vulnerability finding to view data
    /// </summary>
    /// <param name="entity">Vulnerability finding entity</param>
    /// <returns>Vulnerability finding view data</returns>
    private static VulnerabilityFindingViewData MapVulnerabilityFinding(VulnerabilityFinding entity)
    {
        return new VulnerabilityFindingViewData
               {
                   AdvisoryId = entity.AdvisoryId,
                   Title = entity.Title,
                   Severity = entity.Severity.ToString(),
                   Source = entity.Source.ToString(),
                   Summary = entity.Summary,
                   AffectedPackage = entity.AffectedPackage,
                   InstalledVersion = entity.InstalledVersion,
                   FixedVersion = entity.FixedVersion,
                   CvssScore = entity.CvssScore,
                   ReferenceUrl = SanitizeHttpUrl(entity.ReferenceUrl),
                   IsActive = entity.IsActive,
                   DetectedAtUtc = entity.DetectedAtUtc,
                   ResolvedAtUtc = entity.ResolvedAtUtc,
               };
    }

    /// <summary>
    /// Sort vulnerability findings for display (active first, then by severity and CVSS score)
    /// </summary>
    /// <param name="findings">Vulnerability findings</param>
    /// <returns>Sorted findings</returns>
    private static List<VulnerabilityFinding> SortFindingsForDisplay(IEnumerable<VulnerabilityFinding> findings)
    {
        return findings.OrderByDescending(entity => entity.IsActive)
                       .ThenByDescending(entity => entity.Severity)
                       .ThenByDescending(entity => entity.CvssScore ?? decimal.MinValue)
                       .ThenBy(entity => entity.AdvisoryId, StringComparer.OrdinalIgnoreCase)
                       .ThenBy(entity => entity.AffectedPackage, StringComparer.OrdinalIgnoreCase)
                       .ToList();
    }

    /// <summary>
    /// Create a severity summary from severity/count pairs
    /// </summary>
    /// <param name="severityCounts">Severity/count pairs</param>
    /// <returns>Severity summary</returns>
    private static VulnerabilitySeveritySummaryViewData CreateSeveritySummary(IEnumerable<KeyValuePair<VulnerabilitySeverity, int>> severityCounts)
    {
        var summary = new VulnerabilitySeveritySummaryViewData();

        foreach (var severityCount in severityCounts)
        {
            switch (severityCount.Key)
            {
                case VulnerabilitySeverity.Critical:
                    {
                        summary.CriticalCount += severityCount.Value;
                    }
                    break;

                case VulnerabilitySeverity.High:
                    {
                        summary.HighCount += severityCount.Value;
                    }
                    break;

                case VulnerabilitySeverity.Medium:
                    {
                        summary.MediumCount += severityCount.Value;
                    }
                    break;

                case VulnerabilitySeverity.Low:
                    {
                        summary.LowCount += severityCount.Value;
                    }
                    break;

                default:
                    {
                        summary.OtherCount += severityCount.Value;
                    }
                    break;
            }
        }

        return summary;
    }

    /// <summary>
    /// Create a severity summary from the active findings of a finding list
    /// </summary>
    /// <param name="findings">Vulnerability findings</param>
    /// <returns>Severity summary</returns>
    private static VulnerabilitySeveritySummaryViewData CreateSeveritySummaryFromFindings(IEnumerable<VulnerabilityFinding> findings)
    {
        return CreateSeveritySummary(findings.Where(entity => entity.IsActive)
                                             .GroupBy(entity => entity.Severity)
                                             .Select(group => new KeyValuePair<VulnerabilitySeverity, int>(group.Key, group.Count())));
    }

    /// <summary>
    /// Resolve a severity summary from a lookup or return an empty summary
    /// </summary>
    /// <param name="summaries">Severity summaries by image version identifier</param>
    /// <param name="key">Image version identifier</param>
    /// <returns>Severity summary</returns>
    private static VulnerabilitySeveritySummaryViewData GetSummaryOrEmpty(Dictionary<Guid, VulnerabilitySeveritySummaryViewData> summaries, Guid key)
    {
        return summaries.TryGetValue(key, out var summary) ? summary : new VulnerabilitySeveritySummaryViewData();
    }

    /// <summary>
    /// Validate an external reference URL before it is exposed to the UI so that only
    /// absolute <c>http</c>/<c>https</c> URLs are kept and anything else (for example a
    /// <c>javascript:</c> scheme) is dropped and can never be rendered as an <c>href</c>
    /// </summary>
    /// <param name="url">Untrusted reference URL from a vulnerability provider</param>
    /// <returns>The URL if it is a safe absolute http(s) URL; otherwise <c>null</c></returns>
    private static string? SanitizeHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) == true)
        {
            return null;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) == false)
        {
            return null;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return url;
    }

    /// <summary>
    /// Create vulnerability assessment view data for an image version
    /// </summary>
    /// <param name="imageVersion">Image version entity</param>
    /// <param name="severitySummary">Severity summary of the active findings</param>
    /// <returns>Vulnerability assessment view data</returns>
    private static VulnerabilityAssessmentViewData CreateVulnerabilityAssessment(ImageVersion imageVersion, VulnerabilitySeveritySummaryViewData severitySummary)
    {
        return new VulnerabilityAssessmentViewData
               {
                   Status = FormatVulnerabilityAssessmentStatus(imageVersion.VulnerabilityAssessmentStatus),
                   Source = FormatVulnerabilitySource(imageVersion.VulnerabilityAssessmentSource),
                   Message = imageVersion.VulnerabilityAssessmentMessage,
                   CheckedAtUtc = imageVersion.VulnerabilityAssessmentCheckedAtUtc,
                   ActiveFindingCount = severitySummary.TotalCount,
                   SeveritySummary = severitySummary,
               };
    }

    /// <summary>
    /// Summarize base-image vulnerability findings for a relationship list
    /// </summary>
    /// <param name="baseImageRelationships">Base-image relationship list</param>
    /// <returns>Active finding count and summary</returns>
    private static (int ActiveFindingCount, string? Summary) SummarizeBaseImageVulnerabilities(IReadOnlyList<BaseImageRelationshipData>? baseImageRelationships)
    {
        if (baseImageRelationships is null || baseImageRelationships.Count == 0)
        {
            return (0, null);
        }

        var groupedAssessments = baseImageRelationships.GroupBy(entity => entity.ImageReference, StringComparer.OrdinalIgnoreCase)
                                                       .Select(group => group.Max(entity => entity.VulnerabilityAssessment.ActiveFindingCount))
                                                       .ToList();
        var activeFindingCount = groupedAssessments.Sum();

        if (activeFindingCount == 0)
        {
            return (0, null);
        }

        var vulnerableBaseImageCount = groupedAssessments.Count(entity => entity > 0);
        var baseImageLabel = vulnerableBaseImageCount == 1 ? "base image" : "base images";

        return (activeFindingCount, $"{activeFindingCount} active finding(s) across {vulnerableBaseImageCount} {baseImageLabel}");
    }

    /// <summary>
    /// Map a Docker-instance resource sample
    /// </summary>
    /// <param name="entity">Docker-instance resource sample</param>
    /// <returns>Resource usage point</returns>
    private static ResourceUsagePointViewData MapResourceUsage(DockerInstanceResourceSample entity)
    {
        return new ResourceUsagePointViewData
               {
                   CpuPercent = entity.CpuPercent,
                   MemoryUsageBytes = entity.MemoryUsageBytes,
                   MemoryLimitBytes = entity.MemoryLimitBytes,
                   NetworkRxBytesPerSecond = entity.NetworkRxBytesPerSecond,
                   NetworkTxBytesPerSecond = entity.NetworkTxBytesPerSecond,
                   RecordedAtUtc = entity.RecordedAtUtc,
               };
    }

    /// <summary>
    /// Map a runtime-container resource sample
    /// </summary>
    /// <param name="entity">Runtime-container resource sample</param>
    /// <returns>Resource usage point</returns>
    private static ResourceUsagePointViewData MapResourceUsage(RuntimeContainerResourceSample entity)
    {
        return new ResourceUsagePointViewData
               {
                   CpuPercent = entity.CpuPercent,
                   MemoryUsageBytes = entity.MemoryUsageBytes,
                   MemoryLimitBytes = entity.MemoryLimitBytes,
                   NetworkRxBytesPerSecond = entity.NetworkRxBytesPerSecond,
                   NetworkTxBytesPerSecond = entity.NetworkTxBytesPerSecond,
                   RecordedAtUtc = entity.RecordedAtUtc,
               };
    }

    /// <summary>
    /// Format an image reference from its parts
    /// </summary>
    /// <param name="registry">Registry host</param>
    /// <param name="repository">Repository path</param>
    /// <param name="tag">Tag name</param>
    /// <param name="digest">Optional digest</param>
    /// <returns>Formatted image reference</returns>
    private static string FormatImageReference(string registry, string repository, string tag, string? digest)
    {
        return string.IsNullOrWhiteSpace(digest)
                   ? $"{registry}/{repository}:{tag}"
                   : $"{registry}/{repository}:{tag}@{digest}";
    }

    /// <summary>
    /// Create a normalized repository key
    /// </summary>
    /// <param name="entity">Container snapshot entity</param>
    /// <returns>Repository key</returns>
    private static string CreateRepositoryKey(ContainerSnapshot entity)
    {
        return CreateRepositoryKey(entity.ImageVersion);
    }

    /// <summary>
    /// Create a normalized repository key
    /// </summary>
    /// <param name="entity">Image version entity</param>
    /// <returns>Repository key</returns>
    private static string CreateRepositoryKey(ImageVersion entity)
    {
        return CreateRepositoryKey(entity.RegistryRepository.Registry, entity.RegistryRepository.Repository);
    }

    /// <summary>
    /// Create a normalized repository key
    /// </summary>
    /// <param name="registry">Registry host</param>
    /// <param name="repository">Repository path</param>
    /// <returns>Repository key</returns>
    private static string CreateRepositoryKey(string registry, string repository)
    {
        return $"{registry.Trim().ToLowerInvariant()}|{repository.Trim().ToLowerInvariant()}";
    }

    /// <summary>
    /// Create a normalized container key
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="containerId">ID of the container</param>
    /// <returns>Container key</returns>
    private static string CreateContainerKey(Guid dockerInstanceId, string containerId)
    {
        return $"{dockerInstanceId:D}|{containerId.Trim()}";
    }

    /// <summary>
    /// Return the most recent resource usage point or null when the history is empty
    /// </summary>
    /// <param name="history">Ordered resource usage history</param>
    /// <returns>Most recent resource usage point or null</returns>
    private static ResourceUsagePointViewData? GetCurrentResourceUsage(IReadOnlyList<ResourceUsagePointViewData>? history)
    {
        return history is { Count: > 0 } ? history[0] : null;
    }

    /// <summary>
    /// Return the count stored for a key or zero when the key is absent
    /// </summary>
    /// <param name="counts">Count lookup keyed by identifier</param>
    /// <param name="key">Identifier to resolve</param>
    /// <returns>Stored count or zero</returns>
    private static int GetCountOrZero(Dictionary<Guid, int> counts, Guid key)
    {
        return counts.TryGetValue(key, out var count) ? count : 0;
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
    /// Filter visible tag candidates for UI display
    /// </summary>
    /// <param name="candidates">Candidates to filter</param>
    /// <param name="take">Maximum number of candidates</param>
    /// <returns>Filtered candidates</returns>
    private static List<TagCandidateViewData> FilterVisibleTagCandidates(IEnumerable<TagCandidateViewData> candidates, int take = MaxAvailableTagCandidates)
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
    private static List<TagCandidateViewData> FilterRuntimeAvailableTagCandidates(IEnumerable<TagCandidateViewData> candidates,
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
    private static void PopulateResolvedVersionTags(IList<TagCandidateViewData> candidates)
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
    private static string? ResolveResolvedVersionTag(string currentTag,
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
    private static string? ResolveAvailableUpdateVersionTag(IEnumerable<TagCandidateViewData> candidates,
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

    /// <summary>
    /// Format an update assessment status for the UI
    /// </summary>
    /// <param name="status">Update assessment status</param>
    /// <returns>Formatted status</returns>
    private static string FormatUpdateAssessmentStatus(UpdateAssessmentStatus status)
    {
        return status switch
               {
                   UpdateAssessmentStatus.UpToDate => "Up to date",
                   UpdateAssessmentStatus.UpdateAvailable => "Update available",
                   UpdateAssessmentStatus.ManualReviewRequired => "Manual review required",
                   UpdateAssessmentStatus.NoTagData => "No tag data",
                   UpdateAssessmentStatus.Unsupported => "Unsupported",
                   UpdateAssessmentStatus.Failed => "Failed",
                   _ => "Not evaluated",
               };
    }

    /// <summary>
    /// Format an update finding type for the UI
    /// </summary>
    /// <param name="type">Finding type</param>
    /// <returns>Formatted type</returns>
    private static string FormatUpdateFindingType(UpdateFindingType type)
    {
        return type switch
               {
                   UpdateFindingType.BaseImageUpdate => "Base image update",
                   UpdateFindingType.RuntimeImageUpdate => "Runtime image update",
                   UpdateFindingType.TagRecommendation => "Tag recommendation",
                   UpdateFindingType.DerivedBaseRuntimeUpdate => "Base runtime update",
                   _ => type.ToString(),
               };
    }

    /// <summary>
    /// Format a vulnerability assessment status for the UI
    /// </summary>
    /// <param name="status">Vulnerability assessment status</param>
    /// <returns>Formatted status</returns>
    private static string FormatVulnerabilityAssessmentStatus(VulnerabilityAssessmentStatus status)
    {
        return status switch
               {
                   VulnerabilityAssessmentStatus.NoFindings => "No findings",
                   VulnerabilityAssessmentStatus.FindingsDetected => "Findings detected",
                   VulnerabilityAssessmentStatus.NotConfigured => "Not configured",
                   VulnerabilityAssessmentStatus.Unsupported => "Unsupported",
                   VulnerabilityAssessmentStatus.Failed => "Failed",
                   _ => "Not scanned",
               };
    }

    /// <summary>
    /// Format a vulnerability source for the UI
    /// </summary>
    /// <param name="source">Vulnerability source</param>
    /// <returns>Formatted source</returns>
    private static string FormatVulnerabilitySource(VulnerabilitySource source)
    {
        return source switch
               {
                   VulnerabilitySource.DockerScout => "Docker Scout",
                   VulnerabilitySource.Trivy => "Trivy",
                   VulnerabilitySource.RegistryAdvisory => "Registry advisory",
                   VulnerabilitySource.Manual => "Manual",
                   _ => "Not set",
               };
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Execute a serialized database read operation for the scoped UI service
    /// </summary>
    /// <typeparam name="T">Result type</typeparam>
    /// <param name="action">Read operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Operation result</returns>
    private async Task<T> ExecuteSerializedAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await _dbContextLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _dbContextLock.Release();
        }
    }

    /// <summary>
    /// Read runtime containers without re-entering the service gate
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Runtime container list</returns>
    private async Task<IReadOnlyList<RuntimeContainerListItemData>> GetRuntimeContainersCoreAsync(CancellationToken cancellationToken)
    {
        var latestSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        var ownImagesByRepository = await LoadOwnImagesByRepositoryAsync(cancellationToken).ConfigureAwait(false);
        var resourceHistories = await GetRuntimeContainerResourceHistoriesAsync(latestSnapshots, cancellationToken).ConfigureAwait(false);
        var baseImageRelationshipsByChildVersion = await LoadBaseImageRelationshipsByChildVersionAsync(latestSnapshots.Select(entity => entity.ImageVersionId)
                                                                                                                      .Distinct()
                                                                                                                      .ToList(),
                                                                                                       cancellationToken).ConfigureAwait(false);
        var latestSnapshotIds = latestSnapshots.Select(entity => entity.Id)
                                               .ToList();

        IReadOnlyList<UpdateFinding> latestFindings = latestSnapshotIds.Count == 0
                                                          ? []
                                                          : await _dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                                                           .Where(entity => entity.ContainerSnapshotId != null
                                                                                                            && latestSnapshotIds.Contains(entity.ContainerSnapshotId.Value))
                                                                                           .AsNoTracking()
                                                                                           .ToListAsync(cancellationToken)
                                                                                           .ConfigureAwait(false);

        var tagCandidatesBySnapshot = latestFindings.Where(entity => entity.ContainerSnapshotId is not null)
                                                    .GroupBy(entity => entity.ContainerSnapshotId!.Value)
                                                    .ToDictionary(group => group.Key,
                                                                  group => group.SelectMany(entity => entity.TagCandidates)
                                                                                .OrderBy(candidate => candidate.Rank)
                                                                                .Select(candidate => new TagCandidateViewData
                                                                                                     {
                                                                                                         Tag = candidate.Tag,
                                                                                                         Digest = candidate.Digest,
                                                                                                         PublishedAtUtc = candidate.PublishedAtUtc,
                                                                                                         Reason = candidate.Reason,
                                                                                                         IsRecommended = candidate.IsRecommended,
                                                                                                     })
                                                                                .ToList());

        var runtimeImageVersionIds = latestSnapshots.Select(entity => entity.ImageVersionId)
                                                    .Distinct()
                                                    .ToList();
        var activeVulnerabilityFindingLookup = await LoadActiveVulnerabilitySeveritySummariesAsync(runtimeImageVersionIds, cancellationToken).ConfigureAwait(false);

        return latestSnapshots.Select(entity =>
                                      {
                                          var dockerInstance = entity.DockerInstance;
                                          var imageVersion = entity.ImageVersion;
                                          var repositoryKey = CreateRepositoryKey(entity);

                                          ArgumentNullException.ThrowIfNull(dockerInstance);
                                          ArgumentNullException.ThrowIfNull(imageVersion);

                                          ownImagesByRepository.TryGetValue(repositoryKey, out var linkedObservedImage);
                                          resourceHistories.TryGetValue(CreateContainerKey(entity.DockerInstanceId, entity.ContainerId), out var resourceUsageHistory);
                                          tagCandidatesBySnapshot.TryGetValue(entity.Id, out var tagCandidates);
                                          baseImageRelationshipsByChildVersion.TryGetValue(entity.ImageVersionId, out var baseImageRelationships);

                                          var currentResourceUsage = GetCurrentResourceUsage(resourceUsageHistory);
                                          var baseImageVulnerabilitySummary = SummarizeBaseImageVulnerabilities(baseImageRelationships);
                                          var availableTagCandidates = tagCandidates ?? [];

                                          var resolvedVersionTag = string.IsNullOrWhiteSpace(entity.ResolvedVersionTag) == false
                                                                       ? entity.ResolvedVersionTag
                                                                       : ResolveResolvedVersionTag(imageVersion.Tag,
                                                                                                   imageVersion.Digest,
                                                                                                   availableTagCandidates);
                                          var availableUpdateVersionTag = string.IsNullOrWhiteSpace(entity.AvailableUpdateVersionTag) == false
                                                                              ? entity.AvailableUpdateVersionTag
                                                                              : ResolveAvailableUpdateVersionTag(availableTagCandidates,
                                                                                                                 resolvedVersionTag ?? imageVersion.Tag);

                                          return new RuntimeContainerListItemData
                                                 {
                                                     DockerInstanceId = entity.DockerInstanceId,
                                                     ContainerId = entity.ContainerId,
                                                     ContainerName = entity.Name,
                                                     DockerInstanceName = dockerInstance.Name,
                                                     ImageReference = _imageReferenceParser.Format(imageVersion),
                                                     CurrentTag = imageVersion.Tag,
                                                     ResolvedVersionTag = resolvedVersionTag,
                                                     RuntimeStatus = entity.Status.ToString(),
                                                     UpdateState = FormatUpdateAssessmentStatus(entity.UpdateAssessmentStatus),
                                                     UpdateSummary = entity.UpdateAssessmentMessage,
                                                     AvailableUpdateVersionTag = availableUpdateVersionTag,
                                                     PortainerAvailable = dockerInstance.PortainerEndpoint is not null && dockerInstance.PortainerEndpoint.IsEnabled,
                                                     ActiveVulnerabilityFindingCount = GetSummaryOrEmpty(activeVulnerabilityFindingLookup, entity.ImageVersionId).TotalCount,
                                                     VulnerabilitySeveritySummary = GetSummaryOrEmpty(activeVulnerabilityFindingLookup, entity.ImageVersionId),
                                                     VulnerabilityStatus = FormatVulnerabilityAssessmentStatus(imageVersion.VulnerabilityAssessmentStatus),
                                                     VulnerabilitySummary = imageVersion.VulnerabilityAssessmentMessage,
                                                     ActiveBaseImageVulnerabilityFindingCount = baseImageVulnerabilitySummary.ActiveFindingCount,
                                                     BaseImageVulnerabilitySummary = baseImageVulnerabilitySummary.Summary,
                                                     RecordedAtUtc = entity.RecordedAtUtc,
                                                     LinkedObservedImageId = linkedObservedImage?.Id,
                                                     LinkedObservedImageName = linkedObservedImage?.Name,
                                                     CurrentResourceUsage = currentResourceUsage,
                                                     ResourceUsageHistory = resourceUsageHistory ?? [],
                                                 };
                                      })
                              .OrderByDescending(entity => entity.LinkedObservedImageId.HasValue)
                              .ThenBy(entity => entity.DockerInstanceName)
                              .ThenBy(entity => entity.ContainerName)
                              .ToList();
    }

    /// <summary>
    /// Read base images without re-entering the service gate
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Base image list</returns>
    private async Task<IReadOnlyList<SharedBaseImageListItemData>> GetBaseImagesCoreAsync(CancellationToken cancellationToken)
    {
        var baseImages = await _sharedBaseImageQueryService.GetBaseImagesAsync(cancellationToken).ConfigureAwait(false);

        if (baseImages.Count == 0)
        {
            return [];
        }

        var groupedBaseImageVersionIds = baseImages.SelectMany(entity => entity.BaseImageVersionIds)
                                                   .Distinct()
                                                   .ToList();
        var baseRelationships = await _dbContext.ImageRelationships.Include(entity => entity.BaseImageVersion)
                                                                   .ThenInclude(entity => entity.RegistryRepository)
                                                                   .Include(entity => entity.ChildImageVersion)
                                                                   .ThenInclude(entity => entity.RegistryRepository)
                                                                   .Where(entity => groupedBaseImageVersionIds.Contains(entity.BaseImageVersionId)
                                                                                    && entity.RelationshipType == ImageRelationshipType.BaseImage)
                                                                   .AsNoTracking()
                                                                   .ToListAsync(cancellationToken)
                                                                   .ConfigureAwait(false);
        var activeFindingLookup = await LoadActiveVulnerabilitySeveritySummariesAsync(groupedBaseImageVersionIds, cancellationToken).ConfigureAwait(false);
        var latestSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);

        return baseImages.Select(entity =>
                                 {
                                     var baseImageVersionIdSet = entity.BaseImageVersionIds.ToHashSet();
                                     HashSet<string>? sourceReferenceSet = null;

                                     if (string.IsNullOrWhiteSpace(entity.Digest)
                                         && entity.SourceReferences.Count > 0)
                                     {
                                         sourceReferenceSet = entity.SourceReferences.ToHashSet(StringComparer.OrdinalIgnoreCase);
                                     }

                                     var relevantRelationships = baseRelationships.Where(relationship => baseImageVersionIdSet.Contains(relationship.BaseImageVersionId)
                                                                                                         && (sourceReferenceSet is null
                                                                                                             || sourceReferenceSet.Contains(relationship.SourceReference ?? string.Empty)))
                                                                                  .ToList();
                                     var relevantBaseImageVersions = relevantRelationships.Select(relationship => relationship.BaseImageVersion)
                                                                                          .GroupBy(relationship => relationship.Id)
                                                                                          .Select(group => group.First())
                                                                                          .ToList();
                                     var representativeBaseImage = relevantBaseImageVersions.OrderByDescending(baseImageVersion => baseImageVersion.VulnerabilityAssessmentCheckedAtUtc ?? DateTimeOffset.MinValue)
                                                                                            .ThenBy(baseImageVersion => baseImageVersion.Tag, StringComparer.OrdinalIgnoreCase)
                                                                                            .First();
                                     var worstSeveritySummary = relevantBaseImageVersions.Select(baseImageVersion => GetSummaryOrEmpty(activeFindingLookup, baseImageVersion.Id))
                                                                                         .OrderByDescending(summary => summary.TotalCount)
                                                                                         .FirstOrDefault()
                                                                    ?? new VulnerabilitySeveritySummaryViewData();
                                     var childImageVersionIdSet = relevantRelationships.Select(relationship => relationship.ChildImageVersionId)
                                                                                       .ToHashSet();
                                     var runtimeContainers = latestSnapshots.Where(snapshot => childImageVersionIdSet.Contains(snapshot.ImageVersionId))
                                                                            .OrderBy(snapshot => snapshot.DockerInstance.Name, StringComparer.OrdinalIgnoreCase)
                                                                            .ThenBy(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase)
                                                                            .Select(MapLinkedRuntimeContainer)
                                                                            .ToList();

                                     return new SharedBaseImageListItemData
                                            {
                                                BaseImageVersionId = entity.BaseImageVersionId,
                                                BaseImageVersionIds = entity.BaseImageVersionIds,
                                                ImageReference = FormatImageReference(entity.Registry, entity.Repository, entity.Tag, entity.Digest),
                                                ObservedImageCount = entity.ObservedImageCount,
                                                ParentImageReferences = relevantRelationships.Select(relationship => _imageReferenceParser.Format(relationship.ChildImageVersion))
                                                                                             .Distinct(StringComparer.OrdinalIgnoreCase)
                                                                                             .OrderBy(relationship => relationship, StringComparer.OrdinalIgnoreCase)
                                                                                             .ToList(),
                                                RuntimeContainers = runtimeContainers,
                                                VulnerabilityAssessment = CreateVulnerabilityAssessment(representativeBaseImage, worstSeveritySummary),
                                            };
                                 })
                         .OrderByDescending(entity => entity.RuntimeContainers.Count)
                         .ThenByDescending(entity => entity.ObservedImageCount)
                         .ThenBy(entity => entity.ImageReference, StringComparer.OrdinalIgnoreCase)
                         .ToList();
    }

    /// <summary>
    /// Read scan history without re-entering the service gate
    /// </summary>
    /// <param name="take">Maximum number of entries</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scan history list</returns>
    private async Task<IReadOnlyList<ScanHistoryItemData>> GetScanHistoryCoreAsync(int take, CancellationToken cancellationToken)
    {
        var scanRuns = await _dbContext.ScanRuns.Include(entity => entity.ObservedImage)
                                                .Include(entity => entity.DockerInstance)
                                                .AsNoTracking()
                                                .OrderByDescending(entity => entity.StartedAtUtc)
                                                .Take(take)
                                                .ToListAsync(cancellationToken)
                                                .ConfigureAwait(false);

        return scanRuns.Select(MapScanRun)
                       .ToList();
    }

    /// <summary>
    /// Map a linked runtime container projection
    /// </summary>
    /// <param name="entity">Container snapshot entity</param>
    /// <returns>Linked runtime container view data</returns>
    private LinkedRuntimeContainerViewData MapLinkedRuntimeContainer(ContainerSnapshot entity)
    {
        return new LinkedRuntimeContainerViewData
               {
                   DockerInstanceId = entity.DockerInstanceId,
                   ContainerId = entity.ContainerId,
                   DockerInstanceName = entity.DockerInstance.Name,
                   ContainerName = entity.Name,
                   RuntimeStatus = entity.Status.ToString(),
                   ImageReference = _imageReferenceParser.Format(entity.ImageVersion),
                   RecordedAtUtc = entity.RecordedAtUtc,
               };
    }

    /// <summary>
    /// Load recommended image versions for a finding set
    /// </summary>
    /// <param name="updateFindings">Update findings to inspect</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recommended image versions by identifier</returns>
    private async Task<Dictionary<Guid, ImageVersion>> LoadRecommendedImageVersionsAsync(IEnumerable<UpdateFinding> updateFindings,
                                                                                         CancellationToken cancellationToken)
    {
        var recommendedImageVersionIds = updateFindings.Where(entity => entity.RecommendedImageVersionId is not null)
                                                       .Select(entity => entity.RecommendedImageVersionId.GetValueOrDefault())
                                                       .Distinct()
                                                       .ToList();

        return recommendedImageVersionIds.Count == 0
                   ? []
                   : await _dbContext.ImageVersions.Include(entity => entity.RegistryRepository)
                                                   .Where(entity => recommendedImageVersionIds.Contains(entity.Id))
                                                   .AsNoTracking()
                                                   .ToDictionaryAsync(entity => entity.Id, cancellationToken)
                                                   .ConfigureAwait(false);
    }

    /// <summary>
    /// Load latest snapshots for each runtime container
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Latest snapshots</returns>
    private async Task<IReadOnlyList<ContainerSnapshot>> GetLatestContainerSnapshotsAsync(CancellationToken cancellationToken)
    {
        var snapshots = await _dbContext.ContainerSnapshots.Include(entity => entity.DockerInstance)
                                                           .ThenInclude(entity => entity.PortainerEndpoint)
                                                           .Include(entity => entity.ImageVersion)
                                                           .ThenInclude(entity => entity.RegistryRepository)
                                                           .Include(entity => entity.ScanRun)
                                                           .AsNoTracking()
                                                           .OrderByDescending(entity => entity.RecordedAtUtc)
                                                           .ToListAsync(cancellationToken)
                                                           .ConfigureAwait(false);

        var currentSnapshots = new List<ContainerSnapshot>();

        foreach (var instanceGroup in snapshots.GroupBy(entity => entity.DockerInstanceId))
        {
            var latestRuntimeScanSnapshots = instanceGroup.Where(entity => entity.ScanRun?.Type == ScanRunType.RuntimeContainer)
                                                          .GroupBy(entity => entity.ScanRunId)
                                                          .OrderByDescending(group => group.Max(item => item.ScanRun?.StartedAtUtc ?? item.RecordedAtUtc))
                                                          .Select(group => group.OrderByDescending(item => item.RecordedAtUtc)
                                                                                .ToList())
                                                          .FirstOrDefault();

            if (latestRuntimeScanSnapshots is not null)
            {
                currentSnapshots.AddRange(latestRuntimeScanSnapshots);

                continue;
            }

            currentSnapshots.AddRange(instanceGroup.GroupBy(entity => CreateContainerKey(entity.DockerInstanceId, entity.ContainerId))
                                                   .Select(group => group.First()));
        }

        return currentSnapshots;
    }

    /// <summary>
    /// Load account-discovered own images keyed by repository
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed images by repository</returns>
    private async Task<Dictionary<string, ObservedImage>> LoadOwnImagesByRepositoryAsync(CancellationToken cancellationToken)
    {
        var observedImages = await _dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion)
                                                            .ThenInclude(entity => entity.RegistryRepository)
                                                            .Where(entity => entity.Source == RegistrationSource.Discovery)
                                                            .AsNoTracking()
                                                            .ToListAsync(cancellationToken)
                                                            .ConfigureAwait(false);

        return observedImages.GroupBy(entity => CreateRepositoryKey(entity.CurrentImageVersion), StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(group => group.Key,
                                           group => group.OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase).First(),
                                           StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Load base-image relationships keyed by child image version
    /// </summary>
    /// <param name="childImageVersionIds">Child image version identifiers</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Base-image relationships by child image version</returns>
    private async Task<Dictionary<Guid, IReadOnlyList<BaseImageRelationshipData>>> LoadBaseImageRelationshipsByChildVersionAsync(List<Guid> childImageVersionIds,
                                                                                                                                 CancellationToken cancellationToken)
    {
        if (childImageVersionIds.Count == 0)
        {
            return [];
        }

        var relationships = await _dbContext.ImageRelationships.Include(entity => entity.BaseImageVersion)
                                                               .ThenInclude(entity => entity.RegistryRepository)
                                                               .Where(entity => childImageVersionIds.Contains(entity.ChildImageVersionId)
                                                                                && entity.RelationshipType == ImageRelationshipType.BaseImage)
                                                               .OrderBy(entity => entity.ChildImageVersionId)
                                                               .ThenBy(entity => entity.Depth)
                                                               .AsNoTracking()
                                                               .ToListAsync(cancellationToken)
                                                               .ConfigureAwait(false);
        var baseImageVersionIds = relationships.Select(entity => entity.BaseImageVersionId)
                                               .Distinct()
                                               .ToList();
        var activeFindingLookup = await LoadActiveVulnerabilitySeveritySummariesAsync(baseImageVersionIds, cancellationToken).ConfigureAwait(false);

        return relationships.GroupBy(entity => entity.ChildImageVersionId)
                            .ToDictionary(group => group.Key,
                                          group => (IReadOnlyList<BaseImageRelationshipData>)group.Select(entity => new BaseImageRelationshipData
                                                                                                                    {
                                                                                                                        ImageReference = _imageReferenceParser.Format(entity.BaseImageVersion),
                                                                                                                        Depth = entity.Depth,
                                                                                                                        SourceReference = entity.SourceReference,
                                                                                                                        VulnerabilityAssessment = CreateVulnerabilityAssessment(entity.BaseImageVersion,
                                                                                                                                                                                GetSummaryOrEmpty(activeFindingLookup, entity.BaseImageVersionId)),
                                                                                                                    })
                                                                                                  .ToList());
    }

    /// <summary>
    /// Load active vulnerability severity summaries for a set of image versions
    /// </summary>
    /// <param name="imageVersionIds">Image version identifiers</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Severity summary by image version identifier</returns>
    private async Task<Dictionary<Guid, VulnerabilitySeveritySummaryViewData>> LoadActiveVulnerabilitySeveritySummariesAsync(List<Guid> imageVersionIds, CancellationToken cancellationToken)
    {
        if (imageVersionIds.Count == 0)
        {
            return [];
        }

        var activeFindingCounts = await _dbContext.VulnerabilityFindings.Where(entity => entity.IsActive
                                                                                         && imageVersionIds.Contains(entity.ImageVersionId))
                                                                        .GroupBy(entity => new
                                                                                           {
                                                                                               entity.ImageVersionId,
                                                                                               entity.Severity,
                                                                                           })
                                                                        .Select(group => new
                                                                                         {
                                                                                             group.Key.ImageVersionId,
                                                                                             group.Key.Severity,
                                                                                             ActiveFindingCount = group.Count(),
                                                                                         })
                                                                        .ToListAsync(cancellationToken)
                                                                        .ConfigureAwait(false);

        return activeFindingCounts.GroupBy(entity => entity.ImageVersionId)
                                  .ToDictionary(group => group.Key,
                                                group => CreateSeveritySummary(group.Select(entity => new KeyValuePair<VulnerabilitySeverity, int>(entity.Severity,
                                                                                                                                                   entity.ActiveFindingCount))));
    }

    /// <summary>
    /// Load active update finding counts for a set of observed images
    /// </summary>
    /// <param name="observedImageIds">Observed image identifiers</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Active update finding count by observed image identifier</returns>
    private async Task<Dictionary<Guid, int>> LoadActiveUpdateFindingCountsByObservedImageAsync(List<Guid> observedImageIds, CancellationToken cancellationToken)
    {
        if (observedImageIds.Count == 0)
        {
            return [];
        }

        var activeFindingCounts = await _dbContext.UpdateFindings.Where(entity => entity.IsActive
                                                                                  && entity.ObservedImageId != null
                                                                                  && observedImageIds.Contains(entity.ObservedImageId.Value))
                                                                 .GroupBy(entity => entity.ObservedImageId!.Value)
                                                                 .Select(group => new
                                                                                  {
                                                                                      ObservedImageId = group.Key,
                                                                                      ActiveFindingCount = group.Count(),
                                                                                  })
                                                                 .ToListAsync(cancellationToken)
                                                                 .ConfigureAwait(false);

        return activeFindingCounts.ToDictionary(entity => entity.ObservedImageId, entity => entity.ActiveFindingCount);
    }

    /// <summary>
    /// Load recent runtime container resource histories for the runtime list
    /// </summary>
    /// <param name="snapshots">Latest container snapshots</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recent resource histories by container key</returns>
    private async Task<Dictionary<string, IReadOnlyList<ResourceUsagePointViewData>>> GetRuntimeContainerResourceHistoriesAsync(IReadOnlyList<ContainerSnapshot> snapshots,
                                                                                                                                CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        if (snapshots.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<ResourceUsagePointViewData>>(StringComparer.OrdinalIgnoreCase);
        }

        var cutoff = DateTimeOffset.UtcNow.Subtract(_resourceHistoryWindow);
        var instanceIds = snapshots.Select(entity => entity.DockerInstanceId)
                                   .Distinct()
                                   .ToList();
        var containerIds = snapshots.Select(entity => entity.ContainerId)
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToList();
        var expectedKeys = snapshots.Select(entity => CreateContainerKey(entity.DockerInstanceId, entity.ContainerId))
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var samples = await _dbContext.RuntimeContainerResourceSamples.Where(entity => instanceIds.Contains(entity.DockerInstanceId)
                                                                                       && containerIds.Contains(entity.ContainerId)
                                                                                       && entity.RecordedAtUtc >= cutoff)
                                                                      .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                      .AsNoTracking()
                                                                      .ToListAsync(cancellationToken)
                                                                      .ConfigureAwait(false);

        return samples.Where(entity => expectedKeys.Contains(CreateContainerKey(entity.DockerInstanceId, entity.ContainerId)))
                      .GroupBy(entity => CreateContainerKey(entity.DockerInstanceId, entity.ContainerId))
                      .ToDictionary(group => group.Key,
                                    group => (IReadOnlyList<ResourceUsagePointViewData>)group.Take(RuntimeListResourceHistoryCount)
                                                                                             .Select(MapResourceUsage)
                                                                                             .ToList(),
                                    StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Load latest Docker instance resource samples
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Latest instance samples by instance identifier</returns>
    private async Task<Dictionary<Guid, DockerInstanceResourceSample>> GetLatestDockerInstanceResourceSamplesAsync(CancellationToken cancellationToken)
    {
        var samples = await _dbContext.DockerInstanceResourceSamples.AsNoTracking()
                                                                    .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                    .ToListAsync(cancellationToken)
                                                                    .ConfigureAwait(false);

        return samples.GroupBy(entity => entity.DockerInstanceId)
                      .ToDictionary(group => group.Key, group => group.First());
    }

    /// <summary>
    /// Read observed image scan history
    /// </summary>
    /// <param name="observedImageId">ID of the observed image</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scan history entries</returns>
    private async Task<IReadOnlyList<ScanHistoryItemData>> GetObservedImageScanHistoryAsync(Guid observedImageId, CancellationToken cancellationToken)
    {
        var scanRuns = await _dbContext.ScanRuns.Where(entity => entity.ObservedImageId == observedImageId)
                                                .AsNoTracking()
                                                .OrderByDescending(entity => entity.StartedAtUtc)
                                                .Take(20)
                                                .ToListAsync(cancellationToken)
                                                .ConfigureAwait(false);

        return scanRuns.Select(MapScanRun)
                       .ToList();
    }

    /// <summary>
    /// Read runtime-container-specific scan history
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="containerId">ID of the container</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scan history entries</returns>
    private async Task<IReadOnlyList<ScanHistoryItemData>> GetRuntimeContainerScanHistoryAsync(Guid dockerInstanceId,
                                                                                               string containerId,
                                                                                               CancellationToken cancellationToken)
    {
        var snapshots = await _dbContext.ContainerSnapshots.Include(entity => entity.ScanRun)
                                                           .Where(entity => entity.DockerInstanceId == dockerInstanceId
                                                                            && entity.ContainerId == containerId)
                                                           .OrderByDescending(entity => entity.RecordedAtUtc)
                                                           .Take(20)
                                                           .AsNoTracking()
                                                           .ToListAsync(cancellationToken)
                                                           .ConfigureAwait(false);

        return snapshots.Select(entity => new ScanHistoryItemData
                                          {
                                              Id = entity.ScanRunId ?? entity.Id,
                                              Type = entity.ScanRun?.Type.ToString() ?? ScanRunType.RuntimeContainer.ToString(),
                                              Status = entity.ScanRun?.Status.ToString() ?? ScanRunStatus.NotSet.ToString(),
                                              TriggerSource = entity.ScanRun?.TriggerSource.ToString() ?? ScanTriggerSource.NotSet.ToString(),
                                              SubjectName = entity.Name,
                                              StartedAtUtc = entity.ScanRun?.StartedAtUtc ?? entity.RecordedAtUtc,
                                              CompletedAtUtc = entity.ScanRun?.CompletedAtUtc ?? entity.RecordedAtUtc,
                                              ErrorMessage = entity.ScanRun?.ErrorMessage ?? entity.UpdateAssessmentMessage,
                                          })
                        .ToList();
    }

    /// <summary>
    /// Read runtime-container resource history
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="containerId">ID of the container</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resource history entries</returns>
    private async Task<IReadOnlyList<ResourceUsagePointViewData>> GetRuntimeContainerResourceHistoryAsync(Guid dockerInstanceId,
                                                                                                          string containerId,
                                                                                                          CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(_resourceHistoryWindow);
        var samples = await _dbContext.RuntimeContainerResourceSamples.Where(entity => entity.DockerInstanceId == dockerInstanceId
                                                                                       && entity.ContainerId == containerId
                                                                                       && entity.RecordedAtUtc >= cutoff)
                                                                      .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                      .Take(48)
                                                                      .AsNoTracking()
                                                                      .ToListAsync(cancellationToken)
                                                                      .ConfigureAwait(false);

        return samples.Select(MapResourceUsage)
                      .ToList();
    }

    /// <summary>
    /// Read Docker-instance resource history
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resource history entries</returns>
    private async Task<IReadOnlyList<ResourceUsagePointViewData>> GetDockerInstanceResourceHistoryAsync(Guid dockerInstanceId, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(_resourceHistoryWindow);
        var samples = await _dbContext.DockerInstanceResourceSamples.Where(entity => entity.DockerInstanceId == dockerInstanceId
                                                                                     && entity.RecordedAtUtc >= cutoff)
                                                                    .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                    .Take(48)
                                                                    .AsNoTracking()
                                                                    .ToListAsync(cancellationToken)
                                                                    .ConfigureAwait(false);

        return samples.Select(MapResourceUsage)
                      .ToList();
    }

    /// <summary>
    /// Map a scan run to view data
    /// </summary>
    /// <param name="scanRun">Scan run entity</param>
    /// <returns>Scan history item</returns>
    private ScanHistoryItemData MapScanRun(ScanRun scanRun)
    {
        return new ScanHistoryItemData
               {
                   Id = scanRun.Id,
                   Type = scanRun.Type.ToString(),
                   Status = scanRun.Status.ToString(),
                   TriggerSource = scanRun.TriggerSource.ToString(),
                   SubjectName = scanRun.ObservedImage?.Name ?? scanRun.DockerInstance?.Name,
                   StartedAtUtc = scanRun.StartedAtUtc,
                   CompletedAtUtc = scanRun.CompletedAtUtc,
                   ErrorMessage = scanRun.ErrorMessage,
               };
    }

    /// <summary>
    /// Map an update finding to view data
    /// </summary>
    /// <param name="entity">Update finding entity</param>
    /// <param name="recommendedImageVersions">Recommended image versions by identifier</param>
    /// <param name="manualSelection">Manual selection of container tag</param>
    /// <returns>Update finding view data</returns>
    private UpdateFindingViewData MapUpdateFinding(UpdateFinding entity,
                                                   Dictionary<Guid, ImageVersion> recommendedImageVersions,
                                                   RuntimeContainerTagSelection? manualSelection)
    {
        ImageVersion? recommendedImageVersion = null;

        if (entity.RecommendedImageVersionId is not null)
        {
            recommendedImageVersions.TryGetValue(entity.RecommendedImageVersionId.GetValueOrDefault(), out recommendedImageVersion);
        }

        var tagCandidates = FilterVisibleTagCandidates(entity.TagCandidates.OrderBy(candidate => candidate.Rank)
                                                                           .Select(candidate => new TagCandidateViewData
                                                                                                {
                                                                                                    Tag = candidate.Tag,
                                                                                                    Digest = candidate.Digest,
                                                                                                    PublishedAtUtc = candidate.PublishedAtUtc,
                                                                                                    Reason = candidate.Reason,
                                                                                                    IsRecommended = candidate.IsRecommended,
                                                                                                    IsSelected = manualSelection is not null
                                                                                                                 && string.Equals(candidate.Tag, manualSelection.Tag, StringComparison.OrdinalIgnoreCase)
                                                                                                                 && string.Equals(candidate.Digest ?? string.Empty,
                                                                                                                                  manualSelection.Digest ?? string.Empty,
                                                                                                                                  StringComparison.OrdinalIgnoreCase),
                                                                                                }));

        PopulateResolvedVersionTags(tagCandidates);

        return new UpdateFindingViewData
               {
                   Type = FormatUpdateFindingType(entity.Type),
                   Summary = entity.Summary,
                   Details = entity.Details,
                   RecommendedImage = recommendedImageVersion is null ? null : _imageReferenceParser.Format(recommendedImageVersion),
                   IsActive = entity.IsActive,
                   DetectedAtUtc = entity.DetectedAtUtc,
                   TagCandidates = tagCandidates,
               };
    }

    /// <summary>
    /// Read the latest observed-image scan status
    /// </summary>
    /// <param name="observedImageId">ID of the observed image</param>
    /// <returns>Scan status</returns>
    private string GetLatestObservedScanStatus(Guid observedImageId)
    {
        return _dbContext.ScanRuns.Where(entity => entity.ObservedImageId == observedImageId && entity.Type == ScanRunType.ObservedImage)
                                  .OrderByDescending(entity => entity.StartedAtUtc)
                                  .Select(entity => entity.Status.ToString())
                                  .FirstOrDefault() ?? "NotScanned";
    }

    /// <summary>
    /// Read the latest observed-image scan message
    /// </summary>
    /// <param name="observedImageId">ID of the observed image</param>
    /// <returns>Scan message</returns>
    private string? GetLatestObservedScanMessage(Guid observedImageId)
    {
        return _dbContext.ScanRuns.Where(entity => entity.ObservedImageId == observedImageId && entity.Type == ScanRunType.ObservedImage)
                                  .OrderByDescending(entity => entity.StartedAtUtc)
                                  .Select(entity => entity.ErrorMessage)
                                  .FirstOrDefault();
    }

    /// <summary>
    /// Read the latest runtime scan status for a Docker instance
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <returns>Scan status</returns>
    private string GetLatestRuntimeScanStatus(Guid dockerInstanceId)
    {
        return _dbContext.ScanRuns.Where(entity => entity.DockerInstanceId == dockerInstanceId && entity.Type == ScanRunType.RuntimeContainer)
                                  .OrderByDescending(entity => entity.StartedAtUtc)
                                  .Select(entity => entity.Status.ToString())
                                  .FirstOrDefault() ?? "NotScanned";
    }

    /// <summary>
    /// Core observed-image query, optionally filtered to a single registration source
    /// </summary>
    /// <param name="source">Registration source filter; null loads all sources</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed image list items</returns>
    private async Task<IReadOnlyList<ObservedImageListItemData>> GetObservedImagesCoreAsync(RegistrationSource? source, CancellationToken cancellationToken)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var observedImagesQuery = _dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion)
                                                                                                   .ThenInclude(entity => entity.RegistryRepository)
                                                                                                   .AsNoTracking();

                                                if (source.HasValue)
                                                {
                                                    observedImagesQuery = observedImagesQuery.Where(entity => entity.Source == source.Value);
                                                }

                                                var observedImages = await observedImagesQuery.ToListAsync(cancellationToken)
                                                                                              .ConfigureAwait(false);

                                                var latestSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
                                                var latestSnapshotIds = latestSnapshots.Select(entity => entity.Id)
                                                                                       .ToList();
                                                var repositoryKeyBySnapshotId = latestSnapshots.ToDictionary(entity => entity.Id,
                                                                                                             CreateRepositoryKey);
                                                var observedImageIds = observedImages.Select(entity => entity.Id)
                                                                                     .ToList();
                                                var derivedBaseRuntimeFindings = await _dbContext.UpdateFindings.Where(entity => entity.IsActive
                                                                                                                                 && entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate
                                                                                                                                 && ((entity.ObservedImageId != null
                                                                                                                                      && observedImageIds.Contains(entity.ObservedImageId.Value))
                                                                                                                                     || (entity.ContainerSnapshotId != null
                                                                                                                                         && latestSnapshotIds.Contains(entity.ContainerSnapshotId.Value))))
                                                                                                                .OrderByDescending(entity => entity.DetectedAtUtc)
                                                                                                                .AsNoTracking()
                                                                                                                .ToListAsync(cancellationToken)
                                                                                                                .ConfigureAwait(false);

                                                var runtimeLinkLookup = latestSnapshots.GroupBy(CreateRepositoryKey)
                                                                                       .ToDictionary(group => group.Key,
                                                                                                     group => group.Count(),
                                                                                                     StringComparer.OrdinalIgnoreCase);
                                                var runtimeAlertLookup = derivedBaseRuntimeFindings.Where(entity => entity.ContainerSnapshotId is not null
                                                                                                                    && repositoryKeyBySnapshotId.ContainsKey(entity.ContainerSnapshotId.Value))
                                                                                                   .GroupBy(entity => repositoryKeyBySnapshotId[entity.ContainerSnapshotId!.Value],
                                                                                                            StringComparer.OrdinalIgnoreCase)
                                                                                                   .ToDictionary(group => group.Key,
                                                                                                                 group => group.OrderByDescending(entity => entity.DetectedAtUtc)
                                                                                                                               .First(),
                                                                                                                 StringComparer.OrdinalIgnoreCase);
                                                var observedAlertLookup = derivedBaseRuntimeFindings.Where(entity => entity.ObservedImageId != null)
                                                                                                    .GroupBy(entity => entity.ObservedImageId!.Value)
                                                                                                    .ToDictionary(group => group.Key,
                                                                                                                  group => group.OrderByDescending(entity => entity.DetectedAtUtc)
                                                                                                                                .First());
                                                var baseImageRelationshipsByChildVersion = await LoadBaseImageRelationshipsByChildVersionAsync(observedImages.Select(entity => entity.CurrentImageVersionId)
                                                                                                                                                             .Distinct()
                                                                                                                                                             .ToList(),
                                                                                                                                               cancellationToken).ConfigureAwait(false);
                                                var observedImageVersionIds = observedImages.Select(entity => entity.CurrentImageVersionId)
                                                                                            .Distinct()
                                                                                            .ToList();
                                                var activeVulnerabilityFindingLookup = await LoadActiveVulnerabilitySeveritySummariesAsync(observedImageVersionIds, cancellationToken).ConfigureAwait(false);
                                                var activeUpdateFindingLookup = await LoadActiveUpdateFindingCountsByObservedImageAsync(observedImageIds, cancellationToken).ConfigureAwait(false);

                                                return observedImages.Select(entity =>
                                                                             {
                                                                                 var repositoryKey = CreateRepositoryKey(entity.CurrentImageVersion);

                                                                                 observedAlertLookup.TryGetValue(entity.Id, out var observedAlert);
                                                                                 runtimeAlertLookup.TryGetValue(repositoryKey, out var runtimeAlert);

                                                                                 var baseRuntimeAlert = observedAlert ?? runtimeAlert;

                                                                                 baseImageRelationshipsByChildVersion.TryGetValue(entity.CurrentImageVersionId, out var baseImageRelationships);

                                                                                 var baseImageVulnerabilitySummary = SummarizeBaseImageVulnerabilities(baseImageRelationships);

                                                                                 return new ObservedImageListItemData
                                                                                        {
                                                                                            Id = entity.Id,
                                                                                            Name = entity.Name,
                                                                                            Description = entity.Description,
                                                                                            ImageReference = _imageReferenceParser.Format(entity.CurrentImageVersion),
                                                                                            LatestScanStatus = GetLatestObservedScanStatus(entity.Id),
                                                                                            LatestScanMessage = GetLatestObservedScanMessage(entity.Id),
                                                                                            ActiveUpdateFindingCount = GetCountOrZero(activeUpdateFindingLookup, entity.Id),
                                                                                            ActiveVulnerabilityFindingCount = GetSummaryOrEmpty(activeVulnerabilityFindingLookup, entity.CurrentImageVersionId).TotalCount,
                                                                                            VulnerabilitySeveritySummary = GetSummaryOrEmpty(activeVulnerabilityFindingLookup, entity.CurrentImageVersionId),
                                                                                            VulnerabilityStatus = FormatVulnerabilityAssessmentStatus(entity.CurrentImageVersion.VulnerabilityAssessmentStatus),
                                                                                            VulnerabilityMessage = entity.CurrentImageVersion.VulnerabilityAssessmentMessage,
                                                                                            ActiveBaseImageVulnerabilityFindingCount = baseImageVulnerabilitySummary.ActiveFindingCount,
                                                                                            BaseImageVulnerabilitySummary = baseImageVulnerabilitySummary.Summary,
                                                                                            IsOwnImage = entity.Source == RegistrationSource.Discovery,
                                                                                            LinkedRuntimeContainerCount = entity.Source == RegistrationSource.Discovery
                                                                                                                          && runtimeLinkLookup.TryGetValue(repositoryKey, out var linkedRuntimeContainerCount)
                                                                                                                              ? linkedRuntimeContainerCount
                                                                                                                              : 0,
                                                                                            BaseRuntimeAlertSummary = baseRuntimeAlert?.Summary,
                                                                                            BaseRuntimeAlertDetails = baseRuntimeAlert?.Details,
                                                                                        };
                                                                             })
                                                                     .OrderByDescending(entity => entity.IsOwnImage)
                                                                     .ThenByDescending(entity => entity.LinkedRuntimeContainerCount)
                                                                     .ThenBy(entity => entity.Name)
                                                                     .ToList();
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    #endregion // Methods

    #region IApplicationViewService

    /// <inheritdoc/>
    public async Task<DashboardViewData> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var recentScans = await GetScanHistoryCoreAsync(10, cancellationToken).ConfigureAwait(false);
                                                var baseImages = await _sharedBaseImageQueryService.GetBaseImagesAsync(cancellationToken).ConfigureAwait(false);
                                                var observedImageCount = await _dbContext.ObservedImages.CountAsync(entity => entity.Source == RegistrationSource.Manual, cancellationToken).ConfigureAwait(false);
                                                var myImageCount = await _dbContext.ObservedImages.CountAsync(entity => entity.Source == RegistrationSource.Discovery, cancellationToken).ConfigureAwait(false);
                                                var dockerInstanceCount = await _dbContext.DockerInstances.CountAsync(cancellationToken).ConfigureAwait(false);
                                                var runtimeContainers = await GetRuntimeContainersCoreAsync(cancellationToken).ConfigureAwait(false);
                                                var activeUpdateFindingCount = await _dbContext.UpdateFindings.CountAsync(entity => entity.IsActive, cancellationToken).ConfigureAwait(false);
                                                var ownImageBaseRuntimeWarningCount = await _dbContext.UpdateFindings.Join(_dbContext.ObservedImages.Where(entity => entity.Source == RegistrationSource.Discovery),
                                                                                                                           finding => finding.ObservedImageId,
                                                                                                                           observedImage => (Guid?)observedImage.Id,
                                                                                                                           (finding, observedImage) => new
                                                                                                                                                       {
                                                                                                                                                           finding.IsActive,
                                                                                                                                                           finding.Type,
                                                                                                                                                       })
                                                                                                                     .CountAsync(entity => entity.IsActive
                                                                                                                                           && entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate,
                                                                                                                                 cancellationToken)
                                                                                                                     .ConfigureAwait(false);
                                                var activeVulnerabilitySeverityCounts = await _dbContext.VulnerabilityFindings.Where(entity => entity.IsActive)
                                                                                                                              .GroupBy(entity => entity.Severity)
                                                                                                                              .Select(group => new
                                                                                                                                               {
                                                                                                                                                   Severity = group.Key,
                                                                                                                                                   ActiveFindingCount = group.Count(),
                                                                                                                                               })
                                                                                                                              .ToListAsync(cancellationToken)
                                                                                                                              .ConfigureAwait(false);
                                                var vulnerabilitySeveritySummary = CreateSeveritySummary(activeVulnerabilitySeverityCounts.Select(entity => new KeyValuePair<VulnerabilitySeverity, int>(entity.Severity, entity.ActiveFindingCount)));

                                                return new DashboardViewData
                                                       {
                                                           ObservedImageCount = observedImageCount,
                                                           MyImageCount = myImageCount,
                                                           DockerInstanceCount = dockerInstanceCount,
                                                           RuntimeContainerCount = runtimeContainers.Count,
                                                           BaseImageCount = baseImages.Count,
                                                           ActiveUpdateFindingCount = activeUpdateFindingCount,
                                                           OwnImageBaseRuntimeWarningCount = ownImageBaseRuntimeWarningCount,
                                                           ActiveVulnerabilityFindingCount = vulnerabilitySeveritySummary.TotalCount,
                                                           VulnerabilitySeveritySummary = vulnerabilitySeveritySummary,
                                                           RecentScans = recentScans,
                                                       };
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<DashboardSummaryViewData> GetDashboardSummaryAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var recentScans = await GetScanHistoryCoreAsync(1, cancellationToken).ConfigureAwait(false);
                                                var observedImageCount = await _dbContext.ObservedImages.CountAsync(entity => entity.Source == RegistrationSource.Manual, cancellationToken).ConfigureAwait(false);
                                                var myImageCount = await _dbContext.ObservedImages.CountAsync(entity => entity.Source == RegistrationSource.Discovery, cancellationToken).ConfigureAwait(false);
                                                var latestSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);

                                                return new DashboardSummaryViewData
                                                       {
                                                           ObservedImageCount = observedImageCount,
                                                           MyImageCount = myImageCount,
                                                           RuntimeContainerCount = latestSnapshots.Count,
                                                           LatestScan = recentScans.Count > 0 ? recentScans[0] : null,
                                                       };
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ObservedImageListItemData>> GetObservedImagesAsync(CancellationToken cancellationToken = default)
    {
        return GetObservedImagesCoreAsync(source: null, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ObservedImageListItemData>> GetManualObservedImagesAsync(CancellationToken cancellationToken = default)
    {
        return GetObservedImagesCoreAsync(RegistrationSource.Manual, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ObservedImageListItemData>> GetDiscoveryObservedImagesAsync(CancellationToken cancellationToken = default)
    {
        return GetObservedImagesCoreAsync(RegistrationSource.Discovery, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ObservedImageDetailViewData?> GetObservedImageDetailAsync(Guid observedImageId, CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var observedImage = await _dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion)
                                                                                                   .ThenInclude(entity => entity.RegistryRepository)
                                                                                                   .AsNoTracking()
                                                                                                   .SingleOrDefaultAsync(entity => entity.Id == observedImageId, cancellationToken)
                                                                                                   .ConfigureAwait(false);

                                                if (observedImage is null)
                                                {
                                                    return null;
                                                }

                                                var baseImageRelationshipsByChildVersion = await LoadBaseImageRelationshipsByChildVersionAsync([observedImage.CurrentImageVersionId], cancellationToken).ConfigureAwait(false);

                                                var updateFindings = await _dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                                                                    .Where(entity => entity.ObservedImageId == observedImage.Id && entity.IsActive)
                                                                                                    .OrderByDescending(entity => entity.DetectedAtUtc)
                                                                                                    .AsNoTracking()
                                                                                                    .ToListAsync(cancellationToken)
                                                                                                    .ConfigureAwait(false);
                                                var vulnerabilityFindings = SortFindingsForDisplay(await _dbContext.VulnerabilityFindings.Where(entity => entity.ImageVersionId == observedImage.CurrentImageVersionId)
                                                                                                                                         .AsNoTracking()
                                                                                                                                         .ToListAsync(cancellationToken)
                                                                                                                                         .ConfigureAwait(false));
                                                var latestSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
                                                var linkedSnapshots = latestSnapshots.Where(entity => string.Equals(CreateRepositoryKey(entity),
                                                                                                                    CreateRepositoryKey(observedImage.CurrentImageVersion),
                                                                                                                    StringComparison.OrdinalIgnoreCase))
                                                                                     .OrderBy(entity => entity.DockerInstance.Name)
                                                                                     .ThenBy(entity => entity.Name)
                                                                                     .ToList();
                                                var linkedSnapshotIds = linkedSnapshots.Select(entity => entity.Id)
                                                                                       .ToList();
                                                var runtimeDerivedFindings = linkedSnapshotIds.Count == 0
                                                                                 ? []
                                                                                 : await _dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                                                                                  .Where(entity => entity.IsActive
                                                                                                                                   && entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate
                                                                                                                                   && entity.ContainerSnapshotId != null
                                                                                                                                   && linkedSnapshotIds.Contains(entity.ContainerSnapshotId.Value))
                                                                                                                  .OrderByDescending(entity => entity.DetectedAtUtc)
                                                                                                                  .AsNoTracking()
                                                                                                                  .ToListAsync(cancellationToken)
                                                                                                                  .ConfigureAwait(false);
                                                var combinedUpdateFindings = updateFindings.Concat(runtimeDerivedFindings)
                                                                                           .OrderByDescending(entity => entity.DetectedAtUtc)
                                                                                           .ToList();
                                                var baseRuntimeAlert = updateFindings.Where(entity => entity.IsActive
                                                                                                      && entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate)
                                                                                     .OrderByDescending(entity => entity.DetectedAtUtc)
                                                                                     .FirstOrDefault()
                                                                           ?? runtimeDerivedFindings.FirstOrDefault();
                                                var recommendedImageVersions = await LoadRecommendedImageVersionsAsync(combinedUpdateFindings, cancellationToken).ConfigureAwait(false);

                                                baseImageRelationshipsByChildVersion.TryGetValue(observedImage.CurrentImageVersionId, out var baseImages);

                                                var baseImageVulnerabilitySummary = SummarizeBaseImageVulnerabilities(baseImages);

                                                return new ObservedImageDetailViewData
                                                       {
                                                           Id = observedImage.Id,
                                                           Name = observedImage.Name,
                                                           Description = observedImage.Description,
                                                           ImageReference = _imageReferenceParser.Format(observedImage.CurrentImageVersion),
                                                           LatestScanStatus = GetLatestObservedScanStatus(observedImage.Id),
                                                           LatestScanMessage = GetLatestObservedScanMessage(observedImage.Id),
                                                           IsOwnImage = observedImage.Source == RegistrationSource.Discovery,
                                                           BaseImages = baseImages ?? [],
                                                           ActiveBaseImageVulnerabilityFindingCount = baseImageVulnerabilitySummary.ActiveFindingCount,
                                                           BaseImageVulnerabilitySummary = baseImageVulnerabilitySummary.Summary,
                                                           UpdateFindings = combinedUpdateFindings.Select(entity => MapUpdateFinding(entity, recommendedImageVersions, manualSelection: null))
                                                                                                  .ToList(),
                                                           BaseRuntimeAlertSummary = baseRuntimeAlert?.Summary,
                                                           BaseRuntimeAlertDetails = baseRuntimeAlert?.Details,
                                                           VulnerabilityAssessment = CreateVulnerabilityAssessment(observedImage.CurrentImageVersion,
                                                                                                                   CreateSeveritySummaryFromFindings(vulnerabilityFindings)),
                                                           VulnerabilityFindings = vulnerabilityFindings.Select(MapVulnerabilityFinding)
                                                                                                        .ToList(),
                                                           LinkedRuntimeContainers = observedImage.Source == RegistrationSource.Discovery
                                                                                         ? linkedSnapshots.Select(MapLinkedRuntimeContainer)
                                                                                                          .ToList()
                                                                                         : [],
                                                           ScanHistory = await GetObservedImageScanHistoryAsync(observedImage.Id, cancellationToken).ConfigureAwait(false),
                                                       };
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RuntimeContainerListItemData>> GetRuntimeContainersAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(() => GetRuntimeContainersCoreAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<RuntimeContainerDetailViewData?> GetRuntimeContainerDetailAsync(Guid dockerInstanceId,
                                                                                      string containerId,
                                                                                      CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

                                                var latestSnapshot = await _dbContext.ContainerSnapshots.Include(entity => entity.DockerInstance)
                                                                                                        .ThenInclude(entity => entity.PortainerEndpoint)
                                                                                                        .Include(entity => entity.ImageVersion)
                                                                                                        .ThenInclude(entity => entity.RegistryRepository)
                                                                                                        .AsNoTracking()
                                                                                                        .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                                                        .FirstOrDefaultAsync(entity => entity.DockerInstanceId == dockerInstanceId
                                                                                                                                       && entity.ContainerId == containerId,
                                                                                                                             cancellationToken)
                                                                                                        .ConfigureAwait(false);

                                                if (latestSnapshot is null)
                                                {
                                                    return null;
                                                }

                                                var updateFindings = await _dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                                                                    .Where(entity => entity.ContainerSnapshotId == latestSnapshot.Id)
                                                                                                    .OrderByDescending(entity => entity.DetectedAtUtc)
                                                                                                    .AsNoTracking()
                                                                                                    .ToListAsync(cancellationToken)
                                                                                                    .ConfigureAwait(false);

                                                var recommendedImageVersions = await LoadRecommendedImageVersionsAsync(updateFindings, cancellationToken).ConfigureAwait(false);

                                                var manualSelection = await _dbContext.RuntimeContainerTagSelections.AsNoTracking()
                                                                                                                    .SingleOrDefaultAsync(entity => entity.DockerInstanceId == dockerInstanceId
                                                                                                                                                    && entity.ContainerId == containerId
                                                                                                                                                    && entity.RegistryRepositoryId == latestSnapshot.ImageVersion.RegistryRepositoryId,
                                                                                                                                          cancellationToken)
                                                                                                                    .ConfigureAwait(false);

                                                var vulnerabilityFindings = SortFindingsForDisplay(await _dbContext.VulnerabilityFindings.Where(entity => entity.ImageVersionId == latestSnapshot.ImageVersionId)
                                                                                                                                         .AsNoTracking()
                                                                                                                                         .ToListAsync(cancellationToken)
                                                                                                                                         .ConfigureAwait(false));
                                                var baseImageRelationshipsByChildVersion = await LoadBaseImageRelationshipsByChildVersionAsync([latestSnapshot.ImageVersionId], cancellationToken).ConfigureAwait(false);

                                                var mappedUpdateFindings = updateFindings.Select(entity => MapUpdateFinding(entity, recommendedImageVersions, manualSelection))
                                                                                         .ToList();

                                                var availableTagCandidates = FilterRuntimeAvailableTagCandidates(FilterVisibleTagCandidates(mappedUpdateFindings.SelectMany(entity => entity.TagCandidates)
                                                                                                                                                                .OrderByDescending(entity => entity.IsRecommended)
                                                                                                                                                                .ThenBy(entity => entity.Tag, StringComparer.OrdinalIgnoreCase)),
                                                                                                                 latestSnapshot.ImageVersion.Tag,
                                                                                                                 latestSnapshot.ResolvedVersionTag,
                                                                                                                 latestSnapshot.ImageVersion.PublishedAtUtc);

                                                var resolvedVersionTag = string.IsNullOrWhiteSpace(latestSnapshot.ResolvedVersionTag) == false
                                                                             ? latestSnapshot.ResolvedVersionTag
                                                                             : ResolveResolvedVersionTag(latestSnapshot.ImageVersion.Tag,
                                                                                                         latestSnapshot.ImageVersion.Digest,
                                                                                                         availableTagCandidates);
                                                var availableUpdateVersionTag = string.IsNullOrWhiteSpace(latestSnapshot.AvailableUpdateVersionTag) == false
                                                                                    ? latestSnapshot.AvailableUpdateVersionTag
                                                                                    : ResolveAvailableUpdateVersionTag(availableTagCandidates,
                                                                                                                       resolvedVersionTag ?? latestSnapshot.ImageVersion.Tag);

                                                var registryRepository = latestSnapshot.ImageVersion.RegistryRepository;

                                                var ownImagesByRepository = await LoadOwnImagesByRepositoryAsync(cancellationToken).ConfigureAwait(false);
                                                var resourceUsageHistory = await GetRuntimeContainerResourceHistoryAsync(dockerInstanceId, containerId, cancellationToken).ConfigureAwait(false);

                                                ArgumentNullException.ThrowIfNull(registryRepository);

                                                ownImagesByRepository.TryGetValue(CreateRepositoryKey(latestSnapshot), out var linkedObservedImage);

                                                var baseRuntimeAlert = updateFindings.FirstOrDefault(entity => entity.IsActive
                                                                                                               && entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate);

                                                if (baseRuntimeAlert is null && linkedObservedImage is not null)
                                                {
                                                    baseRuntimeAlert = await _dbContext.UpdateFindings.Where(entity => entity.IsActive
                                                                                                                       && entity.ObservedImageId == linkedObservedImage.Id
                                                                                                                       && entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate)
                                                                                                      .OrderByDescending(entity => entity.DetectedAtUtc)
                                                                                                      .AsNoTracking()
                                                                                                      .FirstOrDefaultAsync(cancellationToken)
                                                                                                      .ConfigureAwait(false);
                                                }

                                                baseImageRelationshipsByChildVersion.TryGetValue(latestSnapshot.ImageVersionId, out var baseImages);

                                                var baseImageVulnerabilitySummary = SummarizeBaseImageVulnerabilities(baseImages);

                                                return new RuntimeContainerDetailViewData
                                                       {
                                                           DockerInstanceId = dockerInstanceId,
                                                           ContainerId = containerId,
                                                           ContainerName = latestSnapshot.Name,
                                                           DockerInstanceName = latestSnapshot.DockerInstance.Name,
                                                           ImageReference = _imageReferenceParser.Format(latestSnapshot.ImageVersion),
                                                           CurrentTag = latestSnapshot.ImageVersion.Tag,
                                                           ResolvedVersionTag = resolvedVersionTag,
                                                           RuntimeStatus = latestSnapshot.Status.ToString(),
                                                           ComposeProject = latestSnapshot.ComposeProject,
                                                           StackName = latestSnapshot.StackName,
                                                           ServiceName = latestSnapshot.ServiceName,
                                                           RecordedAtUtc = latestSnapshot.RecordedAtUtc,
                                                           LinkedObservedImageId = linkedObservedImage?.Id,
                                                           LinkedObservedImageName = linkedObservedImage?.Name,
                                                           UpdateStatus = FormatUpdateAssessmentStatus(latestSnapshot.UpdateAssessmentStatus),
                                                           UpdateMessage = latestSnapshot.UpdateAssessmentMessage,
                                                           AvailableUpdateVersionTag = availableUpdateVersionTag,
                                                           ManualSelectionImage = manualSelection is null
                                                                                      ? null
                                                                                      : FormatImageReference(registryRepository.Registry,
                                                                                                             registryRepository.Repository,
                                                                                                             manualSelection.Tag,
                                                                                                             manualSelection.Digest),
                                                           ManualSelectionAtUtc = manualSelection?.SelectedAtUtc,
                                                           AvailableTagCandidates = availableTagCandidates,
                                                           BaseImages = baseImages ?? [],
                                                           ActiveBaseImageVulnerabilityFindingCount = baseImageVulnerabilitySummary.ActiveFindingCount,
                                                           BaseImageVulnerabilitySummary = baseImageVulnerabilitySummary.Summary,
                                                           UpdateFindings = mappedUpdateFindings,
                                                           BaseRuntimeAlertSummary = baseRuntimeAlert?.Summary,
                                                           BaseRuntimeAlertDetails = baseRuntimeAlert?.Details,
                                                           VulnerabilityAssessment = CreateVulnerabilityAssessment(latestSnapshot.ImageVersion,
                                                                                                                   CreateSeveritySummaryFromFindings(vulnerabilityFindings)),
                                                           VulnerabilityFindings = vulnerabilityFindings.Select(MapVulnerabilityFinding)
                                                                                                        .ToList(),
                                                           CurrentResourceUsage = GetCurrentResourceUsage(resourceUsageHistory),
                                                           ResourceUsageHistory = resourceUsageHistory,
                                                           ScanHistory = await GetRuntimeContainerScanHistoryAsync(dockerInstanceId, containerId, cancellationToken).ConfigureAwait(false),
                                                       };
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DockerInstanceListItemData>> GetDockerInstancesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var instances = await _dbContext.DockerInstances.Include(entity => entity.PortainerEndpoint)
                                                                                                .AsNoTracking()
                                                                                                .OrderBy(entity => entity.Name)
                                                                                                .ToListAsync(cancellationToken)
                                                                                                .ConfigureAwait(false);
                                                var latestSnapshots = await GetLatestContainerSnapshotsAsync(cancellationToken).ConfigureAwait(false);
                                                var latestResourceSamples = await GetLatestDockerInstanceResourceSamplesAsync(cancellationToken).ConfigureAwait(false);

                                                return instances.Select(entity =>
                                                                        {
                                                                            latestResourceSamples.TryGetValue(entity.Id, out var currentResourceUsage);

                                                                            return new DockerInstanceListItemData
                                                                                   {
                                                                                       Id = entity.Id,
                                                                                       Name = entity.Name,
                                                                                       EndpointUri = entity.EndpointUri,
                                                                                       ConnectionKind = entity.ConnectionKind.ToString(),
                                                                                       PortainerEnabled = entity.PortainerEndpoint is not null && entity.PortainerEndpoint.IsEnabled,
                                                                                       LatestScanStatus = GetLatestRuntimeScanStatus(entity.Id),
                                                                                       LatestScanCompletedAtUtc = _dbContext.ScanRuns
                                                                                                                            .Where(scan => scan.DockerInstanceId == entity.Id
                                                                                                                                           && scan.Type == ScanRunType.RuntimeContainer)
                                                                                                                            .OrderByDescending(scan => scan.StartedAtUtc)
                                                                                                                            .Select(scan => scan.CompletedAtUtc)
                                                                                                                            .FirstOrDefault(),
                                                                                       RuntimeContainerCount = latestSnapshots.Count(snapshot => snapshot.DockerInstanceId == entity.Id),
                                                                                       CurrentResourceUsage = currentResourceUsage is null ? null : MapResourceUsage(currentResourceUsage),
                                                                                   };
                                                                        })
                                                                .ToList();
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<DockerInstanceDetailViewData?> GetDockerInstanceDetailAsync(Guid dockerInstanceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var dockerInstance = await _dbContext.DockerInstances.Include(entity => entity.PortainerEndpoint)
                                                                                                     .AsNoTracking()
                                                                                                     .SingleOrDefaultAsync(entity => entity.Id == dockerInstanceId, cancellationToken)
                                                                                                     .ConfigureAwait(false);

                                                if (dockerInstance is null)
                                                {
                                                    return null;
                                                }

                                                var runtimeContainers = await GetRuntimeContainersCoreAsync(cancellationToken).ConfigureAwait(false);
                                                var resourceHistory = await GetDockerInstanceResourceHistoryAsync(dockerInstanceId, cancellationToken).ConfigureAwait(false);
                                                var latestScanCompletedAtUtc = await _dbContext.ScanRuns.Where(scan => scan.DockerInstanceId == dockerInstanceId
                                                                                                                       && scan.Type == ScanRunType.RuntimeContainer)
                                                                                                        .OrderByDescending(scan => scan.StartedAtUtc)
                                                                                                        .Select(scan => scan.CompletedAtUtc)
                                                                                                        .FirstOrDefaultAsync(cancellationToken)
                                                                                                        .ConfigureAwait(false);

                                                return new DockerInstanceDetailViewData
                                                       {
                                                           Id = dockerInstance.Id,
                                                           Name = dockerInstance.Name,
                                                           EndpointUri = dockerInstance.EndpointUri,
                                                           ConnectionKind = dockerInstance.ConnectionKind.ToString(),
                                                           PortainerEnabled = dockerInstance.PortainerEndpoint is not null && dockerInstance.PortainerEndpoint.IsEnabled,
                                                           LatestScanStatus = GetLatestRuntimeScanStatus(dockerInstanceId),
                                                           LatestScanCompletedAtUtc = latestScanCompletedAtUtc,
                                                           RuntimeContainerCount = runtimeContainers.Count(entity => entity.DockerInstanceId == dockerInstanceId),
                                                           CurrentResourceUsage = GetCurrentResourceUsage(resourceHistory),
                                                           ResourceUsageHistory = resourceHistory,
                                                           RuntimeContainers = runtimeContainers.Where(entity => entity.DockerInstanceId == dockerInstanceId)
                                                                                                .ToList(),
                                                       };
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> HasBaseImagesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var baseImages = await _sharedBaseImageQueryService.GetBaseImagesAsync(cancellationToken).ConfigureAwait(false);

                                                return baseImages.Count > 0;
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SharedBaseImageListItemData>> GetBaseImagesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(() => GetBaseImagesCoreAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScanHistoryItemData>> GetScanHistoryAsync(int take = 20, CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(() => GetScanHistoryCoreAsync(take, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    #endregion // IApplicationViewService

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        _dbContextLock.Dispose();
    }

    #endregion // IDisposable
}