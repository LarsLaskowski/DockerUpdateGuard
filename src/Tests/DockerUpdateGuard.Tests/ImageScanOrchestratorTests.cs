using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Enums;
using DockerUpdateGuard.Images.Interfaces;
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
    /// Verify observed image scans persist exact base-image metadata without enumerating repository tags
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncPersistsResolvedBaseImageWithoutEnumeratingRepositoryTagsAsync()
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
                                       .Returns(callInfo =>
                                                {
                                                    var imageReference = callInfo.ArgAt<ImageReference>(0);

                                                    return imageReference.Repository switch
                                                           {
                                                               "company/app" => ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                                                    {
                                                                                                                                        Tag = "1.0.0",
                                                                                                                                        Digest = "sha256:app",
                                                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                                    }),
                                                               "library/debian" => ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                                                       {
                                                                                                                                           Tag = "12.0.0",
                                                                                                                                           Digest = "sha256:base-old",
                                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                                       }),
                                                               _ => ExternalOperationResult<DockerHubTagData>.NotFound($"No tag metadata is available for '{imageReference.FullReference}'"),
                                                           };
                                                });

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
                                                             registryMetadataService);

                await orchestrator.ScanAsync(observedImage.Id,
                                             ScanTriggerSource.Manual,
                                             CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var scanRun = await dbContext.ScanRuns.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var relationship = await dbContext.ImageRelationships.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var refreshedCurrentVersionTask = dbContext.ImageVersions.SingleAsync(entity => entity.Id == currentImageVersion.Id, TestContext.CancellationToken);
                var refreshedCurrentVersion = await refreshedCurrentVersionTask.ConfigureAwait(false);
                var baseImageVersionTask = dbContext.ImageVersions.SingleAsync(entity => entity.Id == relationship.BaseImageVersionId, TestContext.CancellationToken);
                var baseImageVersion = await baseImageVersionTask.ConfigureAwait(false);
                var findingCountTask = dbContext.UpdateFindings.CountAsync(TestContext.CancellationToken);
                var findingCount = await findingCountTask.ConfigureAwait(false);

                Assert.AreEqual(ScanRunType.ObservedImage,
                                scanRun.Type,
                                "Observed image scans must persist an observed-image scan run");
                Assert.AreEqual(ScanRunStatus.Succeeded,
                                scanRun.Status,
                                "The observed image scan must succeed when metadata and base image resolution succeed");
                Assert.AreEqual(currentImageVersion.Id,
                                relationship.ChildImageVersionId,
                                "The base-image relationship must point back to the observed image version");
                Assert.AreEqual("12.0.0",
                                baseImageVersion.Tag,
                                "Observed image scans must persist the exact resolved base image tag");
                Assert.AreEqual("sha256:base-old",
                                baseImageVersion.Digest,
                                "Observed image scans must persist the resolved base image digest");
                Assert.AreEqual(new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                baseImageVersion.PublishedAtUtc,
                                "Observed image scans must persist the exact base image publication timestamp");
                Assert.AreEqual(0,
                                findingCount,
                                "Observed image scans must not create base-image update findings when only the exact base image version is relevant");

                var containerSnapshotCount = await dbContext.ContainerSnapshots.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(0,
                                containerSnapshotCount,
                                "Observed image scans must not create runtime container snapshots");
                Assert.IsFalse(string.IsNullOrWhiteSpace(refreshedCurrentVersion.MetadataJson), "Observed image scans must refresh the current image metadata payload");
                Assert.IsFalse(string.IsNullOrWhiteSpace(baseImageVersion.MetadataJson), "Observed image scans must persist exact base image metadata");
                Assert.Contains(entry => entry.EventId.Id == 2063
                                         && entry.Message.Contains("Company App", StringComparison.Ordinal),
                                logger.Entries,
                                "Observed image scans must log when an image scan starts");
                Assert.Contains(entry => entry.EventId.Id == 2031
                                         && entry.Message.Contains("Succeeded", StringComparison.Ordinal),
                                logger.Entries,
                                "Observed image scans must log the final summary outcome");

                _ = registryMetadataService.Received(1)
                                           .GetTagAsync(Arg.Is<ImageReference>(entity => entity.Registry == "docker.io"
                                                                                         && entity.Repository == "library/debian"
                                                                                         && entity.Tag == "12.0.0"
                                                                                         && entity.Digest == "sha256:base-old"),
                                                        Arg.Any<CancellationToken>());

                _ = registryMetadataService.DidNotReceive()
                                           .GetTagsAsync("docker.io",
                                                         "library/debian",
                                                         Arg.Any<CancellationToken>(),
                                                         Arg.Any<string?>(),
                                                         Arg.Any<string?>());
            }
        }
    }

    /// <summary>
    /// Verify observed image scans resolve MCR base-image aliases to exact tags in the same variant family
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncResolvesMcrBaseImageAliasToExactVariantTagAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var imageReferenceParser = new ImageReferenceParser();
                var currentImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("mcr.microsoft.com",
                                                                                                    "company/app",
                                                                                                    "1.0.0-alpine",
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
                                                                                                                        Registry = "mcr.microsoft.com",
                                                                                                                        Repository = "dotnet/runtime",
                                                                                                                        Tag = "10.0-alpine",
                                                                                                                        Digest = "sha256:base-current",
                                                                                                                        Depth = 1,
                                                                                                                        SourceReference = "FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine",
                                                                                                                    },
                                                                                                                ]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(callInfo =>
                                                {
                                                    var imageReference = callInfo.ArgAt<ImageReference>(0);

                                                    return imageReference.Repository switch
                                                           {
                                                               "company/app" => ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                                                    {
                                                                                                                                        Tag = "1.0.0-alpine",
                                                                                                                                        Digest = "sha256:app",
                                                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                                    }),
                                                               "dotnet/runtime" => ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                                                       {
                                                                                                                                           Tag = "10.0-alpine",
                                                                                                                                           Digest = "sha256:base-current",
                                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                                       }),
                                                               _ => ExternalOperationResult<DockerHubTagData>.NotFound($"No tag metadata is available for '{imageReference.FullReference}'"),
                                                           };
                                                });
                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<RegistryImageConfigurationData>.NotFound("No registry config"));
                registryMetadataService.GetTagsAsync("mcr.microsoft.com",
                                                     "dotnet/runtime",
                                                     Arg.Any<CancellationToken>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Is<RegistryTagQueryOptions>(options => options.CurrentDigest == "sha256:base-current"
                                                                                                && options.CurrentTag == "10.0-alpine"
                                                                                                && options.MaximumTags == 150
                                                                                                && options.VersionLineTag == "10.0-alpine"
                                                                                                && options.PublishedSinceUtc == new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero)))
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "10.0-alpine",
                                                                                                                           Digest = "sha256:base-current",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "10.0.7-alpine3.23",
                                                                                                                           Digest = "sha256:base-current",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "10.0.8-alpine3.24",
                                                                                                                           Digest = "sha256:base-next",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "10.0.7-jammy",
                                                                                                                           Digest = "sha256:base-current",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));

                var orchestrator = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                             baseImageResolver,
                                                             dbContext,
                                                             derivedBaseRuntimeDetector,
                                                             dotNetReleaseMetadataService,
                                                             nginxReleaseMetadataService,
                                                             imageCatalogRepository,
                                                             imageReferenceParser,
                                                             new TestLogger<ImageScanOrchestrator>(),
                                                             registryMetadataService);

                await orchestrator.ScanAsync(observedImage.Id,
                                             ScanTriggerSource.Manual,
                                             CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var relationship = await dbContext.ImageRelationships.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var baseImageVersion = await dbContext.ImageVersions.SingleAsync(entity => entity.Id == relationship.BaseImageVersionId, TestContext.CancellationToken)
                                                                    .ConfigureAwait(false);

                Assert.AreEqual("10.0.7-alpine3.23",
                                baseImageVersion.Tag,
                                "Observed image scans must persist the exact MCR base-image tag resolved from the manifest digest");
                _ = registryMetadataService.Received(1)
                                           .GetTagsAsync("mcr.microsoft.com",
                                                         "dotnet/runtime",
                                                         Arg.Any<CancellationToken>(),
                                                         Arg.Any<string?>(),
                                                         Arg.Any<string?>(),
                                                         Arg.Any<RegistryTagQueryOptions?>());
            }
        }
    }

    /// <summary>
    /// Verify observed image scans mark the run partial when exact base-image metadata cannot be refreshed
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncMarksScanPartialWhenExactBaseImageMetadataCannotBeReadAsync()
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
                                       .Returns(callInfo =>
                                                {
                                                    var imageReference = callInfo.ArgAt<ImageReference>(0);

                                                    return imageReference.Repository switch
                                                           {
                                                               "company/app" => ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                                                    {
                                                                                                                                        Tag = "1.0.0",
                                                                                                                                        Digest = "sha256:app",
                                                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                                    }),
                                                               "library/debian" => ExternalOperationResult<DockerHubTagData>.Failed("The exact base image metadata is unavailable"),
                                                               _ => ExternalOperationResult<DockerHubTagData>.NotFound($"No tag metadata is available for '{imageReference.FullReference}'"),
                                                           };
                                                });

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
                                                             registryMetadataService);

                await orchestrator.ScanAsync(observedImage.Id,
                                             ScanTriggerSource.Manual,
                                             CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var scanRun = await dbContext.ScanRuns.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var relationshipCount = await dbContext.ImageRelationships.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var findingCount = await dbContext.UpdateFindings.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(ScanRunStatus.Partial,
                                scanRun.Status,
                                "Observed image scans must become partial when the exact base image metadata cannot be refreshed");
                Assert.AreEqual(1,
                                relationshipCount,
                                "Observed image scans must still persist the resolved base-image relationship when the exact metadata refresh fails");
                Assert.AreEqual(0,
                                findingCount,
                                "Observed image scans must not create base-image update findings when exact base image metadata refresh fails");

                _ = registryMetadataService.DidNotReceive()
                                           .GetTagsAsync("docker.io",
                                                         "library/debian",
                                                         Arg.Any<CancellationToken>(),
                                                         Arg.Any<string?>(),
                                                         Arg.Any<string?>());
            }
        }
    }

    /// <summary>
    /// Verify observed image scans remain Succeeded when base image resolution returns no results
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncRemainsSucceededWhenNoBaseImageIsFoundAsync()
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
                                 .Returns(ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Succeeded([]));

                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:app",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

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
                                                             NullLogger<ImageScanOrchestrator>.Instance,
                                                             registryMetadataService);

                await orchestrator.ScanAsync(observedImage.Id,
                                             ScanTriggerSource.Manual,
                                             CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var scanRun = await dbContext.ScanRuns.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(ScanRunStatus.Succeeded,
                                scanRun.Status,
                                "Observed image scans must remain Succeeded when base image resolution returns no results");
                Assert.IsNull(scanRun.ErrorMessage,
                              "Observed image scans must not record an error message when no base image is found");
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
                                                             Substitute.For<IRegistryMetadataService>());

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                Assert.Contains(entry => entry.EventId.Id == 2061, logger.Entries, "Observed image batch scans must log when they are skipped because no images are enabled");
            }
        }
    }

    /// <summary>
    /// Verify a scan for an observed image removed after listing is skipped without throwing
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncWithMissingObservedImageSkipsWithoutThrowingAsync()
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
                                                             Substitute.For<IRegistryMetadataService>());

                await orchestrator.ScanAsync(Guid.NewGuid(), ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var scanRunCount = await dbContext.ScanRuns.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.Contains(entry => entry.EventId.Id == 2032, logger.Entries, "A scan for an observed image removed after listing must log that it was skipped");
                Assert.AreEqual(0, scanRunCount, "A scan for a missing observed image must not persist a scan run");
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
                                                             registryMetadataService);

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
                                                             registryMetadataService);

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

    /// <summary>
    /// Verify a transient configuration refresh failure does not delete existing derived findings
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncPreservesDerivedFindingWhenConfigurationRefreshFailsAsync()
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
                                                                                                                  }),
                                                ExternalOperationResult<RegistryImageConfigurationData>.Failed("The registry configuration is temporarily unavailable"));
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
                                                             registryMetadataService);

                await orchestrator.ScanAsync(observedImage.Id,
                                             ScanTriggerSource.Manual,
                                             CancellationToken.None)
                                  .ConfigureAwait(false);
                await orchestrator.ScanAsync(observedImage.Id,
                                             ScanTriggerSource.Manual,
                                             CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var findings = await dbContext.UpdateFindings.Where(entity => entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate)
                                                             .ToListAsync(TestContext.CancellationToken)
                                                             .ConfigureAwait(false);

                Assert.HasCount(1,
                                findings,
                                "A transient configuration refresh failure must not duplicate or delete the derived base-runtime finding");
                Assert.IsTrue(findings[0].IsActive,
                              "A transient configuration refresh failure must keep the existing derived finding active instead of flickering it away");
            }
        }
    }

    /// <summary>
    /// Verify concurrent scans for the same observed image are serialized across orchestrator scopes
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncConcurrentRunsForSameObservedImageAreSerializedAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var seedContext = database.CreateDbContext();

            await using (seedContext.ConfigureAwait(false))
            {
                var imageCatalogRepository = new ImageCatalogRepository(seedContext);
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

                seedContext.ObservedImages.Add(observedImage);
                await seedContext.SaveChangesAsync(TestContext.CancellationToken)
                                 .ConfigureAwait(false);
            }

            var dbContext1 = database.CreateDbContext();
            var dbContext2 = database.CreateDbContext();

            await using (dbContext1.ConfigureAwait(false))

            await using (dbContext2.ConfigureAwait(false))
            {
                var imageCatalogRepository1 = new ImageCatalogRepository(dbContext1);
                var imageCatalogRepository2 = new ImageCatalogRepository(dbContext2);
                var imageReferenceParser = new ImageReferenceParser();
                var baseImageResolver = Substitute.For<IBaseImageResolver>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var firstResolveStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseFirstResolve = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var resolveCallCount = 0;
                var baseImageDescriptors = ExternalOperationResult<IReadOnlyList<BaseImageDescriptor>>.Succeeded([
                                                                                                                     new BaseImageDescriptor
                                                                                                                     {
                                                                                                                         Registry = "docker.io",
                                                                                                                         Repository = "library/debian",
                                                                                                                         Tag = "12.0.0",
                                                                                                                         Digest = "sha256:base",
                                                                                                                         Depth = 1,
                                                                                                                         SourceReference = "FROM debian:12.0.0",
                                                                                                                     },
                                                                                                                 ]);

                baseImageResolver.ResolveAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                 .Returns(async _ =>
                                          {
                                              var callIndex = Interlocked.Increment(ref resolveCallCount);

                                              if (callIndex == 1)
                                              {
                                                  firstResolveStarted.TrySetResult(true);
                                                  await releaseFirstResolve.Task.ConfigureAwait(false);
                                              }

                                              return baseImageDescriptors;
                                          });
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(callInfo =>
                                                {
                                                    var imageReference = callInfo.ArgAt<ImageReference>(0);

                                                    return ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                               {
                                                                                                                   Tag = imageReference.Tag,
                                                                                                                   Digest = imageReference.Digest,
                                                                                                                   PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                               });
                                                });
                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<RegistryImageConfigurationData>.NotFound("No registry config"));

                var orchestrator1 = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                              baseImageResolver,
                                                              dbContext1,
                                                              derivedBaseRuntimeDetector,
                                                              dotNetReleaseMetadataService,
                                                              nginxReleaseMetadataService,
                                                              imageCatalogRepository1,
                                                              imageReferenceParser,
                                                              NullLogger<ImageScanOrchestrator>.Instance,
                                                              registryMetadataService);
                var orchestrator2 = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                              baseImageResolver,
                                                              dbContext2,
                                                              derivedBaseRuntimeDetector,
                                                              dotNetReleaseMetadataService,
                                                              nginxReleaseMetadataService,
                                                              imageCatalogRepository2,
                                                              imageReferenceParser,
                                                              NullLogger<ImageScanOrchestrator>.Instance,
                                                              registryMetadataService);
                var observedImageId = await dbContext1.ObservedImages.Select(entity => entity.Id)
                                                                     .SingleAsync(TestContext.CancellationToken)
                                                                     .ConfigureAwait(false);

                var firstScanTask = orchestrator1.ScanAsync(observedImageId, ScanTriggerSource.Scheduled, CancellationToken.None);

                await firstResolveStarted.Task.ConfigureAwait(false);

                var secondScanTask = orchestrator2.ScanAsync(observedImageId, ScanTriggerSource.Scheduled, CancellationToken.None);

                await Task.Delay(100, TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(1,
                                Volatile.Read(ref resolveCallCount),
                                "Concurrent scans for the same observed image must wait for the in-flight scan before starting base-image resolution");

                releaseFirstResolve.SetResult(true);

                await Task.WhenAll(firstScanTask, secondScanTask).ConfigureAwait(false);

                var assertionContext = database.CreateDbContext();

                await using (assertionContext.ConfigureAwait(false))
                {
                    var relationshipCount = await assertionContext.ImageRelationships.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);
                    var scanRunCount = await assertionContext.ScanRuns.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);

                    Assert.AreEqual(1,
                                    relationshipCount,
                                    "Serialized scans must leave a single current base-image relationship for the observed image");
                    Assert.AreEqual(2,
                                    scanRunCount,
                                    "Both serialized scans must complete and persist their scan runs");
                }
            }
        }
    }

    #endregion // Methods
}