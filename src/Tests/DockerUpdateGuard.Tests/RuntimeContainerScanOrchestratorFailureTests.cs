using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for the failure handling of the runtime container orchestration path
/// </summary>
[TestClass]
public class RuntimeContainerScanOrchestratorFailureTests
{
    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Create an update detection service with a clock fixed at 2030-01-01T00:00:00Z
    /// </summary>
    /// <param name="optionsMonitor">Options monitor</param>
    /// <returns>Update detection service</returns>
    private static UpdateDetectionService CreateUpdateDetectionService(IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor)
    {
        var clock = Substitute.For<TimeProvider>();

        clock.GetUtcNow().Returns(new DateTimeOffset(2030, 01, 01, 00, 00, 00, TimeSpan.Zero));

        return new UpdateDetectionService(optionsMonitor, clock);
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify a runtime container keeps its derived finding when the .NET release feed fails in a later scan
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncKeepsDerivedFindingWhenDotNetReleaseFeedFailsAsync()
    {
        var outcome = await RunTwoDerivedScansAsync("DOTNET_VERSION=9.0.13",
                                                    CreateInspectResult("DOTNET_VERSION=9.0.13"),
                                                    CreateHistoryResult(),
                                                    ExternalOperationResult.Failed<DotNetChannelReleaseData>("The release feed is temporarily unavailable"),
                                                    CreateNginxFeedResult()).ConfigureAwait(false);

        AssertDerivedFindingKept(outcome, "a failed .NET release feed");
    }

    /// <summary>
    /// Verify a runtime container keeps its derived finding when the nginx release feed fails in a later scan
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncKeepsDerivedFindingWhenNginxReleaseFeedFailsAsync()
    {
        var outcome = await RunTwoDerivedScansAsync("NGINX_VERSION=1.29.1",
                                                    CreateInspectResult("NGINX_VERSION=1.29.1"),
                                                    CreateHistoryResult(),
                                                    CreateDotNetFeedResult(),
                                                    ExternalOperationResult.NotFound<NginxChannelReleaseData>("The channel is missing from the feed")).ConfigureAwait(false);

        AssertDerivedFindingKept(outcome, "an nginx release feed that does not answer");
    }

    /// <summary>
    /// Verify a runtime container keeps its derived finding when the image inspect fails in a later scan
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncKeepsDerivedFindingWhenImageInspectFailsAsync()
    {
        var outcome = await RunTwoDerivedScansAsync("DOTNET_VERSION=9.0.13",
                                                    ExternalOperationResult.Failed<DockerImageInspectData>("The Docker Engine is temporarily unavailable"),
                                                    CreateHistoryResult(),
                                                    CreateDotNetFeedResult(),
                                                    CreateNginxFeedResult()).ConfigureAwait(false);

        AssertDerivedFindingKept(outcome, "a failed image inspect");
    }

    /// <summary>
    /// Verify a runtime container keeps its derived finding when the image history fails in a later scan
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncKeepsDerivedFindingWhenImageHistoryFailsAsync()
    {
        var outcome = await RunTwoDerivedScansAsync("DOTNET_VERSION=9.0.13",
                                                    CreateInspectResult("DOTNET_VERSION=9.0.13"),
                                                    ExternalOperationResult.Failed<IReadOnlyList<DockerImageHistoryEntryData>>("The Docker Engine is temporarily unavailable"),
                                                    CreateDotNetFeedResult(),
                                                    CreateNginxFeedResult()).ConfigureAwait(false);

        AssertDerivedFindingKept(outcome, "a failed image history");
    }

    /// <summary>
    /// Verify a runtime container resolves its derived finding when the runtime is current in a later scan
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncResolvesDerivedFindingWhenRuntimeIsCurrentAsync()
    {
        var outcome = await RunTwoDerivedScansAsync("DOTNET_VERSION=9.0.13",
                                                    CreateInspectResult("DOTNET_VERSION=9.0.13"),
                                                    CreateHistoryResult(),
                                                    ExternalOperationResult.Succeeded<DotNetChannelReleaseData>(new DotNetChannelReleaseData
                                                                                                                {
                                                                                                                    ChannelVersion = "9.0",
                                                                                                                    LatestRuntimeVersion = new Version(9, 0, 13),
                                                                                                                }),
                                                    CreateNginxFeedResult()).ConfigureAwait(false);

        AssertDerivedFindingResolved(outcome, "a runtime that is no longer behind the feed");
    }

    /// <summary>
    /// Verify a runtime container resolves its derived finding when no runtime is detected in a later scan
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncResolvesDerivedFindingWhenNoRuntimeIsDetectedAsync()
    {
        var outcome = await RunTwoDerivedScansAsync("DOTNET_VERSION=9.0.13",
                                                    CreateInspectResult("UNRELATED_SETTING=value"),
                                                    CreateHistoryResult(),
                                                    CreateDotNetFeedResult(),
                                                    CreateNginxFeedResult()).ConfigureAwait(false);

        AssertDerivedFindingResolved(outcome, "an image without a detected runtime");
    }

    /// <summary>
    /// Verify a runtime container replaces its derived finding when the runtime is still outdated in a later scan
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncReplacesDerivedFindingWhenRuntimeIsStillOutdatedAsync()
    {
        var outcome = await RunTwoDerivedScansAsync("DOTNET_VERSION=9.0.13",
                                                    CreateInspectResult("DOTNET_VERSION=9.0.13"),
                                                    CreateHistoryResult(),
                                                    CreateDotNetFeedResult(),
                                                    CreateNginxFeedResult()).ConfigureAwait(false);
        var derivedFindings = outcome.Findings.Where(entity => entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate)
                                              .ToList();
        var activeFindings = derivedFindings.Where(entity => entity.IsActive)
                                            .ToList();
        var previousFinding = derivedFindings.Single(entity => entity.Id == outcome.FirstFindingId);

        Assert.HasCount(1,
                        activeFindings,
                        "The container must have exactly one active derived finding after a runtime that is still outdated");
        Assert.AreNotEqual(outcome.FirstFindingId,
                           activeFindings[0].Id,
                           "The active derived finding must be a new one");
        Assert.AreEqual(outcome.SecondSnapshotId,
                        activeFindings[0].ContainerSnapshotId,
                        "The new derived finding must reference the snapshot of the second scan");
        Assert.IsFalse(previousFinding.IsActive, "The previous derived finding must be inactive after it was replaced");
        Assert.IsNotNull(previousFinding.ResolvedAtUtc, "The previous derived finding must have a resolution time after it was replaced");
    }

    /// <summary>
    /// Verify a failing release feed still deactivates the previous runtime image update finding
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncDeactivatesRuntimeImageUpdateFindingWhenReleaseFeedFailsAsync()
    {
        var outcome = await RunTwoDerivedScansAsync("DOTNET_VERSION=9.0.13",
                                                    CreateInspectResult("DOTNET_VERSION=9.0.13"),
                                                    CreateHistoryResult(),
                                                    ExternalOperationResult.Failed<DotNetChannelReleaseData>("The release feed is temporarily unavailable"),
                                                    CreateNginxFeedResult(),
                                                    [
                                                        new DockerHubTagData
                                                        {
                                                            Tag = "1.1.0",
                                                            Digest = "sha256:newer",
                                                            PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                        },
                                                        new DockerHubTagData
                                                        {
                                                            Tag = "1.0.0",
                                                            Digest = "sha256:current",
                                                            PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                        },
                                                    ]).ConfigureAwait(false);
        var runtimeFindings = outcome.Findings.Where(entity => entity.Type == UpdateFindingType.RuntimeImageUpdate)
                                              .ToList();
        var inactiveFindings = runtimeFindings.Where(entity => entity.IsActive == false)
                                              .ToList();

        Assert.HasCount(2,
                        runtimeFindings,
                        "Each scan must create a runtime image update finding");
        Assert.HasCount(1,
                        inactiveFindings,
                        "The previous runtime image update finding must be deactivated after a failed release feed");
        Assert.IsNotNull(inactiveFindings[0].ResolvedAtUtc, "The deactivated runtime image update finding must have a resolution time");
        Assert.AreNotEqual(outcome.SecondSnapshotId,
                           inactiveFindings[0].ContainerSnapshotId,
                           "The deactivated runtime image update finding must stay on the first snapshot");
    }

    /// <summary>
    /// Verify a failed tag lookup in a later scan moves the previous derived finding to the new snapshot
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncMovesDerivedFindingWhenTagLookupFailedAsync()
    {
        var outcome = await RunTwoScansAsync("DOTNET_VERSION=9.0.13",
                                             CreateInspectResult("DOTNET_VERSION=9.0.13"),
                                             _ => Task.FromResult(CreateHistoryResult()),
                                             CreateDotNetFeedResult(),
                                             CreateNginxFeedResult(),
                                             CreateTagsWithNewerVersion(),
                                             _ => Task.FromResult(ExternalOperationResult.Failed<IReadOnlyList<DockerHubTagData>>("The registry is temporarily unavailable"))).ConfigureAwait(false);

        AssertDerivedFindingKept((outcome.FirstDerivedFindingId, outcome.SecondSnapshotId, outcome.Findings), "a failed tag lookup");
    }

    /// <summary>
    /// Verify a processing error after the snapshot was recorded moves every previous finding to the failed snapshot
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncMovesFindingsWhenContainerProcessingThrowsAsync()
    {
        var outcome = await RunTwoScansAsync("DOTNET_VERSION=9.0.13",
                                             CreateInspectResult("DOTNET_VERSION=9.0.13"),
                                             _ => Task.FromResult(CreateHistoryResult()),
                                             CreateDotNetFeedResult(),
                                             CreateNginxFeedResult(),
                                             CreateTagsWithNewerVersion(),
                                             _ => Task.FromException<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>>(new InvalidOperationException("Tag lookup exploded"))).ConfigureAwait(false);

        Assert.IsGreaterThan(1,
                             outcome.FirstFindings.Count,
                             "The first scan must create update findings and the derived finding");
        Assert.HasCount(outcome.FirstFindings.Count,
                        outcome.Findings,
                        "A processing error must neither create nor remove findings");
        Assert.AreEqual(UpdateAssessmentStatus.Failed,
                        outcome.SecondAssessmentStatus,
                        "The snapshot of the failed scan must be assessed as failed");

        foreach (var previous in outcome.FirstFindings)
        {
            var finding = outcome.Findings.Single(entity => entity.Id == previous.Id);

            Assert.IsTrue(finding.IsActive, $"The previous {previous.Type} finding must stay active after a processing error");
            Assert.IsNull(finding.ResolvedAtUtc, $"The previous {previous.Type} finding must have no resolution time after a processing error");
            Assert.AreEqual(outcome.SecondSnapshotId,
                            finding.ContainerSnapshotId,
                            $"The previous {previous.Type} finding must move to the failed snapshot after a processing error");
            Assert.AreEqual(outcome.FirstCandidateCounts.GetValueOrDefault(previous.Id),
                            outcome.CandidateCounts.GetValueOrDefault(previous.Id),
                            $"The previous {previous.Type} finding must keep its tag candidates after a processing error");
        }
    }

    /// <summary>
    /// Verify a processing error in the runtime check moves only the findings still active to the new snapshot
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncMovesOnlyActiveFindingsWhenRuntimeCheckThrowsAsync()
    {
        var outcome = await RunTwoScansAsync("DOTNET_VERSION=9.0.13",
                                             CreateInspectResult("DOTNET_VERSION=9.0.13"),
                                             _ => Task.FromException<ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>>(new InvalidOperationException("History exploded")),
                                             CreateDotNetFeedResult(),
                                             CreateNginxFeedResult(),
                                             CreateTagsWithNewerVersion(),
                                             null).ConfigureAwait(false);
        var previousUpdate = outcome.FirstFindings.Single(entity => entity.Type == UpdateFindingType.RuntimeImageUpdate);
        var updateFindings = outcome.Findings.Where(entity => entity.Type != UpdateFindingType.DerivedBaseRuntimeUpdate)
                                             .ToList();
        var activeUpdates = updateFindings.Where(entity => entity.IsActive)
                                          .ToList();
        var resolvedUpdate = updateFindings.Single(entity => entity.Id == previousUpdate.Id);

        Assert.AreEqual(UpdateAssessmentStatus.Failed,
                        outcome.SecondAssessmentStatus,
                        "The snapshot of the scan whose runtime check failed must be assessed as failed");
        Assert.IsFalse(resolvedUpdate.IsActive, "The previous update finding must be inactive after the second evaluation replaced it");
        Assert.IsNotNull(resolvedUpdate.ResolvedAtUtc, "The previous update finding must have a resolution time");
        Assert.AreEqual(outcome.FirstSnapshotId,
                        resolvedUpdate.ContainerSnapshotId,
                        "A finding the failed processing deactivated must stay on the first snapshot");
        Assert.HasCount(1,
                        activeUpdates,
                        "The update finding of the second evaluation must be the only active update finding");
        Assert.AreEqual(outcome.SecondSnapshotId,
                        activeUpdates[0].ContainerSnapshotId,
                        "The update finding of the second evaluation must reference the second snapshot");
        AssertDerivedFindingKept((outcome.FirstDerivedFindingId, outcome.SecondSnapshotId, outcome.Findings), "a failed runtime check");
    }

    /// <summary>
    /// Verify a failing image inspect still records a failed snapshot that keeps the previous findings
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncRecordsFailedSnapshotWhenImageInspectThrowsAsync()
    {
        var exception = new InvalidOperationException("Inspect exploded");
        var outcome = await RunAliasScansAsync(new AliasScanSecondRun
                                               {
                                                   InspectException = exception
                                               }).ConfigureAwait(false);

        Assert.HasCount(1, outcome.SecondSnapshots, "The second run must record a snapshot although the image inspect threw");

        var second = outcome.SecondSnapshots[0];

        Assert.AreEqual(outcome.FirstSnapshot.ImageVersionId,
                        second.ImageVersionId,
                        "The snapshot must reference the catalogued image version of the second scan");
        AssertFailedSnapshot(second, exception);
        AssertFindingsKept(outcome, second);
    }

    /// <summary>
    /// Verify a failing catalog call records a failed snapshot with the image version and version tags of the previous snapshot
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncRecordsFailedSnapshotFromPreviousSnapshotWhenImageCatalogThrowsAsync()
    {
        var exception = new InvalidOperationException("Catalog exploded");
        var outcome = await RunAliasScansAsync(new AliasScanSecondRun
                                               {
                                                   CatalogException = exception
                                               }).ConfigureAwait(false);

        AssertFirstSnapshotHasVersions(outcome);
        Assert.HasCount(1, outcome.SecondSnapshots, "The second run must record a snapshot although the catalog call threw");

        var second = outcome.SecondSnapshots[0];

        Assert.AreEqual(outcome.FirstSnapshot.ImageVersionId,
                        second.ImageVersionId,
                        "The snapshot must take the image version of the previous snapshot");
        Assert.AreEqual(outcome.FirstSnapshot.ResolvedVersionTag,
                        second.ResolvedVersionTag,
                        "The snapshot must take the resolved version of the previous snapshot");
        Assert.AreEqual(outcome.FirstSnapshot.AvailableUpdateVersionTag,
                        second.AvailableUpdateVersionTag,
                        "The snapshot must take the available update version of the previous snapshot");
        Assert.AreNotEqual(outcome.FirstSnapshot.RecordedAtUtc,
                           second.RecordedAtUtc,
                           "The snapshot must be recorded now, never with the time of the previous snapshot");
        Assert.AreEqual(ScanRunStatus.Partial, outcome.SecondRunStatus, "A container that could not be processed must make the run partial");
        AssertFailedSnapshot(second, exception);
        AssertFindingsKept(outcome, second);
    }

    /// <summary>
    /// Verify a failed tag lookup keeps the version tags of the previous snapshot
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncKeepsVersionTagsWhenTagLookupFailsAsync()
    {
        var outcome = await RunAliasScansAsync(new AliasScanSecondRun
                                               {
                                                   Tags = () => Task.FromResult(ExternalOperationResult.Failed<IReadOnlyList<DockerHubTagData>>("The registry is temporarily unavailable")),
                                               }).ConfigureAwait(false);

        AssertFirstSnapshotHasVersions(outcome);
        Assert.HasCount(1, outcome.SecondSnapshots, "The second run must record one snapshot");
        Assert.AreEqual(outcome.FirstSnapshot.ResolvedVersionTag,
                        outcome.SecondSnapshots[0].ResolvedVersionTag,
                        "A failed tag lookup must keep the resolved version of the previous snapshot");
        Assert.AreEqual(outcome.FirstSnapshot.AvailableUpdateVersionTag,
                        outcome.SecondSnapshots[0].AvailableUpdateVersionTag,
                        "A failed tag lookup must keep the available update version of the previous snapshot");
    }

    /// <summary>
    /// Verify a processing error after the snapshot was recorded keeps the version tags of the previous snapshot
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncKeepsVersionTagsWhenContainerProcessingThrowsAsync()
    {
        var outcome = await RunAliasScansAsync(new AliasScanSecondRun
                                               {
                                                   Tags = () => Task.FromException<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>>(new InvalidOperationException("Tag lookup exploded")),
                                               }).ConfigureAwait(false);

        AssertFirstSnapshotHasVersions(outcome);
        Assert.HasCount(1, outcome.SecondSnapshots, "The second run must record one snapshot");
        Assert.AreEqual(UpdateAssessmentStatus.Failed,
                        outcome.SecondSnapshots[0].UpdateAssessmentStatus,
                        "The snapshot of the failed processing must be assessed as failed");
        Assert.AreEqual(outcome.FirstSnapshot.ResolvedVersionTag,
                        outcome.SecondSnapshots[0].ResolvedVersionTag,
                        "A processing error must keep the resolved version of the previous snapshot");
        Assert.AreEqual(outcome.FirstSnapshot.AvailableUpdateVersionTag,
                        outcome.SecondSnapshots[0].AvailableUpdateVersionTag,
                        "A processing error must keep the available update version of the previous snapshot");
    }

    /// <summary>
    /// Verify a repository that is not found clears the version tags of the previous snapshot
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncStoresNoVersionTagsWhenRepositoryIsNotFoundAsync()
    {
        var outcome = await RunAliasScansAsync(new AliasScanSecondRun
                                               {
                                                   Tags = () => Task.FromResult(ExternalOperationResult.NotFound<IReadOnlyList<DockerHubTagData>>("The repository does not exist")),
                                               }).ConfigureAwait(false);

        AssertFirstSnapshotHasVersions(outcome);
        Assert.HasCount(1, outcome.SecondSnapshots, "The second run must record one snapshot");
        Assert.IsNull(outcome.SecondSnapshots[0].ResolvedVersionTag, "A repository that is not found must leave no resolved version");
        Assert.IsNull(outcome.SecondSnapshots[0].AvailableUpdateVersionTag, "A repository that is not found must leave no available update version");
    }

    /// <summary>
    /// Verify a failed tag lookup does not carry the version tags of a snapshot of another image version
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncKeepsNoVersionTagsOfAnotherImageVersionAsync()
    {
        var outcome = await RunAliasScansAsync(new AliasScanSecondRun
                                               {
                                                   ImageReference = "docker.io/library/phpmyadmin:latest@sha256:other",
                                                   Tags = () => Task.FromResult(ExternalOperationResult.Failed<IReadOnlyList<DockerHubTagData>>("The registry is temporarily unavailable")),
                                               }).ConfigureAwait(false);

        AssertFirstSnapshotHasVersions(outcome);
        Assert.HasCount(1, outcome.SecondSnapshots, "The second run must record one snapshot");
        Assert.AreNotEqual(outcome.FirstSnapshot.ImageVersionId,
                           outcome.SecondSnapshots[0].ImageVersionId,
                           "The running digest changed, so the second snapshot must name another image version");
        Assert.IsNull(outcome.SecondSnapshots[0].ResolvedVersionTag, "The resolved version of another image version must not be carried");
        Assert.IsNull(outcome.SecondSnapshots[0].AvailableUpdateVersionTag, "The available update version of another image version must not be carried");
    }

    /// <summary>
    /// Verify the failed snapshot of a container without a running digest takes the image version but no version tags
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncCarriesNoVersionTagsWithoutRunningDigestWhenImageCatalogThrowsAsync()
    {
        var outcome = await RunAliasScansAsync(new AliasScanSecondRun
                                               {
                                                   ImageReference = "docker.io/library/phpmyadmin:latest",
                                                   CatalogException = new InvalidOperationException("Catalog exploded"),
                                               }).ConfigureAwait(false);

        AssertFirstSnapshotHasVersions(outcome);
        Assert.HasCount(1, outcome.SecondSnapshots, "The second run must record a snapshot although the catalog call threw");
        Assert.AreEqual(outcome.FirstSnapshot.ImageVersionId,
                        outcome.SecondSnapshots[0].ImageVersionId,
                        "The snapshot must take the image version of the previous snapshot");
        Assert.IsNull(outcome.SecondSnapshots[0].ResolvedVersionTag, "Without a running digest the resolved version must not be carried");
        Assert.IsNull(outcome.SecondSnapshots[0].AvailableUpdateVersionTag, "Without a running digest the available update version must not be carried");
    }

    /// <summary>
    /// Verify a container without an earlier snapshot gets no snapshot when its processing fails before the snapshot
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncRecordsNoSnapshotWhenNewContainerImageCatalogThrowsAsync()
    {
        var outcome = await RunAliasScansAsync(new AliasScanSecondRun
                                               {
                                                   ContainerId = "container-2",
                                                   CatalogException = new InvalidOperationException("Catalog exploded"),
                                               }).ConfigureAwait(false);

        Assert.IsEmpty(outcome.SecondSnapshots, "A container without an earlier snapshot must get no snapshot when its processing fails before the snapshot");
        Assert.AreEqual(ScanRunStatus.Partial, outcome.SecondRunStatus, "A container that could not be processed must make the run partial");
    }

    /// <summary>
    /// Create registry tags with the current tag and a newer one, so a scan creates a runtime image update
    /// </summary>
    /// <returns>Tags</returns>
    private static IReadOnlyList<DockerHubTagData> CreateTagsWithNewerVersion()
    {
        return [
                   new DockerHubTagData
                   {
                       Tag = "1.1.0",
                       Digest = "sha256:newer",
                       PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                   },
                   new DockerHubTagData
                   {
                       Tag = "1.0.0",
                       Digest = "sha256:current",
                       PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                   },
               ];
    }

    /// <summary>
    /// Create a successful image inspect answer with one environment variable
    /// </summary>
    /// <param name="environmentVariable">Environment variable the detector reads the runtime from</param>
    /// <returns>Inspect answer</returns>
    private static ExternalOperationResult<DockerImageInspectData> CreateInspectResult(string environmentVariable)
    {
        return ExternalOperationResult.Succeeded<DockerImageInspectData>(new DockerImageInspectData
                                                                         {
                                                                             Id = "sha256:local-image",
                                                                             EnvironmentVariables = [environmentVariable],
                                                                         });
    }

    /// <summary>
    /// Create a successful empty image history answer
    /// </summary>
    /// <returns>History answer</returns>
    private static ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>> CreateHistoryResult()
    {
        return ExternalOperationResult.Succeeded<IReadOnlyList<DockerImageHistoryEntryData>>([]);
    }

    /// <summary>
    /// Create a successful .NET release feed answer that is ahead of the detected runtime
    /// </summary>
    /// <returns>Feed answer</returns>
    private static ExternalOperationResult<DotNetChannelReleaseData> CreateDotNetFeedResult()
    {
        return ExternalOperationResult.Succeeded<DotNetChannelReleaseData>(new DotNetChannelReleaseData
                                                                           {
                                                                               ChannelVersion = "9.0",
                                                                               LatestRuntimeVersion = new Version(9, 0, 15),
                                                                               IsSecurityRelease = true,
                                                                           });
    }

    /// <summary>
    /// Create a successful nginx release feed answer that is ahead of the detected runtime
    /// </summary>
    /// <returns>Feed answer</returns>
    private static ExternalOperationResult<NginxChannelReleaseData> CreateNginxFeedResult()
    {
        return ExternalOperationResult.Succeeded<NginxChannelReleaseData>(new NginxChannelReleaseData
                                                                          {
                                                                              ChannelVersion = "1.29",
                                                                              LatestVersion = new Version(1, 29, 8),
                                                                          });
    }

    /// <summary>
    /// Assert the derived finding of the first scan stayed active on the snapshot of the second scan
    /// </summary>
    /// <param name="outcome">Result of both scans</param>
    /// <param name="reason">Why the runtime check could not be completed</param>
    private static void AssertDerivedFindingKept((Guid FirstFindingId, Guid SecondSnapshotId, IReadOnlyList<UpdateFinding> Findings) outcome, string reason)
    {
        var derivedFindings = outcome.Findings.Where(entity => entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate)
                                              .ToList();

        Assert.HasCount(1,
                        derivedFindings,
                        $"The container must have only its previous derived finding after {reason}");

        var finding = derivedFindings[0];

        Assert.AreEqual(outcome.FirstFindingId,
                        finding.Id,
                        $"The previous derived finding must be kept after {reason}");
        Assert.IsTrue(finding.IsActive,
                      $"The previous derived finding must stay active after {reason}");
        Assert.IsNull(finding.ResolvedAtUtc,
                      $"The previous derived finding must have no resolution time after {reason}");
        Assert.AreEqual(outcome.SecondSnapshotId,
                        finding.ContainerSnapshotId,
                        $"The kept derived finding must move to the snapshot of the second scan after {reason}");
    }

    /// <summary>
    /// Assert the derived finding of the first scan was resolved and no derived finding is active
    /// </summary>
    /// <param name="outcome">Result of both scans</param>
    /// <param name="reason">Why the finding must be resolved</param>
    private static void AssertDerivedFindingResolved((Guid FirstFindingId, Guid SecondSnapshotId, IReadOnlyList<UpdateFinding> Findings) outcome, string reason)
    {
        var derivedFindings = outcome.Findings.Where(entity => entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate)
                                              .ToList();
        var previousFinding = derivedFindings.Single(entity => entity.Id == outcome.FirstFindingId);

        Assert.IsFalse(previousFinding.IsActive, $"The previous derived finding must be inactive after {reason}");
        Assert.IsNotNull(previousFinding.ResolvedAtUtc, $"The previous derived finding must have a resolution time after {reason}");
        Assert.DoesNotContain(entity => entity.IsActive,
                              derivedFindings,
                              $"The container must have no active derived finding after {reason}");
    }

    /// <summary>
    /// Assert the snapshot is assessed as failed with the message of the exception
    /// </summary>
    /// <param name="snapshot">Snapshot of the failed processing</param>
    /// <param name="exception">Exception that failed the processing</param>
    private static void AssertFailedSnapshot(ContainerSnapshot snapshot, Exception exception)
    {
        var reason = FailureReason.FromException(exception, null, CancellationToken.None);

        Assert.AreEqual(UpdateAssessmentStatus.Failed, snapshot.UpdateAssessmentStatus, "The snapshot must be assessed as failed");
        Assert.AreEqual($"Container 'phpmyadmin' could not be processed: {reason}",
                        snapshot.UpdateAssessmentMessage,
                        "The snapshot must carry the failure message with the fixed reason");
    }

    /// <summary>
    /// Assert the first scan stored both the resolved and the available update version
    /// </summary>
    /// <param name="outcome">Outcome of the two scans</param>
    private static void AssertFirstSnapshotHasVersions(AliasScanOutcome outcome)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(outcome.FirstSnapshot.ResolvedVersionTag), "The first scan must store a resolved version");
        Assert.IsFalse(string.IsNullOrWhiteSpace(outcome.FirstSnapshot.AvailableUpdateVersionTag), "The first scan must store an available update version");
    }

    /// <summary>
    /// Assert every finding of the first scan is still active and references the snapshot
    /// </summary>
    /// <param name="outcome">Outcome of the two scans</param>
    /// <param name="snapshot">Snapshot of the second scan</param>
    private static void AssertFindingsKept(AliasScanOutcome outcome, ContainerSnapshot snapshot)
    {
        Assert.IsNotEmpty(outcome.FirstFindings, "The first scan must create findings");

        foreach (var previous in outcome.FirstFindings)
        {
            var finding = outcome.Findings.Single(entity => entity.Id == previous.Id);

            Assert.IsTrue(finding.IsActive, $"The previous {previous.Type} finding must stay active");
            Assert.AreEqual(snapshot.Id,
                            finding.ContainerSnapshotId,
                            $"The previous {previous.Type} finding must reference the snapshot of the second scan");
        }
    }

    /// <summary>
    /// Create the tags of the alias scans: the running digest belongs to a semantic tag, the alias moved on to a newer one
    /// </summary>
    /// <returns>Registry tags</returns>
    private static IReadOnlyList<DockerHubTagData> CreateAliasTags()
    {
        return [
                   new DockerHubTagData
                   {
                       Tag = "5.2.3",
                       Digest = "sha256:old",
                       PublishedAtUtc = new DateTimeOffset(2026, 07, 03, 10, 00, 05, TimeSpan.Zero),
                   },
                   new DockerHubTagData
                   {
                       Tag = "5.3.0",
                       Digest = "sha256:new",
                       PublishedAtUtc = new DateTimeOffset(2026, 07, 04, 12, 00, 05, TimeSpan.Zero),
                   },
               ];
    }

    /// <summary>
    /// Create an image catalog double that forwards to the real repository until the second scan, then throws
    /// </summary>
    /// <param name="repository">Real repository</param>
    /// <param name="exception">Exception the catalog call throws in the second scan</param>
    /// <param name="isSecondScan">Whether the second scan runs</param>
    /// <returns>Catalog double</returns>
    private static IImageCatalogRepository CreateSwitchableCatalog(ImageCatalogRepository repository, Exception? exception, Func<bool> isSecondScan)
    {
        var catalog = Substitute.For<IImageCatalogRepository>();

        catalog.GetOrCreateImageVersionAsync(Arg.Any<string>(),
                                             Arg.Any<string>(),
                                             Arg.Any<string>(),
                                             Arg.Any<string?>(),
                                             Arg.Any<DateTimeOffset?>(),
                                             Arg.Any<string?>(),
                                             Arg.Any<CancellationToken>())
               .Returns(call => isSecondScan() && exception is not null
                                    ? Task.FromException<ImageVersion>(exception)
                                    : repository.GetOrCreateImageVersionAsync(call.ArgAt<string>(0),
                                                                              call.ArgAt<string>(1),
                                                                              call.ArgAt<string>(2),
                                                                              call.ArgAt<string?>(3),
                                                                              call.ArgAt<DateTimeOffset?>(4),
                                                                              call.ArgAt<string?>(5),
                                                                              call.ArgAt<CancellationToken>(6)));
        catalog.FindImageVersionAsync(Arg.Any<string>(),
                                      Arg.Any<string>(),
                                      Arg.Any<string>(),
                                      Arg.Any<string?>(),
                                      Arg.Any<CancellationToken>())
               .Returns(call => repository.FindImageVersionAsync(call.ArgAt<string>(0),
                                                                 call.ArgAt<string>(1),
                                                                 call.ArgAt<string>(2),
                                                                 call.ArgAt<string?>(3),
                                                                 call.ArgAt<CancellationToken>(4)));
        catalog.GetOrCreateRegistryRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(call => repository.GetOrCreateRegistryRepositoryAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<CancellationToken>(2)));
        catalog.AddObservedImageAsync(Arg.Any<ObservedImage>(), Arg.Any<CancellationToken>())
               .Returns(call => repository.AddObservedImageAsync(call.ArgAt<ObservedImage>(0), call.ArgAt<CancellationToken>(1)));
        catalog.SaveChangesAsync(Arg.Any<CancellationToken>())
               .Returns(call => repository.SaveChangesAsync(call.ArgAt<CancellationToken>(0)));

        return catalog;
    }

    /// <summary>
    /// Scan one container on the alias tag latest twice: the first scan stores the resolved and the available update
    /// version, the second scan behaves as given
    /// </summary>
    /// <param name="second">Behavior of the second scan</param>
    /// <returns>State after the two scans</returns>
    private async Task<AliasScanOutcome> RunAliasScansAsync(AliasScanSecondRun second)
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(new DockerUpdateGuardOptions
                                                                                      {
                                                                                          DockerInstances = [
                                                                                                                new DockerInstanceOptions
                                                                                                                {
                                                                                                                    Name = "Production",
                                                                                                                    BaseUrl = "https://docker.example.test",
                                                                                                                    Enabled = true,
                                                                                                                },
                                                                                                            ],
                                                                                      });
                var isSecondScan = false;
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);
                var catalog = CreateSwitchableCatalog(new ImageCatalogRepository(dbContext), second.CatalogException, () => isSecondScan);
                var tags = CreateAliasTags();

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(_ => ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
                                                                                                                                   new RuntimeContainerDescriptor
                                                                                                                                   {
                                                                                                                                       ContainerId = isSecondScan ? second.ContainerId ?? "container-1" : "container-1",
                                                                                                                                       Name = "phpmyadmin",
                                                                                                                                       ImageReference = isSecondScan ? second.ImageReference ?? "docker.io/library/phpmyadmin:latest@sha256:old" : "docker.io/library/phpmyadmin:latest@sha256:old",
                                                                                                                                       RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                       IsRunning = true,
                                                                                                                                   },
                                                                                                                               ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(_ => isSecondScan && second.InspectException is not null
                                                      ? Task.FromException<ExternalOperationResult<DockerImageInspectData>>(second.InspectException)
                                                      : Task.FromResult(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available")));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/phpmyadmin",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(_ => isSecondScan && second.Tags is not null
                                                         ? second.Tags()
                                                         : Task.FromResult(ExternalOperationResult.Succeeded(tags)));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded(new DockerHubTagData
                                                                                  {
                                                                                      Tag = "latest",
                                                                                      Digest = "sha256:new",
                                                                                      PublishedAtUtc = new DateTimeOffset(2026, 07, 04, 12, 00, 00, TimeSpan.Zero),
                                                                                  }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        Substitute.For<IDerivedBaseRuntimeDetector>(),
                                                                        Substitute.For<IDotNetReleaseMetadataService>(),
                                                                        Substitute.For<INginxReleaseMetadataService>(),
                                                                        catalog,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        new TestLogger<RuntimeContainerScanOrchestrator>(),
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        CreateUpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var firstSnapshot = await dbContext.ContainerSnapshots.SingleAsync(TestContext.CancellationToken)
                                                                      .ConfigureAwait(false);
                var firstFindings = await dbContext.UpdateFindings.ToListAsync(TestContext.CancellationToken)
                                                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();
                isSecondScan = true;

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var secondSnapshots = await dbContext.ContainerSnapshots.Where(entity => entity.Id != firstSnapshot.Id)
                                                                        .ToListAsync(TestContext.CancellationToken)
                                                                        .ConfigureAwait(false);
                var findings = await dbContext.UpdateFindings.ToListAsync(TestContext.CancellationToken)
                                                             .ConfigureAwait(false);
                var secondRun = await dbContext.ScanRuns.SingleAsync(entity => entity.Id != firstSnapshot.ScanRunId, TestContext.CancellationToken)
                                                        .ConfigureAwait(false);

                return new AliasScanOutcome
                       {
                           FirstSnapshot = firstSnapshot,
                           SecondSnapshots = secondSnapshots,
                           FirstFindings = firstFindings,
                           Findings = findings,
                           SecondRunStatus = secondRun.Status,
                       };
            }
        }
    }

    /// <summary>
    /// Scan one container twice with the real derived runtime detector: the first scan reads every input and
    /// finds an outdated runtime, the second scan answers with the given inputs
    /// </summary>
    /// <param name="environmentVariable">Environment variable the detector reads the runtime from</param>
    /// <param name="secondInspect">Image inspect answer of the second scan</param>
    /// <param name="secondHistory">Image history answer of the second scan</param>
    /// <param name="secondDotNet">.NET release feed answer of the second scan</param>
    /// <param name="secondNginx">Nginx release feed answer of the second scan</param>
    /// <param name="registryTags">Registry tags both scans read; the current tag only when omitted</param>
    /// <returns>Id of the first scan's derived finding, id of the second scan's snapshot and all findings</returns>
    private async Task<(Guid FirstFindingId, Guid SecondSnapshotId, IReadOnlyList<UpdateFinding> Findings)> RunTwoDerivedScansAsync(string environmentVariable,
                                                                                                                                    ExternalOperationResult<DockerImageInspectData> secondInspect,
                                                                                                                                    ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>> secondHistory,
                                                                                                                                    ExternalOperationResult<DotNetChannelReleaseData> secondDotNet,
                                                                                                                                    ExternalOperationResult<NginxChannelReleaseData> secondNginx,
                                                                                                                                    IReadOnlyList<DockerHubTagData>? registryTags = null)
    {
        var outcome = await RunTwoScansAsync(environmentVariable,
                                             secondInspect,
                                             _ => Task.FromResult(secondHistory),
                                             secondDotNet,
                                             secondNginx,
                                             registryTags,
                                             null).ConfigureAwait(false);

        return (outcome.FirstDerivedFindingId, outcome.SecondSnapshotId, outcome.Findings);
    }

    /// <summary>
    /// Scan one container twice with the real derived runtime detector: the first scan reads every input and
    /// finds an outdated runtime, the second scan answers with the given inputs
    /// </summary>
    /// <param name="environmentVariable">Environment variable the detector reads the runtime from</param>
    /// <param name="secondInspect">Image inspect answer of the second scan</param>
    /// <param name="secondHistory">Image history call of the second scan</param>
    /// <param name="secondDotNet">.NET release feed answer of the second scan</param>
    /// <param name="secondNginx">Nginx release feed answer of the second scan</param>
    /// <param name="registryTags">Registry tags both scans read; the current tag only when omitted</param>
    /// <param name="secondTags">Tag lookup call of the second scan; the same tags as the first scan when omitted</param>
    /// <returns>State of the first scan and the outcome of the second</returns>
    private async Task<TwoScanOutcome> RunTwoScansAsync(string environmentVariable,
                                                        ExternalOperationResult<DockerImageInspectData> secondInspect,
                                                        Func<NSubstitute.Core.CallInfo, Task<ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>>> secondHistory,
                                                        ExternalOperationResult<DotNetChannelReleaseData> secondDotNet,
                                                        ExternalOperationResult<NginxChannelReleaseData> secondNginx,
                                                        IReadOnlyList<DockerHubTagData>? registryTags,
                                                        Func<NSubstitute.Core.CallInfo, Task<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>>>? secondTags)
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(new DockerUpdateGuardOptions
                                                                                      {
                                                                                          DockerInstances = [
                                                                                                                new DockerInstanceOptions
                                                                                                                {
                                                                                                                    Name = "Production",
                                                                                                                    BaseUrl = "https://docker.example.test",
                                                                                                                    Enabled = true,
                                                                                                                },
                                                                                                            ],
                                                                                      });
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);
                var currentTag = new DockerHubTagData
                                 {
                                     Tag = "1.0.0",
                                     Digest = "sha256:current",
                                     PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                 };

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "api",
                                                                                                                                  ImageReference = "docker.io/company/api:1.0.0",
                                                                                                                                  LocalImageId = "sha256:local-image",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), "sha256:local-image", Arg.Any<CancellationToken>())
                                    .Returns(CreateInspectResult(environmentVariable), secondInspect);
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), "sha256:local-image", Arg.Any<CancellationToken>())
                                    .Returns(_ => Task.FromResult(CreateHistoryResult()), secondHistory);
                dotNetReleaseMetadataService.GetChannelReleaseAsync("9.0", Arg.Any<CancellationToken>())
                                            .Returns(CreateDotNetFeedResult(), secondDotNet);
                nginxReleaseMetadataService.GetChannelReleaseAsync("1.29", Arg.Any<CancellationToken>())
                                           .Returns(CreateNginxFeedResult(), secondNginx);
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "company/api",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(_ => Task.FromResult(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>(registryTags ?? [currentTag])),
                                                secondTags ?? (_ => Task.FromResult(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>(registryTags ?? [currentTag]))));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(currentTag));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        new DerivedBaseRuntimeDetector(),
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        new TestLogger<RuntimeContainerScanOrchestrator>(),
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        CreateUpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var firstSnapshot = await dbContext.ContainerSnapshots.SingleAsync(TestContext.CancellationToken)
                                                                      .ConfigureAwait(false);
                var firstFindings = await dbContext.UpdateFindings.ToListAsync(TestContext.CancellationToken)
                                                                  .ConfigureAwait(false);
                var firstCandidateCounts = await dbContext.TagCandidates.GroupBy(entity => entity.UpdateFindingId)
                                                                        .ToDictionaryAsync(group => group.Key, group => group.Count(), TestContext.CancellationToken)
                                                                        .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var secondSnapshot = await dbContext.ContainerSnapshots.SingleAsync(entity => entity.Id != firstSnapshot.Id, TestContext.CancellationToken)
                                                                       .ConfigureAwait(false);
                var findings = await dbContext.UpdateFindings.ToListAsync(TestContext.CancellationToken)
                                                             .ConfigureAwait(false);
                var candidateCounts = await dbContext.TagCandidates.GroupBy(entity => entity.UpdateFindingId)
                                                                   .ToDictionaryAsync(group => group.Key, group => group.Count(), TestContext.CancellationToken)
                                                                   .ConfigureAwait(false);

                return new TwoScanOutcome
                       {
                           FirstSnapshotId = firstSnapshot.Id,
                           SecondSnapshotId = secondSnapshot.Id,
                           SecondAssessmentStatus = secondSnapshot.UpdateAssessmentStatus,
                           FirstFindings = firstFindings,
                           FirstCandidateCounts = firstCandidateCounts,
                           Findings = findings,
                           CandidateCounts = candidateCounts,
                       };
            }
        }
    }

    #endregion // Methods
}