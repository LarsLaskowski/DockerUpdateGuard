using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Queries;
using DockerUpdateGuard.Images;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.UI;

/// <summary>
/// Default UI query service
/// </summary>
public sealed class ApplicationViewService : IApplicationViewService, IDisposable
{
    #region Fields

    /// <summary>
    /// Resource-history window
    /// </summary>
    private static readonly TimeSpan ResourceHistoryWindow = TimeSpan.FromHours(24);

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
                   FixedVersion = entity.FixedVersion,
                   CvssScore = entity.CvssScore,
                   ReferenceUrl = entity.ReferenceUrl,
                   IsActive = entity.IsActive,
                   DetectedAtUtc = entity.DetectedAtUtc,
               };
    }

    /// <summary>
    /// Create vulnerability assessment view data for an image version
    /// </summary>
    /// <param name="imageVersion">Image version entity</param>
    /// <param name="activeFindingCount">Number of active findings</param>
    /// <returns>Vulnerability assessment view data</returns>
    private static VulnerabilityAssessmentViewData CreateVulnerabilityAssessment(ImageVersion imageVersion, int activeFindingCount)
    {
        return new VulnerabilityAssessmentViewData
               {
                   Status = FormatVulnerabilityAssessmentStatus(imageVersion.VulnerabilityAssessmentStatus),
                   Source = FormatVulnerabilitySource(imageVersion.VulnerabilityAssessmentSource),
                   Message = imageVersion.VulnerabilityAssessmentMessage,
                   CheckedAtUtc = imageVersion.VulnerabilityAssessmentCheckedAtUtc,
                   ActiveFindingCount = activeFindingCount,
               };
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
    /// Populate resolved semantic version tags for alias candidates
    /// </summary>
    /// <param name="candidates">Tag candidates</param>
    private static void PopulateResolvedVersionTags(IList<TagCandidateViewData> candidates)
    {
        foreach (var candidate in candidates)
        {
            candidate.ResolvedVersionTag = ResolveResolvedVersionTag(candidate.Tag,
                                                                     candidate.Digest,
                                                                     candidates);
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
        if (string.IsNullOrWhiteSpace(currentTag)
            || TryParseVersionTag(currentTag, out _))
        {
            return null;
        }

        var candidateList = candidates.Where(entity => string.IsNullOrWhiteSpace(entity.Tag) == false)
                                      .ToList();
        var currentTagCandidate = candidateList.FirstOrDefault(entity => string.Equals(entity.Tag, currentTag, StringComparison.OrdinalIgnoreCase)
                                                                         && string.Equals(entity.Digest ?? string.Empty,
                                                                                          currentDigest ?? string.Empty,
                                                                                          StringComparison.OrdinalIgnoreCase))
                                      ?? candidateList.FirstOrDefault(entity => string.Equals(entity.Tag, currentTag, StringComparison.OrdinalIgnoreCase));
        var digest = string.IsNullOrWhiteSpace(currentTagCandidate?.Digest) ? currentDigest : currentTagCandidate.Digest;

        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        var matchingCandidates = candidateList.Where(entity => string.Equals(entity.Digest, digest, StringComparison.OrdinalIgnoreCase)
                                                               && string.Equals(entity.Tag, currentTag, StringComparison.OrdinalIgnoreCase) == false)
                                              .ToList();

        if (matchingCandidates.Count == 0)
        {
            return null;
        }

        var semanticCandidates = matchingCandidates.Where(entity => TryParseVersionTag(entity.Tag, out _))
                                                   .Select(entity => new
                                                                     {
                                                                         Candidate = entity,
                                                                         Version = ParseVersionTag(entity.Tag),
                                                                     })
                                                   .OrderByDescending(entity => entity.Candidate.PublishedAtUtc)
                                                   .ThenByDescending(entity => entity.Version)
                                                   .ToList();

        if (semanticCandidates.Count > 0)
        {
            return semanticCandidates[0].Candidate.Tag;
        }

        return matchingCandidates.OrderByDescending(entity => entity.IsRecommended)
                                 .ThenByDescending(entity => entity.PublishedAtUtc)
                                 .ThenBy(entity => entity.Tag, StringComparer.OrdinalIgnoreCase)
                                 .Select(entity => entity.Tag)
                                 .FirstOrDefault();
    }

    /// <summary>
    /// Attempt to parse a semantic version tag
    /// </summary>
    /// <param name="value">Candidate tag</param>
    /// <param name="version">Parsed version</param>
    /// <returns>True when parsing succeeded</returns>
    private static bool TryParseVersionTag(string value, out Version version)
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
    /// <param name="value">Candidate tag</param>
    /// <returns>Parsed version</returns>
    private static Version ParseVersionTag(string value)
    {
        return Version.Parse(value.Trim().TrimStart('v', 'V'));
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
    /// Read dashboard data for the main overview
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dashboard view data</returns>
    public async Task<DashboardViewData> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var recentScans = await GetScanHistoryCoreAsync(10, cancellationToken).ConfigureAwait(false);
                                                var sharedBaseImages = await _sharedBaseImageQueryService.GetSharedBaseImagesAsync(cancellationToken).ConfigureAwait(false);
                                                var observedImageCount = await _dbContext.ObservedImages.CountAsync(cancellationToken).ConfigureAwait(false);
                                                var dockerInstanceCount = await _dbContext.DockerInstances.CountAsync(cancellationToken).ConfigureAwait(false);
                                                var runtimeContainers = await GetRuntimeContainersCoreAsync(cancellationToken).ConfigureAwait(false);
                                                var activeUpdateFindingCount = await _dbContext.UpdateFindings.CountAsync(entity => entity.IsActive, cancellationToken).ConfigureAwait(false);
                                                var activeVulnerabilityFindingCount = await _dbContext.VulnerabilityFindings.CountAsync(entity => entity.IsActive, cancellationToken).ConfigureAwait(false);

                                                return new DashboardViewData
                                                       {
                                                           ObservedImageCount = observedImageCount,
                                                           DockerInstanceCount = dockerInstanceCount,
                                                           RuntimeContainerCount = runtimeContainers.Count,
                                                           SharedBaseImageCount = sharedBaseImages.Count,
                                                           ActiveUpdateFindingCount = activeUpdateFindingCount,
                                                           ActiveVulnerabilityFindingCount = activeVulnerabilityFindingCount,
                                                           RecentScans = recentScans,
                                                       };
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read all observed images for the overview page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed image list items</returns>
    public async Task<IReadOnlyList<ObservedImageListItemData>> GetObservedImagesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var observedImages = await _dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion)
                                                                                                    .ThenInclude(entity => entity.RegistryRepository)
                                                                                                    .AsNoTracking()
                                                                                                    .ToListAsync(cancellationToken)
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

                                                return observedImages.Select(entity =>
                                                                             {
                                                                                 var repositoryKey = CreateRepositoryKey(entity.CurrentImageVersion);
                                                                                 observedAlertLookup.TryGetValue(entity.Id, out var observedAlert);
                                                                                 runtimeAlertLookup.TryGetValue(repositoryKey, out var runtimeAlert);
                                                                                 var baseRuntimeAlert = observedAlert ?? runtimeAlert;

                                                                                 return new ObservedImageListItemData
                                                                                        {
                                                                                            Id = entity.Id,
                                                                                            Name = entity.Name,
                                                                                            Description = entity.Description,
                                                                                            ImageReference = _imageReferenceParser.Format(entity.CurrentImageVersion),
                                                                                            LatestScanStatus = GetLatestObservedScanStatus(entity.Id),
                                                                                            LatestScanMessage = GetLatestObservedScanMessage(entity.Id),
                                                                                            ActiveUpdateFindingCount = _dbContext.UpdateFindings.Count(finding => finding.ObservedImageId == entity.Id && finding.IsActive),
                                                                                            ActiveVulnerabilityFindingCount = _dbContext.VulnerabilityFindings.Count(finding => finding.ImageVersionId == entity.CurrentImageVersionId && finding.IsActive),
                                                                                            VulnerabilityStatus = FormatVulnerabilityAssessmentStatus(entity.CurrentImageVersion.VulnerabilityAssessmentStatus),
                                                                                            VulnerabilityMessage = entity.CurrentImageVersion.VulnerabilityAssessmentMessage,
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

    /// <summary>
    /// Read the detail view for a single observed image
    /// </summary>
    /// <param name="observedImageId">ID of the observed image</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Observed image detail view data or null</returns>
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

                                                var baseImages = await _dbContext.ImageRelationships.Include(entity => entity.BaseImageVersion)
                                                                                                    .ThenInclude(entity => entity.RegistryRepository)
                                                                                                    .Where(entity => entity.ChildImageVersionId == observedImage.CurrentImageVersionId
                                                                                                                     && entity.RelationshipType == ImageRelationshipType.BaseImage)
                                                                                                    .OrderBy(entity => entity.Depth)
                                                                                                    .AsNoTracking()
                                                                                                    .ToListAsync(cancellationToken)
                                                                                                    .ConfigureAwait(false);

                                                var updateFindings = await _dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                                                                    .Where(entity => entity.ObservedImageId == observedImage.Id)
                                                                                                    .OrderByDescending(entity => entity.DetectedAtUtc)
                                                                                                    .AsNoTracking()
                                                                                                    .ToListAsync(cancellationToken)
                                                                                                    .ConfigureAwait(false);
                                                var vulnerabilityFindings = await _dbContext.VulnerabilityFindings.Where(entity => entity.ImageVersionId == observedImage.CurrentImageVersionId)
                                                                                                                  .OrderByDescending(entity => entity.DetectedAtUtc)
                                                                                                                  .AsNoTracking()
                                                                                                                  .ToListAsync(cancellationToken)
                                                                                                                  .ConfigureAwait(false);
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

                                                return new ObservedImageDetailViewData
                                                       {
                                                           Id = observedImage.Id,
                                                           Name = observedImage.Name,
                                                           Description = observedImage.Description,
                                                           ImageReference = _imageReferenceParser.Format(observedImage.CurrentImageVersion),
                                                           LatestScanStatus = GetLatestObservedScanStatus(observedImage.Id),
                                                           LatestScanMessage = GetLatestObservedScanMessage(observedImage.Id),
                                                           IsOwnImage = observedImage.Source == RegistrationSource.Discovery,
                                                           BaseImages = baseImages.Select(entity => new BaseImageRelationshipData
                                                                                                    {
                                                                                                        ImageReference = _imageReferenceParser.Format(entity.BaseImageVersion),
                                                                                                        Depth = entity.Depth,
                                                                                                        SourceReference = entity.SourceReference,
                                                                                                    })
                                                                                  .ToList(),
                                                           UpdateFindings = combinedUpdateFindings.Select(entity => MapUpdateFinding(entity, recommendedImageVersions, manualSelection: null))
                                                                                                  .ToList(),
                                                           BaseRuntimeAlertSummary = baseRuntimeAlert?.Summary,
                                                           BaseRuntimeAlertDetails = baseRuntimeAlert?.Details,
                                                           VulnerabilityAssessment = CreateVulnerabilityAssessment(observedImage.CurrentImageVersion,
                                                                                                                   vulnerabilityFindings.Count(entity => entity.IsActive)),
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

    /// <summary>
    /// Read all runtime containers for the overview page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Runtime container list items</returns>
    public async Task<IReadOnlyList<RuntimeContainerListItemData>> GetRuntimeContainersAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(() => GetRuntimeContainersCoreAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read the detail view for a runtime container
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="containerId">ID of the container</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Runtime container detail view data or null</returns>
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

                                                var vulnerabilityFindings = await _dbContext.VulnerabilityFindings.Where(entity => entity.ImageVersionId == latestSnapshot.ImageVersionId)
                                                                                                                  .OrderByDescending(entity => entity.DetectedAtUtc)
                                                                                                                  .AsNoTracking()
                                                                                                                  .ToListAsync(cancellationToken)
                                                                                                                  .ConfigureAwait(false);

                                                var mappedUpdateFindings = updateFindings.Select(entity => MapUpdateFinding(entity, recommendedImageVersions, manualSelection))
                                                                                         .ToList();

                                                var availableTagCandidates = mappedUpdateFindings.SelectMany(entity => entity.TagCandidates)
                                                                                                 .OrderByDescending(entity => entity.IsRecommended)
                                                                                                 .ThenBy(entity => entity.Tag)
                                                                                                 .ToList();
                                                var resolvedVersionTag = ResolveResolvedVersionTag(latestSnapshot.ImageVersion.Tag,
                                                                                                   latestSnapshot.ImageVersion.Digest,
                                                                                                   availableTagCandidates);

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
                                                           ManualSelectionImage = manualSelection is null
                                                                                      ? null
                                                                                      : FormatImageReference(registryRepository.Registry,
                                                                                                             registryRepository.Repository,
                                                                                                             manualSelection.Tag,
                                                                                                             manualSelection.Digest),
                                                           ManualSelectionAtUtc = manualSelection?.SelectedAtUtc,
                                                           AvailableTagCandidates = availableTagCandidates,
                                                           UpdateFindings = mappedUpdateFindings,
                                                           BaseRuntimeAlertSummary = baseRuntimeAlert?.Summary,
                                                           BaseRuntimeAlertDetails = baseRuntimeAlert?.Details,
                                                           VulnerabilityAssessment = CreateVulnerabilityAssessment(latestSnapshot.ImageVersion,
                                                                                                                   vulnerabilityFindings.Count(entity => entity.IsActive)),
                                                           VulnerabilityFindings = vulnerabilityFindings.Select(MapVulnerabilityFinding)
                                                                                                        .ToList(),
                                                           CurrentResourceUsage = resourceUsageHistory.FirstOrDefault(),
                                                           ResourceUsageHistory = resourceUsageHistory,
                                                           ScanHistory = await GetRuntimeContainerScanHistoryAsync(dockerInstanceId, containerId, cancellationToken).ConfigureAwait(false),
                                                       };
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read all Docker instances for the overview page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Docker instance list items</returns>
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

    /// <summary>
    /// Read the detail view for a single Docker instance
    /// </summary>
    /// <param name="dockerInstanceId">ID of the Docker instance</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Docker instance detail view data or null</returns>
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

                                                return new DockerInstanceDetailViewData
                                                       {
                                                           Id = dockerInstance.Id,
                                                           Name = dockerInstance.Name,
                                                           EndpointUri = dockerInstance.EndpointUri,
                                                           ConnectionKind = dockerInstance.ConnectionKind.ToString(),
                                                           PortainerEnabled = dockerInstance.PortainerEndpoint is not null && dockerInstance.PortainerEndpoint.IsEnabled,
                                                           LatestScanStatus = GetLatestRuntimeScanStatus(dockerInstanceId),
                                                           LatestScanCompletedAtUtc = _dbContext.ScanRuns
                                                                                                .Where(scan => scan.DockerInstanceId == dockerInstanceId
                                                                                                               && scan.Type == ScanRunType.RuntimeContainer)
                                                                                                .OrderByDescending(scan => scan.StartedAtUtc)
                                                                                                .Select(scan => scan.CompletedAtUtc)
                                                                                                .FirstOrDefault(),
                                                           RuntimeContainerCount = runtimeContainers.Count(entity => entity.DockerInstanceId == dockerInstanceId),
                                                           CurrentResourceUsage = resourceHistory.FirstOrDefault(),
                                                           ResourceUsageHistory = resourceHistory,
                                                           RuntimeContainers = runtimeContainers.Where(entity => entity.DockerInstanceId == dockerInstanceId)
                                                                                                .ToList(),
                                                       };
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read shared base images for the overview page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Shared base image list items</returns>
    public async Task<IReadOnlyList<SharedBaseImageListItemData>> GetSharedBaseImagesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(async () =>
                                            {
                                                var sharedBaseImages = await _sharedBaseImageQueryService.GetSharedBaseImagesAsync(cancellationToken).ConfigureAwait(false);

                                                return sharedBaseImages.Select(MapSharedBaseImage)
                                                                       .ToList();
                                            },
                                            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read scan history entries for the activity overview
    /// </summary>
    /// <param name="take">Maximum number of entries</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scan history entries</returns>
    public async Task<IReadOnlyList<ScanHistoryItemData>> GetScanHistoryAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        return await ExecuteSerializedAsync(() => GetScanHistoryCoreAsync(take, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    #endregion // Methods

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
        var latestResourceSamples = await GetLatestRuntimeContainerResourceSamplesAsync(cancellationToken).ConfigureAwait(false);
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

        return latestSnapshots.Select(entity =>
                                      {
                                          var dockerInstance = entity.DockerInstance;
                                          var imageVersion = entity.ImageVersion;
                                          var repositoryKey = CreateRepositoryKey(entity);

                                          ArgumentNullException.ThrowIfNull(dockerInstance);
                                          ArgumentNullException.ThrowIfNull(imageVersion);

                                          ownImagesByRepository.TryGetValue(repositoryKey, out var linkedObservedImage);
                                          latestResourceSamples.TryGetValue(CreateContainerKey(entity.DockerInstanceId, entity.ContainerId), out var currentResourceUsage);
                                          tagCandidatesBySnapshot.TryGetValue(entity.Id, out var tagCandidates);

                                          var resolvedVersionTag = tagCandidates is null
                                                                       ? null
                                                                       : ResolveResolvedVersionTag(imageVersion.Tag, imageVersion.Digest, tagCandidates);

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
                                                     PortainerAvailable = dockerInstance.PortainerEndpoint is not null && dockerInstance.PortainerEndpoint.IsEnabled,
                                                     ActiveVulnerabilityFindingCount = _dbContext.VulnerabilityFindings.Count(finding => finding.ImageVersionId == entity.ImageVersionId && finding.IsActive),
                                                     VulnerabilityStatus = FormatVulnerabilityAssessmentStatus(imageVersion.VulnerabilityAssessmentStatus),
                                                     VulnerabilitySummary = imageVersion.VulnerabilityAssessmentMessage,
                                                     RecordedAtUtc = entity.RecordedAtUtc,
                                                     LinkedObservedImageId = linkedObservedImage?.Id,
                                                     LinkedObservedImageName = linkedObservedImage?.Name,
                                                     CurrentResourceUsage = currentResourceUsage is null ? null : MapResourceUsage(currentResourceUsage),
                                                 };
                                      })
                              .OrderByDescending(entity => entity.LinkedObservedImageId.HasValue)
                              .ThenBy(entity => entity.DockerInstanceName)
                              .ThenBy(entity => entity.ContainerName)
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

    #region IDisposable implementation

    /// <summary>
    /// Releases the resources used by the current instance of the class
    /// </summary>
    public void Dispose()
    {
        _dbContextLock.Dispose();
    }

    #endregion // IDisposable implementation

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
    /// Load latest runtime container resource samples
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Latest container samples by container key</returns>
    private async Task<Dictionary<string, RuntimeContainerResourceSample>> GetLatestRuntimeContainerResourceSamplesAsync(CancellationToken cancellationToken)
    {
        var samples = await _dbContext.RuntimeContainerResourceSamples.AsNoTracking()
                                                                      .OrderByDescending(entity => entity.RecordedAtUtc)
                                                                      .ToListAsync(cancellationToken)
                                                                      .ConfigureAwait(false);

        return samples.GroupBy(entity => CreateContainerKey(entity.DockerInstanceId, entity.ContainerId))
                      .ToDictionary(group => group.Key,
                                    group => group.First(),
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
        var cutoff = DateTimeOffset.UtcNow.Subtract(ResourceHistoryWindow);
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
        var cutoff = DateTimeOffset.UtcNow.Subtract(ResourceHistoryWindow);
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
    /// Map a shared base image entry to view data
    /// </summary>
    /// <param name="entity">Shared base image usage data</param>
    /// <returns>Shared base image item</returns>
    private SharedBaseImageListItemData MapSharedBaseImage(SharedBaseImageUsageData entity)
    {
        return new SharedBaseImageListItemData
               {
                   BaseImageVersionId = entity.BaseImageVersionId,
                   ImageReference = FormatImageReference(entity.Registry, entity.Repository, entity.Tag, entity.Digest),
                   ObservedImageCount = entity.ObservedImageCount,
                   ActiveFindingCount = _dbContext.UpdateFindings.Count(finding => finding.SubjectImageVersionId == entity.BaseImageVersionId && finding.IsActive),
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
                                                   IReadOnlyDictionary<Guid, ImageVersion> recommendedImageVersions,
                                                   RuntimeContainerTagSelection? manualSelection)
    {
        ImageVersion? recommendedImageVersion = null;

        if (entity.RecommendedImageVersionId is not null)
        {
            recommendedImageVersions.TryGetValue(entity.RecommendedImageVersionId.GetValueOrDefault(), out recommendedImageVersion);
        }

        var tagCandidates = entity.TagCandidates.OrderBy(candidate => candidate.Rank)
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
                                                                     })
                                                .ToList();

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

    #endregion // Methods
}