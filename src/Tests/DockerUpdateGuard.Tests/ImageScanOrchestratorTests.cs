using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Data;

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
    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

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
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();

                dbContext.ObservedImages.Add(observedImage);
                await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                               .ConfigureAwait(false);
                var logger = new TestLogger<ImageScanOrchestrator>();

                baseImageResolver.ResolveAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                 .Returns(ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Succeeded([
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
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:app",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/debian",
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
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
                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<RegistryImageConfigurationData>.NotFound("No registry config"));

                var orchestrator = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                             baseImageResolver,
                                                             dbContext,
                                                             derivedBaseRuntimeDetector,
                                                             dotNetReleaseMetadataService,
                                                             nginxReleaseMetadataService,
                                                             imageCatalogRepository,
                                                             imageReferenceParser,
                                                             logger,
                                                             registryMetadataService,
                                                             new UpdateDetectionService());

                await orchestrator.ScanAsync(observedImage.Id,
                                             ScanTriggerSource.Manual,
                                             CancellationToken.None)
                                  .ConfigureAwait(false);

                var scanRun = await dbContext.ScanRuns.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var relationship = await dbContext.ImageRelationships.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var finding = await dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                            .SingleAsync(TestContext.CancellationToken)
                                                            .ConfigureAwait(false);
                Assert.IsNotNull(finding.RecommendedImageVersionId, "Observed image findings with an update must persist the recommended image version");

                var recommendedImageTask = dbContext.ImageVersions.SingleAsync(entity => entity.Id == finding.RecommendedImageVersionId.Value, TestContext.CancellationToken);
                var recommendedImage = await recommendedImageTask.ConfigureAwait(false);
                var refreshedCurrentVersionTask = dbContext.ImageVersions.SingleAsync(entity => entity.Id == currentImageVersion.Id, TestContext.CancellationToken);
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
                var containerSnapshotCount = await dbContext.ContainerSnapshots.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(0,
                                containerSnapshotCount,
                                "Observed image scans must not create runtime container snapshots");
                Assert.IsFalse(string.IsNullOrWhiteSpace(refreshedCurrentVersion.MetadataJson), "Observed image scans must refresh the current image metadata payload");
                Assert.Contains(entry => entry.EventId.Id == 2063
                                         && entry.Message.Contains("Company App", StringComparison.Ordinal),
                                logger.Entries,
                                "Observed image scans must log when an image scan starts");
                Assert.Contains(entry => entry.EventId.Id == 2031
                                         && entry.Message.Contains("Succeeded", StringComparison.Ordinal),
                                logger.Entries,
                                "Observed image scans must log the final summary outcome");
            }
        }
    }

    /// <summary>
    /// Verify observed image scans persist update candidates even when the registry omits candidate digests
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncPersistsCandidatesWithoutDigestAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var currentImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                    "company/app",
                                                                                                    "1.0.0",
                                                                                                    "sha256:app",
                                                                                                    cancellationToken: CancellationToken.None)
                                                                      .ConfigureAwait(false);
                var observedImage = new ObservedImage
                                    {
                                        Name = "Company App",
                                        CurrentImageVersionId = currentImageVersion.Id,
                                    };
                var baseImageResolver = Substitute.For<IBaseImageResolver>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();

                dbContext.ObservedImages.Add(observedImage);

                await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                               .ConfigureAwait(false);

                baseImageResolver.ResolveAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                 .Returns(ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Succeeded([
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

                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:app",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/debian",
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "12.1.0",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "12.0.0",
                                                                                                                           Digest = "sha256:base-old",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));

                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<RegistryImageConfigurationData>.NotFound("No registry config"));

                var orchestrator = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                             baseImageResolver,
                                                             dbContext,
                                                             derivedBaseRuntimeDetector,
                                                             dotNetReleaseMetadataService,
                                                             nginxReleaseMetadataService,
                                                             imageCatalogRepository,
                                                             new ImageReferenceParser(),
                                                             new TestLogger<ImageScanOrchestrator>(),
                                                             registryMetadataService,
                                                             new UpdateDetectionService());

                await orchestrator.ScanAsync(observedImage.Id,
                                             ScanTriggerSource.Manual,
                                             CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var scanRun = await dbContext.ScanRuns.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var finding = await dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                            .SingleAsync(TestContext.CancellationToken)
                                                            .ConfigureAwait(false);
                var candidate = finding.TagCandidates.Single();

                Assert.AreEqual(ScanRunStatus.Succeeded,
                                scanRun.Status,
                                "Observed image scans must still complete when a registry candidate has no digest");
                Assert.AreEqual("12.1.0",
                                candidate.Tag,
                                "The candidate tag must still be persisted when its digest is missing");
                Assert.IsNull(candidate.Digest,
                              "Persisted candidates without a digest must materialize back as null");
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
                                                             Substitute.For<IDerivedBaseRuntimeDetector>(),
                                                             Substitute.For<IDotNetReleaseMetadataService>(),
                                                             Substitute.For<INginxReleaseMetadataService>(),
                                                             new ImageCatalogRepository(dbContext),
                                                             new ImageReferenceParser(),
                                                             logger,
                                                             Substitute.For<IRegistryMetadataService>(),
                                                             new UpdateDetectionService());

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                Assert.Contains(entry => entry.EventId.Id == 2061, logger.Entries, "Observed image batch scans must log when they are skipped because no images are enabled");
            }
        }
    }

    /// <summary>
    /// Verify observed image scans create derived .NET runtime findings from registry configuration metadata
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncCreatesDerivedBaseRuntimeFindingWithoutRuntimeContainerAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var imageReferenceParser = new ImageReferenceParser();
                var currentImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("ghcr.io",
                                                                                                    "acme/api",
                                                                                                    "1.0.0",
                                                                                                    "sha256:app",
                                                                                                    cancellationToken: CancellationToken.None)
                                                                      .ConfigureAwait(false);
                var observedImage = new ObservedImage
                                    {
                                        Name = "Acme API",
                                        Source = RegistrationSource.Discovery,
                                        CurrentImageVersionId = currentImageVersion.Id,
                                    };
                var baseImageResolver = Substitute.For<IBaseImageResolver>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();

                dbContext.ObservedImages.Add(observedImage);
                await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                               .ConfigureAwait(false);

                baseImageResolver.ResolveAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                 .Returns(ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Succeeded([]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:app",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));
                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<RegistryImageConfigurationData>.Succeeded(new RegistryImageConfigurationData
                                                                                                                  {
                                                                                                                      EnvironmentVariables = [
                                                                                                                                                 "DOTNET_VERSION=9.0.13",
                                                                                                                                             ],
                                                                                                                  }));
                derivedBaseRuntimeDetector.Detect(Arg.Any<DockerImageInspectData>(), Arg.Any<IReadOnlyList<DockerImageHistoryEntryData>>())
                                          .Returns(new DerivedBaseRuntimeDescriptor
                                                   {
                                                       Kind = DerivedBaseRuntimeKind.DotNet,
                                                       RuntimeVersion = new Version(9, 0, 13),
                                                       ChannelVersion = "9.0",
                                                   });
                dotNetReleaseMetadataService.GetChannelReleaseAsync("9.0", Arg.Any<CancellationToken>())
                                            .Returns(ExternalOperationResult<DotNetChannelReleaseData>.Succeeded(new DotNetChannelReleaseData
                                                                                                                 {
                                                                                                                     ChannelVersion = "9.0",
                                                                                                                     LatestRuntimeVersion = new Version(9, 0, 15),
                                                                                                                     IsSecurityRelease = true,
                                                                                                                 }));

                var orchestrator = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                             baseImageResolver,
                                                             dbContext,
                                                             derivedBaseRuntimeDetector,
                                                             dotNetReleaseMetadataService,
                                                             nginxReleaseMetadataService,
                                                             imageCatalogRepository,
                                                             imageReferenceParser,
                                                             NullLogger<ImageScanOrchestrator>.Instance,
                                                             registryMetadataService,
                                                             new UpdateDetectionService());

                await orchestrator.ScanAsync(observedImage.Id,
                                             ScanTriggerSource.Manual,
                                             CancellationToken.None)
                                  .ConfigureAwait(false);

                var derivedFinding = await dbContext.UpdateFindings.SingleAsync(entity => entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate, TestContext.CancellationToken)
                                                                   .ConfigureAwait(false);

                Assert.AreEqual(observedImage.Id,
                                derivedFinding.ObservedImageId,
                                "Derived base-runtime findings must be attached directly to the observed image");
                Assert.IsNull(derivedFinding.ContainerSnapshotId,
                              "Derived base-runtime findings created during observed image scans must not depend on runtime containers");
                Assert.AreEqual("Own image uses an outdated .NET base runtime",
                                derivedFinding.Summary,
                                "Own observed images must produce the stronger .NET base-runtime warning text");
                Assert.IsNotNull(derivedFinding.Details, "The derived finding details must be persisted");
                Assert.IsTrue(derivedFinding.Details.Contains(".NET 9.0.13", StringComparison.Ordinal),
                              "The derived finding details must describe the detected .NET version");
                Assert.IsTrue(derivedFinding.Details.Contains("9.0.15", StringComparison.Ordinal),
                              "The derived finding details must describe the latest channel runtime version");
            }
        }
    }

    /// <summary>
    /// Verify observed image scans create derived NGINX runtime findings from registry configuration metadata
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncCreatesDerivedNginxBaseRuntimeFindingWithoutRuntimeContainerAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var imageReferenceParser = new ImageReferenceParser();
                var currentImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("ghcr.io",
                                                                                                    "acme/web",
                                                                                                    "1.0.0",
                                                                                                    "sha256:web",
                                                                                                    cancellationToken: CancellationToken.None)
                                                                      .ConfigureAwait(false);
                var observedImage = new ObservedImage
                                    {
                                        Name = "Acme Web",
                                        Source = RegistrationSource.Discovery,
                                        CurrentImageVersionId = currentImageVersion.Id,
                                    };
                var baseImageResolver = Substitute.For<IBaseImageResolver>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();

                dbContext.ObservedImages.Add(observedImage);
                await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                               .ConfigureAwait(false);

                baseImageResolver.ResolveAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                 .Returns(ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Succeeded([]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:web",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));
                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<RegistryImageConfigurationData>.Succeeded(new RegistryImageConfigurationData
                                                                                                                  {
                                                                                                                      EnvironmentVariables = [
                                                                                                                                                 "NGINX_VERSION=1.29.1",
                                                                                                                                             ],
                                                                                                                  }));
                derivedBaseRuntimeDetector.Detect(Arg.Any<DockerImageInspectData>(), Arg.Any<IReadOnlyList<DockerImageHistoryEntryData>>())
                                          .Returns(new DerivedBaseRuntimeDescriptor
                                                   {
                                                       Kind = DerivedBaseRuntimeKind.Nginx,
                                                       RuntimeVersion = new Version(1, 29, 1),
                                                       ChannelVersion = "1.29",
                                                   });
                nginxReleaseMetadataService.GetChannelReleaseAsync("1.29", Arg.Any<CancellationToken>())
                                           .Returns(ExternalOperationResult<NginxChannelReleaseData>.Succeeded(new NginxChannelReleaseData
                                                                                                               {
                                                                                                                   ChannelVersion = "1.29",
                                                                                                                   LatestVersion = new Version(1, 29, 8),
                                                                                                               }));

                var orchestrator = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                             baseImageResolver,
                                                             dbContext,
                                                             derivedBaseRuntimeDetector,
                                                             dotNetReleaseMetadataService,
                                                             nginxReleaseMetadataService,
                                                             imageCatalogRepository,
                                                             imageReferenceParser,
                                                             NullLogger<ImageScanOrchestrator>.Instance,
                                                             registryMetadataService,
                                                             new UpdateDetectionService());

                await orchestrator.ScanAsync(observedImage.Id,
                                             ScanTriggerSource.Manual,
                                             CancellationToken.None)
                                  .ConfigureAwait(false);

                var derivedFinding = await dbContext.UpdateFindings.SingleAsync(entity => entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate, TestContext.CancellationToken)
                                                                   .ConfigureAwait(false);

                Assert.AreEqual("Own image uses an outdated NGINX base runtime",
                                derivedFinding.Summary,
                                "Own observed images must produce an NGINX base-runtime warning when a newer patch exists");
                Assert.IsNotNull(derivedFinding.Details, "The derived finding details must be persisted");
                Assert.IsTrue(derivedFinding.Details.Contains("NGINX 1.29.1", StringComparison.Ordinal),
                              "The derived finding details must describe the detected NGINX version");
                Assert.IsTrue(derivedFinding.Details.Contains("1.29.8", StringComparison.Ordinal),
                              "The derived finding details must describe the latest NGINX channel version");
            }
        }
    }

    #endregion // Methods
}