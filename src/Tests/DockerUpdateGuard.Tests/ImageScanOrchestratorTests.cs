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
using Microsoft.Extensions.Logging;
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
                                 .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<BaseImageDescriptor>>([
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
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(callInfo =>
                                                {
                                                    var imageReference = callInfo.ArgAt<ImageReference>(0);

                                                    return imageReference.Repository switch
                                                           {
                                                               "company/app" => ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                                                    {
                                                                                                                                        Tag = "1.0.0",
                                                                                                                                        Digest = "sha256:app",
                                                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                                    }),
                                                               "library/debian" => ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                                                       {
                                                                                                                                           Tag = "12.0.0",
                                                                                                                                           Digest = "sha256:base-old",
                                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                                       }),
                                                               _ => ExternalOperationResult.NotFound<DockerHubTagData>($"No tag metadata is available for '{imageReference.FullReference}'"),
                                                           };
                                                });

                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.NotFound<RegistryImageConfigurationData>("No registry config"));

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
                                           .GetTagAsync(Arg.Is<ImageReference>(entity => entity != null
                                                                                         && entity.Registry == "docker.io"
                                                                                         && entity.Repository == "library/debian"
                                                                                         && entity.Tag == "12.0.0"
                                                                                         && entity.Digest == "sha256:base-old"),
                                                        cancellationToken: Arg.Any<CancellationToken>());

                _ = registryMetadataService.DidNotReceive()
                                           .GetTagsAsync("docker.io",
                                                         "library/debian",
                                                         Arg.Any<string?>(),
                                                         Arg.Any<string?>(),
                                                         cancellationToken: Arg.Any<CancellationToken>());
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
                                 .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<BaseImageDescriptor>>([
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
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(callInfo =>
                                                {
                                                    var imageReference = callInfo.ArgAt<ImageReference>(0);

                                                    return imageReference.Repository switch
                                                           {
                                                               "company/app" => ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                                                    {
                                                                                                                                        Tag = "1.0.0-alpine",
                                                                                                                                        Digest = "sha256:app",
                                                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                                    }),
                                                               "dotnet/runtime" => ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                                                       {
                                                                                                                                           Tag = "10.0-alpine",
                                                                                                                                           Digest = "sha256:base-current",
                                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                                       }),
                                                               _ => ExternalOperationResult.NotFound<DockerHubTagData>($"No tag metadata is available for '{imageReference.FullReference}'"),
                                                           };
                                                });
                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.NotFound<RegistryImageConfigurationData>("No registry config"));
                registryMetadataService.GetTagsAsync("mcr.microsoft.com",
                                                     "dotnet/runtime",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Is<RegistryTagQueryOptions>(options => options != null
                                                                                                && options.CurrentDigest == "sha256:base-current"
                                                                                                && options.CurrentTag == "10.0-alpine"
                                                                                                && options.MaximumTags == 150
                                                                                                && options.VersionLineTag == "10.0-alpine"
                                                                                                && options.PublishedSinceUtc == new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero)),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
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
                                                         Arg.Any<string?>(),
                                                         Arg.Any<string?>(),
                                                         Arg.Any<RegistryTagQueryOptions?>(),
                                                         Arg.Any<CancellationToken>());
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
                                 .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<BaseImageDescriptor>>([
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

                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(callInfo =>
                                                {
                                                    var imageReference = callInfo.ArgAt<ImageReference>(0);

                                                    return imageReference.Repository switch
                                                           {
                                                               "company/app" => ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                                                    {
                                                                                                                                        Tag = "1.0.0",
                                                                                                                                        Digest = "sha256:app",
                                                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                                    }),
                                                               "library/debian" => ExternalOperationResult.Failed<DockerHubTagData>("The exact base image metadata is unavailable"),
                                                               _ => ExternalOperationResult.NotFound<DockerHubTagData>($"No tag metadata is available for '{imageReference.FullReference}'"),
                                                           };
                                                });

                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.NotFound<RegistryImageConfigurationData>("No registry config"));

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
                                                         Arg.Any<string?>(),
                                                         Arg.Any<string?>(),
                                                         cancellationToken: Arg.Any<CancellationToken>());
            }
        }
    }

    /// <summary>
    /// Verify a failed observed image scan is not downgraded to partial by a later base image failure
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncKeepsFailedStatusWhenBaseImageMetadataAlsoFailsAsync()
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
                                 .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<BaseImageDescriptor>>([
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

                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Failed<DockerHubTagData>("The registry is unavailable"));

                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.NotFound<RegistryImageConfigurationData>("No registry config"));

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

                Assert.AreEqual(ScanRunStatus.Failed,
                                scanRun.Status,
                                "A failed current-image metadata refresh must not be downgraded to partial by a later base image failure");
            }
        }
    }

    /// <summary>
    /// Verify an exception from the registry reports a fixed reason in the scan run
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncWhenRegistryThrowsReportsFixedReasonAsync()
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
                var injected = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (registry.internal.test:443)");
                var logger = new TestLogger<ImageScanOrchestrator>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();

                dbContext.ObservedImages.Add(observedImage);

                await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                               .ConfigureAwait(false);

                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(Task.FromException<ExternalOperationResult<DockerHubTagData>>(injected));

                var orchestrator = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                             Substitute.For<IBaseImageResolver>(),
                                                             dbContext,
                                                             Substitute.For<IDerivedBaseRuntimeDetector>(),
                                                             Substitute.For<IDotNetReleaseMetadataService>(),
                                                             Substitute.For<INginxReleaseMetadataService>(),
                                                             imageCatalogRepository,
                                                             new ImageReferenceParser(),
                                                             logger,
                                                             registryMetadataService);

                await orchestrator.ScanAsync(observedImage.Id,
                                             ScanTriggerSource.Manual,
                                             CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var scanRun = await dbContext.ScanRuns.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(ScanRunStatus.Failed, scanRun.Status, "An exception from the registry must fail the scan run");
                Assert.AreEqual("Observed image scan failed: connection failed",
                                scanRun.ErrorMessage,
                                "The scan run must carry the fixed reason instead of the exception text");
                Assert.IsNotNull(scanRun.ErrorMessage, "A failed result must carry a message");
                Assert.DoesNotContain("internal.test", scanRun.ErrorMessage, "The exception text must not reach the scan run");
                Assert.Contains(entry => ReferenceEquals(entry.Exception, injected),
                                logger.Entries,
                                "The failure must still be logged with the exception");
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
                                 .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<BaseImageDescriptor>>([]));

                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:app",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.NotFound<RegistryImageConfigurationData>("No registry config"));

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
    /// Verify a failing observed image scan does not abort the remaining batch items
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ImageScanOrchestratorScanAllAsyncContinuesAfterItemScanFailureAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var seedContext = database.CreateDbContext();

            await using (seedContext.ConfigureAwait(false))
            {
                var imageCatalogRepository = new ImageCatalogRepository(seedContext);
                var firstImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                  "company/first",
                                                                                                  "1.0.0",
                                                                                                  "sha256:first",
                                                                                                  cancellationToken: CancellationToken.None)
                                                                    .ConfigureAwait(false);
                var secondImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                   "company/second",
                                                                                                   "2.0.0",
                                                                                                   "sha256:second",
                                                                                                   cancellationToken: CancellationToken.None)
                                                                     .ConfigureAwait(false);

                seedContext.ObservedImages.Add(new ObservedImage
                                               {
                                                   Name = "First",
                                                   CurrentImageVersionId = firstImageVersion.Id,
                                               });
                seedContext.ObservedImages.Add(new ObservedImage
                                               {
                                                   Name = "Second",
                                                   CurrentImageVersionId = secondImageVersion.Id,
                                               });

                await seedContext.SaveChangesAsync(TestContext.CancellationToken)
                                 .ConfigureAwait(false);

                var failingContext = database.CreateSaveChangesFailingDbContext();

                await using (failingContext.ConfigureAwait(false))
                {
                    var logger = new TestLogger<ImageScanOrchestrator>();
                    var orchestrator = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                                 Substitute.For<IBaseImageResolver>(),
                                                                 failingContext,
                                                                 Substitute.For<IDerivedBaseRuntimeDetector>(),
                                                                 Substitute.For<IDotNetReleaseMetadataService>(),
                                                                 Substitute.For<INginxReleaseMetadataService>(),
                                                                 new ImageCatalogRepository(failingContext),
                                                                 new ImageReferenceParser(),
                                                                 logger,
                                                                 Substitute.For<IRegistryMetadataService>());

                    failingContext.FailOnSaveChanges = true;

                    await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                      .ConfigureAwait(false);

                    var itemFailures = logger.Entries.Count(entry => entry.EventId.Id == 2033);

                    Assert.AreEqual(2, itemFailures, "Every failing observed image scan must be logged so later items are still attempted");
                    Assert.Contains(entry => entry.EventId.Id == 2062, logger.Entries, "The batch must complete even when individual observed image scans fail");

                    var recoveryFailures = logger.Entries.Where(entry => entry.EventId.Id == 2034).ToList();

                    Assert.HasCount(2, recoveryFailures, "Every failed recovery save must be logged per observed image");
                    Assert.IsTrue(recoveryFailures.All(entry => entry.LogLevel == LogLevel.Error), "A failed recovery save must be logged as an error");
                }
            }
        }
    }

    /// <summary>
    /// Verify a batch scan still completes the remaining image after an earlier image fails before its scan body
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ImageScanOrchestratorScanAllAsyncCompletesRemainingImageAfterEarlierImageFailsBeforeScanAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var seedContext = database.CreateDbContext();

            await using (seedContext.ConfigureAwait(false))
            {
                var imageCatalogRepository = new ImageCatalogRepository(seedContext);
                var firstImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                  "company/first",
                                                                                                  "1.0.0",
                                                                                                  "sha256:first",
                                                                                                  cancellationToken: CancellationToken.None)
                                                                    .ConfigureAwait(false);
                var secondImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                   "company/second",
                                                                                                   "2.0.0",
                                                                                                   "sha256:second",
                                                                                                   cancellationToken: CancellationToken.None)
                                                                     .ConfigureAwait(false);

                seedContext.ObservedImages.Add(new ObservedImage
                                               {
                                                   Name = "First",
                                                   CurrentImageVersionId = firstImageVersion.Id,
                                               });
                seedContext.ObservedImages.Add(new ObservedImage
                                               {
                                                   Name = "Second",
                                                   CurrentImageVersionId = secondImageVersion.Id,
                                               });

                await seedContext.SaveChangesAsync(TestContext.CancellationToken)
                                 .ConfigureAwait(false);

                var failingContext = database.CreateSaveChangesFailingDbContext();

                await using (failingContext.ConfigureAwait(false))
                {
                    var logger = new TestLogger<ImageScanOrchestrator>();
                    var baseImageResolver = Substitute.For<IBaseImageResolver>();
                    var registryMetadataService = Substitute.For<IRegistryMetadataService>();

                    baseImageResolver.ResolveAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                     .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<BaseImageDescriptor>>([]));
                    registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                           .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                        {
                                                                                                            Tag = "1.0.0",
                                                                                                            Digest = "sha256:app",
                                                                                                            PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                        }));
                    registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                           .Returns(ExternalOperationResult.NotFound<RegistryImageConfigurationData>("No registry config"));

                    var orchestrator = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                                 baseImageResolver,
                                                                 failingContext,
                                                                 Substitute.For<IDerivedBaseRuntimeDetector>(),
                                                                 Substitute.For<IDotNetReleaseMetadataService>(),
                                                                 Substitute.For<INginxReleaseMetadataService>(),
                                                                 new ImageCatalogRepository(failingContext),
                                                                 new ImageReferenceParser(),
                                                                 logger,
                                                                 registryMetadataService);

                    failingContext.FailOnSaveAttempt = 1;

                    await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                      .ConfigureAwait(false);

                    failingContext.ChangeTracker.Clear();

                    var itemFailures = logger.Entries.Count(entry => entry.EventId.Id == 2033);
                    var succeededRuns = await failingContext.ScanRuns.Where(entity => entity.Type == ScanRunType.ObservedImage
                                                                                      && entity.Status == ScanRunStatus.Succeeded
                                                                                      && entity.CompletedAtUtc != null)
                                                                     .CountAsync(TestContext.CancellationToken)
                                                                     .ConfigureAwait(false);

                    Assert.AreEqual(1, itemFailures, "Exactly the image whose first save failed must be logged as a batch item failure");
                    Assert.AreEqual(1, succeededRuns, "The remaining image must be scanned to completion after the earlier image failed");

                    var allRuns = await failingContext.ScanRuns.Where(entity => entity.Type == ScanRunType.ObservedImage)
                                                               .ToListAsync(TestContext.CancellationToken)
                                                               .ConfigureAwait(false);
                    var failedRuns = allRuns.Where(entity => entity.Status == ScanRunStatus.Failed).ToList();

                    Assert.HasCount(2, allRuns, "The run of the failed first save must be stored besides the later run");
                    Assert.DoesNotContain(entity => entity.Status == ScanRunStatus.Running, allRuns, "No run of the batch may stay running");
                    Assert.HasCount(1, failedRuns, "The run of the failed first save must be stored as failed");
                    Assert.IsNotNull(failedRuns[0].CompletedAtUtc, "The failed run must carry a completion time");
                    Assert.AreEqual("Observed image scan could not be saved: unexpected error", failedRuns[0].ErrorMessage, "The failed run must carry the save failure line");

                    await registryMetadataService.Received(1)
                                                 .GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                                 .ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Verify cancellation during an observed image scan aborts the batch instead of being swallowed
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task ImageScanOrchestratorScanAllAsyncRethrowsCancellationFromItemScanAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var seedContext = database.CreateDbContext();

            await using (seedContext.ConfigureAwait(false))
            {
                var imageVersion = await new ImageCatalogRepository(seedContext).GetOrCreateImageVersionAsync("docker.io",
                                                                                                              "company/app",
                                                                                                              "1.0.0",
                                                                                                              "sha256:app",
                                                                                                              cancellationToken: CancellationToken.None)
                                                                                .ConfigureAwait(false);

                seedContext.ObservedImages.Add(new ObservedImage
                                               {
                                                   Name = "App",
                                                   CurrentImageVersionId = imageVersion.Id,
                                               });

                await seedContext.SaveChangesAsync(TestContext.CancellationToken)
                                 .ConfigureAwait(false);

                var failingContext = database.CreateSaveChangesFailingDbContext();

                await using (failingContext.ConfigureAwait(false))
                {
                    var logger = new TestLogger<ImageScanOrchestrator>();
                    var orchestrator = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                                 Substitute.For<IBaseImageResolver>(),
                                                                 failingContext,
                                                                 Substitute.For<IDerivedBaseRuntimeDetector>(),
                                                                 Substitute.For<IDotNetReleaseMetadataService>(),
                                                                 Substitute.For<INginxReleaseMetadataService>(),
                                                                 new ImageCatalogRepository(failingContext),
                                                                 new ImageReferenceParser(),
                                                                 logger,
                                                                 Substitute.For<IRegistryMetadataService>());

                    failingContext.SaveChangesException = new OperationCanceledException();
                    failingContext.FailOnSaveChanges = true;

                    await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None),
                                                                                "Cancellation during an observed image scan must abort the batch instead of being swallowed")
                                .ConfigureAwait(false);

                    Assert.DoesNotContain(entry => entry.EventId.Id == 2033, logger.Entries, "A cancelled observed image scan must not be reported as an item failure");
                    Assert.AreEqual(1, failingContext.SaveChangesAttemptCount, "A cancellation must be rethrown without a recovery save");
                }
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
                                 .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<BaseImageDescriptor>>([]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:app",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));
                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<RegistryImageConfigurationData>(new RegistryImageConfigurationData
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
                                            .Returns(ExternalOperationResult.Succeeded<DotNetChannelReleaseData>(new DotNetChannelReleaseData
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
                                 .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<BaseImageDescriptor>>([]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:web",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));
                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<RegistryImageConfigurationData>(new RegistryImageConfigurationData
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
                                           .Returns(ExternalOperationResult.Succeeded<NginxChannelReleaseData>(new NginxChannelReleaseData
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
                                 .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<BaseImageDescriptor>>([]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:app",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));
                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<RegistryImageConfigurationData>(new RegistryImageConfigurationData
                                                                                                                  {
                                                                                                                      EnvironmentVariables = [
                                                                                                                                                 "DOTNET_VERSION=9.0.13",
                                                                                                                                             ],
                                                                                                                  }),
                                                ExternalOperationResult.Failed<RegistryImageConfigurationData>("The registry configuration is temporarily unavailable"));
                derivedBaseRuntimeDetector.Detect(Arg.Any<DockerImageInspectData>(), Arg.Any<IReadOnlyList<DockerImageHistoryEntryData>>())
                                          .Returns(new DerivedBaseRuntimeDescriptor
                                                   {
                                                       Kind = DerivedBaseRuntimeKind.DotNet,
                                                       RuntimeVersion = new Version(9, 0, 13),
                                                       ChannelVersion = "9.0",
                                                   });
                dotNetReleaseMetadataService.GetChannelReleaseAsync("9.0", Arg.Any<CancellationToken>())
                                            .Returns(ExternalOperationResult.Succeeded<DotNetChannelReleaseData>(new DotNetChannelReleaseData
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
    /// Verify a transient release feed failure does not delete existing derived findings
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncPreservesDerivedFindingWhenReleaseFeedFailsAsync()
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
                                 .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<BaseImageDescriptor>>([]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:app",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));
                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<RegistryImageConfigurationData>(new RegistryImageConfigurationData
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
                                            .Returns(ExternalOperationResult.Succeeded<DotNetChannelReleaseData>(new DotNetChannelReleaseData
                                                                                                                 {
                                                                                                                     ChannelVersion = "9.0",
                                                                                                                     LatestRuntimeVersion = new Version(9, 0, 15),
                                                                                                                     IsSecurityRelease = true,
                                                                                                                 }),
                                                     ExternalOperationResult.Failed<DotNetChannelReleaseData>("The release feed is temporarily unavailable"));

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
                                "A transient release feed failure must not duplicate or delete the derived base-runtime finding");
                Assert.IsTrue(findings[0].IsActive,
                              "A transient release feed failure must keep the existing derived finding active instead of flickering it away");
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
            {
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
                    var baseImageDescriptors = ExternalOperationResult.Succeeded<IReadOnlyList<BaseImageDescriptor>>([
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
                    registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                           .Returns(callInfo =>
                                                    {
                                                        var imageReference = callInfo.ArgAt<ImageReference>(0);

                                                        return ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                                   {
                                                                                                                       Tag = imageReference.Tag,
                                                                                                                       Digest = imageReference.Digest,
                                                                                                                       PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                   });
                                                    });
                    registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                           .Returns(ExternalOperationResult.NotFound<RegistryImageConfigurationData>("No registry config"));

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
    }

    /// <summary>
    /// Verify the shared per-observed-image scan lock is released and removed once a scan completes
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncReleasesObservedImageScanLockAfterCompletionAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var currentImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                    "company/lock-cleanup",
                                                                                                    "1.0.0",
                                                                                                    "sha256:app",
                                                                                                    cancellationToken: CancellationToken.None)
                                                                      .ConfigureAwait(false);
                var observedImage = new ObservedImage
                                    {
                                        Name = "Lock Cleanup",
                                        CurrentImageVersionId = currentImageVersion.Id,
                                    };
                var baseImageResolver = Substitute.For<IBaseImageResolver>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();

                dbContext.ObservedImages.Add(observedImage);
                await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                               .ConfigureAwait(false);

                baseImageResolver.ResolveAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                 .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<BaseImageDescriptor>>([]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:app",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));
                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.NotFound<RegistryImageConfigurationData>("No registry config"));

                var orchestrator = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                             baseImageResolver,
                                                             dbContext,
                                                             Substitute.For<IDerivedBaseRuntimeDetector>(),
                                                             Substitute.For<IDotNetReleaseMetadataService>(),
                                                             Substitute.For<INginxReleaseMetadataService>(),
                                                             imageCatalogRepository,
                                                             new ImageReferenceParser(),
                                                             NullLogger<ImageScanOrchestrator>.Instance,
                                                             registryMetadataService);

                await orchestrator.ScanAsync(observedImage.Id,
                                             ScanTriggerSource.Manual,
                                             CancellationToken.None)
                                  .ConfigureAwait(false);

                Assert.AreEqual(0,
                                ImageScanOrchestrator.ObservedImageScanLocks.GetReferenceCount(observedImage.Id),
                                "The per-observed-image scan lock must be removed once its scan completes so the registry does not grow unboundedly");
            }
        }
    }

    /// <summary>
    /// Verify a source reference over the stored length is cut to 500 characters ending in the truncation marker
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncWhenSourceReferenceExceedsStoredLengthCutsItAsync()
    {
        var longText = new string('x', 501);
        var longReference = new ImageReference
                            {
                                Registry = new string('a', 200),
                                Repository = new string('b', 255),
                                Tag = "1.0.0",
                                Digest = "sha256:" + new string('c', 64),
                            }.FullReference;

        var cutText = await ScanAndReadSourceReferenceAsync(longText).ConfigureAwait(false);
        var cutReference = await ScanAndReadSourceReferenceAsync(longReference).ConfigureAwait(false);

        Assert.IsGreaterThan(500, longReference.Length, "The valid reference must exceed the stored length");
        Assert.AreEqual(500, cutText!.Length, "A source reference of 501 characters must be stored with 500 characters");
        Assert.EndsWith("…", cutText, StringComparison.Ordinal, "The cut source reference must end with the truncation marker");
        Assert.StartsWith(new string('x', 499), cutText, StringComparison.Ordinal, "The cut source reference must keep its first 499 characters");
        Assert.AreEqual(500, cutReference!.Length, "A valid reference over the stored length must be stored with 500 characters");
        Assert.EndsWith("…", cutReference, StringComparison.Ordinal, "The cut reference must end with the truncation marker");
        Assert.StartsWith(longReference[..499], cutReference, StringComparison.Ordinal, "The cut reference must keep its first 499 characters");
    }

    /// <summary>
    /// Verify a source reference within the stored length, up to the 500 character boundary, is stored unchanged
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ImageScanOrchestratorScanAsyncWhenSourceReferenceFitsStoredLengthStoresItUnchangedAsync()
    {
        const string shortReference = "FROM debian:12.0.0";
        var boundaryReference = new ImageReference
                                {
                                    Registry = new string('a', 200),
                                    Repository = new string('b', 221),
                                    Tag = "1.0.0",
                                    Digest = "sha256:" + new string('c', 64),
                                }.FullReference;

        var storedShort = await ScanAndReadSourceReferenceAsync(shortReference).ConfigureAwait(false);
        var storedBoundary = await ScanAndReadSourceReferenceAsync(boundaryReference).ConfigureAwait(false);

        Assert.AreEqual(500, boundaryReference.Length, "The boundary reference must have exactly the stored length");
        Assert.AreEqual(shortReference, storedShort, "A source reference within the stored length must be stored unchanged");
        Assert.AreEqual(boundaryReference, storedBoundary, "A source reference of exactly 500 characters must be stored unchanged");
    }

    /// <summary>
    /// Verify a failed final save of one image discards its changes, ends its run as failed and leaves the next image unaffected
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    [TestCategory("AC3")]
    public async Task ImageScanOrchestratorScanAllAsyncDiscardsFailedImageChangesBeforeNextImageAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var observedImageIds = await SeedObservedImagesAsync(database, TestContext.CancellationToken).ConfigureAwait(false);
            var failingContext = database.CreateSaveChangesFailingDbContext();

            await using (failingContext.ConfigureAwait(false))
            {
                var logger = new TestLogger<ImageScanOrchestrator>();
                var orchestrator = CreateFailingContextOrchestrator(failingContext, logger, CreateBaseImageResolver("company/first"));

                failingContext.FailWhen = tracker => tracker.Entries<ImageVersion>()
                                                            .Any(entry => entry.State == EntityState.Modified && entry.Entity.Digest == "sha256:first");

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var readContext = database.CreateDbContext();

                await using (readContext.ConfigureAwait(false))
                {
                    var runs = await readContext.ScanRuns.Where(entity => entity.Type == ScanRunType.ObservedImage)
                                                         .ToListAsync(TestContext.CancellationToken)
                                                         .ConfigureAwait(false);
                    var firstRun = runs.SingleOrDefault(entity => entity.ObservedImageId == observedImageIds[0]);
                    var secondRun = runs.SingleOrDefault(entity => entity.ObservedImageId == observedImageIds[1]);
                    var firstVersion = await readContext.ImageVersions.SingleAsync(entity => entity.Digest == "sha256:first", TestContext.CancellationToken)
                                                                      .ConfigureAwait(false);

                    Assert.IsNotNull(firstRun, "The failed image must have exactly one stored run");
                    Assert.IsNotNull(secondRun, "The later image must have exactly one stored run");
                    Assert.AreEqual(ScanRunStatus.Failed, firstRun.Status, "A run whose final save failed must end as failed");
                    Assert.IsNotNull(firstRun.CompletedAtUtc, "The failed run must carry a completion time");
                    Assert.AreEqual($"Base image unavailable{Environment.NewLine}Observed image scan could not be saved: unexpected error",
                                    firstRun.ErrorMessage,
                                    "The failed run must list the messages gathered so far followed by the save failure line");
                    Assert.IsNull(firstVersion.MetadataJson, "The registry metadata of the failed image must not be stored by a later save");
                    Assert.AreEqual(ScanRunStatus.Succeeded, secondRun.Status, "The later image must be scanned to completion");
                    Assert.DoesNotContain(entity => entity.Status == ScanRunStatus.Running, runs, "No run of the batch may stay running");
                    Assert.ContainsSingle(entry => entry.EventId.Id == 2033, logger.Entries, "Only the failed image must be logged as a batch item failure");
                }
            }
        }
    }

    /// <summary>
    /// Verify a failed first save stores the run once as failed and rethrows the original exception
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ImageScanOrchestratorScanAsyncStoresFailedRunWhenFirstSaveFailsAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var observedImageIds = await SeedObservedImagesAsync(database, TestContext.CancellationToken).ConfigureAwait(false);
            var failingContext = database.CreateSaveChangesFailingDbContext();

            await using (failingContext.ConfigureAwait(false))
            {
                var logger = new TestLogger<ImageScanOrchestrator>();
                var orchestrator = CreateFailingContextOrchestrator(failingContext, logger, CreateBaseImageResolver(null));
                var saveException = new InvalidOperationException("Simulated first save failure");

                failingContext.SaveChangesException = saveException;
                failingContext.FailOnSaveAttempt = 1;

                var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => orchestrator.ScanAsync(observedImageIds[0], ScanTriggerSource.Manual, CancellationToken.None),
                                                                                        "The original save exception must reach the caller of a direct scan")
                                         .ConfigureAwait(false);

                Assert.AreSame(saveException, thrown, "The original exception must be rethrown, not the recovery's");

                var readContext = database.CreateDbContext();

                await using (readContext.ConfigureAwait(false))
                {
                    var runs = await readContext.ScanRuns.ToListAsync(TestContext.CancellationToken)
                                                         .ConfigureAwait(false);

                    Assert.HasCount(1, runs, "The run of the failed first save must be stored exactly once");
                    Assert.AreEqual(ScanRunStatus.Failed, runs[0].Status, "The run must be stored as failed");
                    Assert.IsNotNull(runs[0].CompletedAtUtc, "The stored run must carry a completion time");
                    Assert.AreEqual("Observed image scan could not be saved: unexpected error",
                                    runs[0].ErrorMessage,
                                    "The stored run must carry the save failure line without the exception text");
                }
            }
        }
    }

    /// <summary>
    /// Verify a failing recovery save is logged and leaves nothing tracked, so a later scan on the same context is unaffected
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ImageScanOrchestratorScanAsyncLeavesNothingTrackedWhenFailedRunCannotBeSavedAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var observedImageIds = await SeedObservedImagesAsync(database, TestContext.CancellationToken).ConfigureAwait(false);
            var failingContext = database.CreateSaveChangesFailingDbContext();

            await using (failingContext.ConfigureAwait(false))
            {
                var logger = new TestLogger<ImageScanOrchestrator>();
                var orchestrator = CreateFailingContextOrchestrator(failingContext, logger, CreateBaseImageResolver(null));
                var saveException = new InvalidOperationException("Simulated persistence failure of every save");

                failingContext.SaveChangesException = saveException;
                failingContext.FailOnSaveChanges = true;

                var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => orchestrator.ScanAsync(observedImageIds[0], ScanTriggerSource.Manual, CancellationToken.None),
                                                                                        "The original exception must be rethrown when the recovery save fails too")
                                         .ConfigureAwait(false);
                var recoveryEntries = logger.Entries.Where(entry => entry.EventId.Id == 2034).ToList();

                Assert.AreSame(saveException, thrown, "The original exception must be rethrown");
                Assert.HasCount(1, recoveryEntries, "The failed recovery save must be logged once");
                Assert.AreEqual(LogLevel.Error, recoveryEntries[0].LogLevel, "The failed recovery save must be logged as an error");
                Assert.IsEmpty(failingContext.ChangeTracker.Entries().ToList(), "Nothing of the failed image may stay tracked");

                failingContext.FailOnSaveChanges = false;

                await orchestrator.ScanAsync(observedImageIds[1], ScanTriggerSource.Manual, CancellationToken.None)
                                  .ConfigureAwait(false);

                var readContext = database.CreateDbContext();

                await using (readContext.ConfigureAwait(false))
                {
                    var runs = await readContext.ScanRuns.ToListAsync(TestContext.CancellationToken)
                                                         .ConfigureAwait(false);

                    Assert.HasCount(1, runs, "Only the run of the later scan may be stored");
                    Assert.AreEqual(observedImageIds[1], runs[0].ObservedImageId, "No run of the failed image may be stored");
                    Assert.AreEqual(ScanRunStatus.Succeeded, runs[0].Status, "The later scan on the same context must succeed");
                }
            }
        }
    }

    /// <summary>
    /// Verify a recovery save cancelled by the scan's token is logged and the original exception is rethrown
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ImageScanOrchestratorScanAsyncLogsCancelledRecoverySaveAndRethrowsOriginalAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var observedImageIds = await SeedObservedImagesAsync(database, TestContext.CancellationToken).ConfigureAwait(false);
            var failingContext = database.CreateSaveChangesFailingDbContext();

            await using (failingContext.ConfigureAwait(false))
            {
                var logger = new TestLogger<ImageScanOrchestrator>();
                var orchestrator = CreateFailingContextOrchestrator(failingContext, logger, CreateBaseImageResolver(null));
                var saveException = new InvalidOperationException("Simulated first save failure");
                var saveCalls = 0;

                using (var cancellationTokenSource = new CancellationTokenSource())
                {
                    failingContext.SaveChangesException = saveException;
                    failingContext.FailWhen = _ =>
                                              {
                                                  saveCalls++;

                                                  if (saveCalls == 1)
                                                  {
                                                      cancellationTokenSource.Cancel();
                                                  }

                                                  return saveCalls == 1;
                                              };

                    var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => orchestrator.ScanAsync(observedImageIds[0], ScanTriggerSource.Manual, cancellationTokenSource.Token),
                                                                                            "The original exception must be rethrown when the recovery save is cancelled")
                                             .ConfigureAwait(false);
                    var recoveryEntries = logger.Entries.Where(entry => entry.EventId.Id == 2034).ToList();

                    Assert.AreSame(saveException, thrown, "The original exception must be rethrown");
                    Assert.HasCount(1, recoveryEntries, "The cancelled recovery save must be logged once");
                    Assert.AreEqual(LogLevel.Error, recoveryEntries[0].LogLevel, "The cancelled recovery save must be logged as an error");
                    Assert.IsInstanceOfType<OperationCanceledException>(recoveryEntries[0].Exception, "The log entry must carry the cancellation of the recovery save");
                    Assert.IsEmpty(failingContext.ChangeTracker.Entries().ToList(), "Nothing of the failed image may stay tracked");
                }
            }
        }
    }

    /// <summary>
    /// Seed two enabled observed images
    /// </summary>
    /// <param name="database">Test database</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Identifiers of the first and the second observed image</returns>
    private static async Task<IReadOnlyList<Guid>> SeedObservedImagesAsync(SqliteTestDatabase database, CancellationToken cancellationToken)
    {
        var seedContext = database.CreateDbContext();

        await using (seedContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(seedContext);
            var firstImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                              "company/first",
                                                                                              "1.0.0",
                                                                                              "sha256:first",
                                                                                              cancellationToken: cancellationToken)
                                                                .ConfigureAwait(false);
            var secondImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                               "company/second",
                                                                                               "2.0.0",
                                                                                               "sha256:second",
                                                                                               cancellationToken: cancellationToken)
                                                                 .ConfigureAwait(false);
            var firstImage = new ObservedImage
                             {
                                 Name = "First",
                                 CurrentImageVersionId = firstImageVersion.Id,
                             };
            var secondImage = new ObservedImage
                              {
                                  Name = "Second",
                                  CurrentImageVersionId = secondImageVersion.Id,
                              };

            seedContext.ObservedImages.Add(firstImage);
            seedContext.ObservedImages.Add(secondImage);

            await seedContext.SaveChangesAsync(cancellationToken)
                             .ConfigureAwait(false);

            return [firstImage.Id, secondImage.Id];
        }
    }

    /// <summary>
    /// Create a base image resolver that succeeds with no base image, except for one repository whose resolution is not found
    /// </summary>
    /// <param name="unavailableRepository">Repository whose base image resolution is not found, or null</param>
    /// <returns>Base image resolver</returns>
    private static IBaseImageResolver CreateBaseImageResolver(string? unavailableRepository)
    {
        var baseImageResolver = Substitute.For<IBaseImageResolver>();

        baseImageResolver.ResolveAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                         .Returns(callInfo => callInfo.Arg<ImageReference>().Repository == unavailableRepository
                                                  ? ExternalOperationResult.NotFound<IReadOnlyList<BaseImageDescriptor>>("Base image unavailable")
                                                  : ExternalOperationResult.Succeeded<IReadOnlyList<BaseImageDescriptor>>([]));

        return baseImageResolver;
    }

    /// <summary>
    /// Create an orchestrator over a fault-injecting context whose registry metadata lookups succeed
    /// </summary>
    /// <param name="failingContext">Fault-injecting context</param>
    /// <param name="logger">Logger capturing the entries</param>
    /// <param name="baseImageResolver">Base image resolver</param>
    /// <returns>Orchestrator</returns>
    private static ImageScanOrchestrator CreateFailingContextOrchestrator(SaveChangesFailingDbContext failingContext,
                                                                          TestLogger<ImageScanOrchestrator> logger,
                                                                          IBaseImageResolver baseImageResolver)
    {
        var registryMetadataService = Substitute.For<IRegistryMetadataService>();

        registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                               .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                            {
                                                                                                Tag = "1.0.0",
                                                                                                Digest = "sha256:app",
                                                                                                PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                            }));
        registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                               .Returns(ExternalOperationResult.NotFound<RegistryImageConfigurationData>("No registry config"));

        return new ImageScanOrchestrator(new ApplicationTelemetry(),
                                         baseImageResolver,
                                         failingContext,
                                         Substitute.For<IDerivedBaseRuntimeDetector>(),
                                         Substitute.For<IDotNetReleaseMetadataService>(),
                                         Substitute.For<INginxReleaseMetadataService>(),
                                         new ImageCatalogRepository(failingContext),
                                         new ImageReferenceParser(),
                                         logger,
                                         registryMetadataService);
    }

    /// <summary>
    /// Scan an observed image whose resolved base image carries the given source reference and read the stored value
    /// </summary>
    /// <param name="sourceReference">Source reference of the resolved base image</param>
    /// <returns>Stored source reference of the relationship</returns>
    private async Task<string?> ScanAndReadSourceReferenceAsync(string sourceReference)
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
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();

                dbContext.ObservedImages.Add(observedImage);
                await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                               .ConfigureAwait(false);

                baseImageResolver.ResolveAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                 .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<BaseImageDescriptor>>([
                                                                                                                    new BaseImageDescriptor
                                                                                                                    {
                                                                                                                        Registry = "docker.io",
                                                                                                                        Repository = "library/debian",
                                                                                                                        Tag = "12.0.0",
                                                                                                                        Digest = "sha256:base-old",
                                                                                                                        Depth = 1,
                                                                                                                        SourceReference = sourceReference,
                                                                                                                    },
                                                                                                                ]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:app",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));
                registryMetadataService.GetImageConfigurationAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.NotFound<RegistryImageConfigurationData>("No registry config"));

                var orchestrator = new ImageScanOrchestrator(new ApplicationTelemetry(),
                                                             baseImageResolver,
                                                             dbContext,
                                                             Substitute.For<IDerivedBaseRuntimeDetector>(),
                                                             Substitute.For<IDotNetReleaseMetadataService>(),
                                                             Substitute.For<INginxReleaseMetadataService>(),
                                                             imageCatalogRepository,
                                                             new ImageReferenceParser(),
                                                             NullLogger<ImageScanOrchestrator>.Instance,
                                                             registryMetadataService);

                await orchestrator.ScanAsync(observedImage.Id,
                                             ScanTriggerSource.Manual,
                                             CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var relationship = await dbContext.ImageRelationships.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);

                return relationship.SourceReference;
            }
        }
    }

    #endregion // Methods
}