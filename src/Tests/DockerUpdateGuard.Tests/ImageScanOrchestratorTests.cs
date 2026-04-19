using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for the observed image scan orchestration path
/// </summary>
[TestClass]
public class ImageScanOrchestratorTests
{
    #region Methods

    /// <summary>
    /// Verify observed image scans create base-image findings without runtime snapshots
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncCreatesObservedImageBaseFindingAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var imageReferenceParser = new ImageReferenceParser();
                var currentImageVersionTask = imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                  "company/app",
                                                                                                  "1.0.0",
                                                                                                  "sha256:app",
                                                                                                  cancellationToken: CancellationToken.None);
                var currentImageVersion = await currentImageVersionTask.ConfigureAwait(false);
                var observedImage = new ObservedImage
                                    {
                                        Name = "Company App",
                                        CurrentImageVersionId = currentImageVersion.Id,
                                    };
                var baseImageResolver = Substitute.For<IBaseImageResolver>();
                var dockerHubClient = Substitute.For<IDockerHubClient>();

                dbContext.ObservedImages.Add(observedImage);
                await dbContext.SaveChangesAsync()
                               .ConfigureAwait(false);
                var logger = new TestLogger<ImageScanOrchestrator>();

                baseImageResolver.ResolveAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                 .Returns(ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Succeeded(
                                 [
                                     new BaseImageDescriptor
                                     {
                                         Registry = "docker.io",
                                         Repository = "library/debian",
                                         Tag = "12.0.0",
                                         Digest = "sha256:base-old",
                                         Depth = 1,
                                         SourceReference = "FROM debian:12.0.0",
                                     },
                                 ]));
                dockerHubClient.GetTagAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                               .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                            {
                                                                                                Tag = "1.0.0",
                                                                                                Digest = "sha256:app",
                                                                                                PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                            }));
                dockerHubClient.GetTagsAsync("docker.io",
                                             "library/debian",
                                             Arg.Any<CancellationToken>())
                               .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded(
                               [
                                   new DockerHubTagData
                                   {
                                       Tag = "12.1.0",
                                       Digest = "sha256:base-new",
                                       PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                   },
                                   new DockerHubTagData
                                   {
                                       Tag = "12.0.0",
                                       Digest = "sha256:base-old",
                                       PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                   },
                               ]));

                var orchestrator = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                             baseImageResolver,
                                                             dbContext,
                                                             dockerHubClient,
                                                             imageCatalogRepository,
                                                             imageReferenceParser,
                                                             logger,
                                                             new UpdateDetectionService());

                await orchestrator.ScanAsync(observedImage.Id,
                                             ScanTriggerSource.Manual,
                                             CancellationToken.None)
                                  .ConfigureAwait(false);

                var scanRun = await dbContext.ScanRuns.SingleAsync().ConfigureAwait(false);
                var relationship = await dbContext.ImageRelationships.SingleAsync().ConfigureAwait(false);
                var finding = await dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                            .SingleAsync()
                                                            .ConfigureAwait(false);
                Assert.IsNotNull(finding.RecommendedImageVersionId, "Observed image findings with an update must persist the recommended image version");

                var recommendedImageTask = dbContext.ImageVersions.SingleAsync(entity => entity.Id == finding.RecommendedImageVersionId.Value);
                var recommendedImage = await recommendedImageTask.ConfigureAwait(false);
                var refreshedCurrentVersionTask = dbContext.ImageVersions.SingleAsync(entity => entity.Id == currentImageVersion.Id);
                var refreshedCurrentVersion = await refreshedCurrentVersionTask.ConfigureAwait(false);

                Assert.AreEqual(ScanRunType.ObservedImage,
                                scanRun.Type,
                                "Observed image scans must persist an observed-image scan run");
                Assert.AreEqual(ScanRunStatus.Succeeded,
                                scanRun.Status,
                                "The observed image scan must succeed when metadata and base image resolution succeed");
                Assert.AreEqual(observedImage.Id,
                                finding.ObservedImageId,
                                "Observed image findings must stay attached to the observed image path");
                Assert.IsNull(finding.ContainerSnapshotId, "Observed image findings must not point to runtime container snapshots");
                Assert.AreEqual(UpdateFindingType.BaseImageUpdate,
                                finding.Type,
                                "Observed image update findings must be classified as base-image updates");
                Assert.AreEqual(currentImageVersion.Id,
                                relationship.ChildImageVersionId,
                                "The base-image relationship must point back to the observed image version");
                Assert.AreEqual("12.1.0",
                                recommendedImage.Tag,
                                "The observed image finding must recommend the newer base image tag");
                Assert.HasCount(1,
                                finding.TagCandidates,
                                "The observed image finding must persist the evaluated tag candidates");
                var containerSnapshotCount = await dbContext.ContainerSnapshots.CountAsync().ConfigureAwait(false);

                Assert.AreEqual(0,
                                containerSnapshotCount,
                                "Observed image scans must not create runtime container snapshots");
                Assert.IsFalse(string.IsNullOrWhiteSpace(refreshedCurrentVersion.MetadataJson), "Observed image scans must refresh the current image metadata payload");
                Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 2063
                                                          && entry.Message.Contains("Company App", StringComparison.Ordinal)),
                              "Observed image scans must log when an image scan starts");
                Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 2031
                                                          && entry.Message.Contains("Succeeded", StringComparison.Ordinal)),
                              "Observed image scans must log the final summary outcome");
            }
        }
    }

    /// <summary>
    /// Verify batch scans log a skip when no enabled observed images are registered
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAllAsyncWithoutEnabledImagesLogsSkippedBatchAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var logger = new TestLogger<ImageScanOrchestrator>();
                var orchestrator = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                             Substitute.For<IBaseImageResolver>(),
                                                             dbContext,
                                                             Substitute.For<IDockerHubClient>(),
                                                             new ImageCatalogRepository(dbContext),
                                                             new ImageReferenceParser(),
                                                             logger,
                                                             new UpdateDetectionService());

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 2061), "Observed image batch scans must log when they are skipped because no images are enabled");
            }
        }
    }

    #endregion // Methods
}