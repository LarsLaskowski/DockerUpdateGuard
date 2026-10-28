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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for the runtime container orchestration path
/// </summary>
[TestClass]
public class RuntimeContainerScanOrchestratorTests
{
    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify runtime scans create runtime findings without observed-image base relationships
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncCreatesRuntimeFindingAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var options = new DockerUpdateGuardOptions
                              {
                                  DockerInstances = [
                                                        new DockerInstanceOptions
                                                        {
                                                            Name = "Production",
                                                            BaseUrl = "https://docker.example.test",
                                                            Enabled = true,
                                                            RequestTimeoutSeconds = 15,
                                                        },
                                                    ],
                              };
                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(options);
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var instanceDiscoveryLogger = new TestLogger<InstanceDiscoveryService>();
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            instanceDiscoveryLogger,
                                                                            optionsMonitor);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "web",
                                                                                                                                  ImageReference = "docker.io/library/nginx:1.0.0@sha256:runtime-old",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                                  StackName = "web-stack",
                                                                                                                                  ServiceName = "frontend",
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.1.0",
                                                                                                                           Digest = "sha256:runtime-new",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.0.0",
                                                                                                                           Digest = "sha256:runtime-old",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:runtime-old",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        logger,
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var dockerInstance = await dbContext.DockerInstances.SingleAsync(TestContext.CancellationToken)
                                                                    .ConfigureAwait(false);
                var scanRun = await dbContext.ScanRuns.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var snapshot = await dbContext.ContainerSnapshots.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var finding = await dbContext.UpdateFindings.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var runtimeImageVersionTask = dbContext.ImageVersions.SingleAsync(entity => entity.Id == snapshot.ImageVersionId, TestContext.CancellationToken);
                var runtimeImageVersion = await runtimeImageVersionTask.ConfigureAwait(false);

                Assert.IsNotNull(finding.RecommendedImageVersionId, "Runtime update findings must persist the recommended image version");

                var recommendedImageTask = dbContext.ImageVersions.SingleAsync(entity => entity.Id == finding.RecommendedImageVersionId.Value, TestContext.CancellationToken);
                var recommendedImage = await recommendedImageTask.ConfigureAwait(false);

                Assert.AreEqual("Production",
                                dockerInstance.Name,
                                "Runtime scans must synchronize configured Docker instances before scanning");
                Assert.AreEqual(ScanRunType.RuntimeContainer,
                                scanRun.Type,
                                "Runtime scans must persist a runtime-container scan run");
                Assert.AreEqual(ScanRunStatus.Succeeded,
                                scanRun.Status,
                                "The runtime scan must succeed when discovery and registry evaluation succeed");
                Assert.AreEqual(dockerInstance.Id,
                                snapshot.DockerInstanceId,
                                "Runtime snapshots must point to the synchronized Docker instance");
                Assert.IsNull(finding.ObservedImageId, "Runtime findings must stay separate from the observed-image path");
                Assert.AreEqual(snapshot.Id,
                                finding.ContainerSnapshotId,
                                "Runtime findings must point to the discovered container snapshot");
                Assert.AreEqual(UpdateFindingType.RuntimeImageUpdate,
                                finding.Type,
                                "Runtime update findings must be classified as runtime image updates");
                Assert.AreEqual(ImageVersionSource.RuntimeContainer,
                                runtimeImageVersion.Source,
                                "Runtime scans must mark discovered image versions as runtime sources");
                Assert.AreEqual("1.1.0",
                                recommendedImage.Tag,
                                "Runtime scans must recommend the newer runtime image tag");

                var imageRelationshipCount = await dbContext.ImageRelationships.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(0,
                                imageRelationshipCount,
                                "Runtime scans must not create observed-image base relationships");
                Assert.Contains(entry => entry.EventId.Id == 2090, instanceDiscoveryLogger.Entries, "Runtime scans must log the start of Docker instance synchronization");
                Assert.Contains(entry => entry.EventId.Id == 2070, logger.Entries, "Runtime scan batches must log when batch processing starts");
                Assert.Contains(entry => entry.EventId.Id == 2072
                                         && entry.Message.Contains("processing 1 Docker instances", StringComparison.Ordinal),
                                logger.Entries,
                                "Runtime scan batches must log a completion summary");
                Assert.Contains(entry => entry.EventId.Id == 2073
                                         && entry.Message.Contains("Production", StringComparison.Ordinal),
                                logger.Entries,
                                "Runtime scans must log when each Docker instance scan starts");
            }
        }
    }

    /// <summary>
    /// Verify runtime scans persist update candidates even when the registry omits candidate digests
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncPersistsCandidatesWithoutDigestAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var options = new DockerUpdateGuardOptions
                              {
                                  DockerInstances = [
                                                        new DockerInstanceOptions
                                                        {
                                                            Name = "Production",
                                                            BaseUrl = "https://docker.example.test",
                                                            Enabled = true,
                                                            RequestTimeoutSeconds = 15,
                                                        },
                                                    ],
                              };
                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(options);
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "web",
                                                                                                                                  ImageReference = "docker.io/library/nginx:1.0.0@sha256:runtime-old",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));

                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));

                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));

                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.1.0",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.0.0",
                                                                                                                           Digest = "sha256:runtime-old",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));

                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:runtime-old",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        new TestLogger<RuntimeContainerScanOrchestrator>(),
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var scanRun = await dbContext.ScanRuns.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var finding = await dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                            .SingleAsync(TestContext.CancellationToken)
                                                            .ConfigureAwait(false);

                Assert.AreEqual(ScanRunStatus.Succeeded,
                                scanRun.Status,
                                "Runtime scans must still complete when a registry candidate has no digest");
                Assert.IsEmpty(finding.TagCandidates,
                               "Candidates without a digest must not be persisted for runtime findings");
            }
        }
    }

    /// <summary>
    /// Verify stale tag candidates are removed when a later runtime scan no longer produces an update finding
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncRemovesStaleTagCandidatesAfterRescanAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var options = new DockerUpdateGuardOptions
                              {
                                  DockerInstances = [
                                                        new DockerInstanceOptions
                                                        {
                                                            Name = "Production",
                                                            BaseUrl = "https://docker.example.test",
                                                            Enabled = true,
                                                            RequestTimeoutSeconds = 15,
                                                        },
                                                    ],
                              };
                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(options);
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "web",
                                                                                                                                  ImageReference = "docker.io/library/nginx:1.0.0@sha256:runtime-old",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.1.0",
                                                                                                                           Digest = "sha256:runtime-new",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.0.0",
                                                                                                                           Digest = "sha256:runtime-old",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]),
                                                ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.0.0",
                                                                                                                           Digest = "sha256:runtime-old",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:runtime-old",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        new TestLogger<RuntimeContainerScanOrchestrator>(),
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);
                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var findings = await dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                             .ToListAsync(TestContext.CancellationToken)
                                                             .ConfigureAwait(false);

                Assert.HasCount(1,
                                findings,
                                "The original finding record must remain for history after the rescan");
                Assert.IsFalse(findings[0].IsActive,
                               "The previous runtime finding must be deactivated when the update is no longer available");
                Assert.IsEmpty(findings[0].TagCandidates,
                               "Stale tag candidates must be removed when a later scan no longer keeps the finding active");
            }
        }
    }

    /// <summary>
    /// Verify a transient discovery failure does not deactivate existing runtime findings
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncPreservesFindingsWhenDiscoveryFailsAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var options = new DockerUpdateGuardOptions
                              {
                                  DockerInstances = [
                                                        new DockerInstanceOptions
                                                        {
                                                            Name = "Production",
                                                            BaseUrl = "https://docker.example.test",
                                                            Enabled = true,
                                                            RequestTimeoutSeconds = 15,
                                                        },
                                                    ],
                              };
                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(options);
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "web",
                                                                                                                                  ImageReference = "docker.io/library/nginx:1.0.0@sha256:runtime-old",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]),
                                             ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Failed("The Docker daemon is unreachable"));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.1.0",
                                                                                                                           Digest = "sha256:runtime-new",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.0.0",
                                                                                                                           Digest = "sha256:runtime-old",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:runtime-old",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        new TestLogger<RuntimeContainerScanOrchestrator>(),
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);
                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var findings = await dbContext.UpdateFindings.ToListAsync(TestContext.CancellationToken)
                                                             .ConfigureAwait(false);

                Assert.HasCount(1,
                                findings,
                                "A transient discovery failure must not create or remove runtime findings");
                Assert.IsTrue(findings[0].IsActive,
                              "A transient discovery failure must keep the existing runtime finding active instead of flickering it away");
            }
        }
    }

    /// <summary>
    /// Verify a transient registry evaluation failure does not deactivate existing runtime findings
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncPreservesFindingsWhenRegistryEvaluationFailsAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var options = new DockerUpdateGuardOptions
                              {
                                  DockerInstances = [
                                                        new DockerInstanceOptions
                                                        {
                                                            Name = "Production",
                                                            BaseUrl = "https://docker.example.test",
                                                            Enabled = true,
                                                            RequestTimeoutSeconds = 15,
                                                        },
                                                    ],
                              };
                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(options);
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "web",
                                                                                                                                  ImageReference = "docker.io/library/nginx:1.0.0@sha256:runtime-old",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.1.0",
                                                                                                                           Digest = "sha256:runtime-new",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.0.0",
                                                                                                                           Digest = "sha256:runtime-old",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]),
                                                ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Failed("The registry is temporarily unavailable"));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:runtime-old",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        new TestLogger<RuntimeContainerScanOrchestrator>(),
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);
                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var findings = await dbContext.UpdateFindings.ToListAsync(TestContext.CancellationToken)
                                                             .ConfigureAwait(false);

                Assert.HasCount(1,
                                findings,
                                "A transient registry evaluation failure must not create or remove runtime findings");
                Assert.IsTrue(findings[0].IsActive,
                              "A transient registry evaluation failure must keep the existing runtime finding active instead of flickering it away");
            }
        }
    }

    /// <summary>
    /// Verify unsupported registry evaluations are logged and produce a partial runtime scan
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncWithUnsupportedRegistryLookupLogsPartialWarningAsync()
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
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "web",
                                                                                                                                  ImageReference = "docker.io/library/nginx:1.0.0@sha256:runtime-old",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                                  StackName = "web-stack",
                                                                                                                                  ServiceName = "frontend",
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Unsupported("Registry adapters cannot evaluate this registry"));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.NotFound("No exact tag metadata is available"));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        logger,
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var scanRun = await dbContext.ScanRuns.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(ScanRunStatus.Partial,
                                scanRun.Status,
                                "Unsupported registry evaluations must keep the runtime scan in a partial state");
                Assert.Contains(entry => entry.EventId.Id == 2078
                                         && entry.LogLevel == LogLevel.Warning,
                                logger.Entries,
                                "Unsupported runtime registry evaluations must be logged as warnings");
                Assert.Contains(entry => entry.EventId.Id == 2041
                                         && entry.Message.Contains("Partial", StringComparison.Ordinal),
                                logger.Entries,
                                "Runtime scans with unsupported registry evaluations must log a partial completion summary");
            }
        }
    }

    /// <summary>
    /// Verify a single invalid container image reference does not stop processing of remaining containers
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncContinuesAfterContainerProcessingFailureAsync()
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
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "web-1",
                                                                                                                                  ImageReference = "docker.io/library/nginx:1.0.0",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-2",
                                                                                                                                  Name = "broken",
                                                                                                                                  ImageReference = "sha256:local-only-image",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-3",
                                                                                                                                  Name = "web-3",
                                                                                                                                  ImageReference = "docker.io/library/nginx:1.1.0",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.1.0",
                                                                                                                           Digest = "sha256:new",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.0.0",
                                                                                                                           Digest = "sha256:old",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:old",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        logger,
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var scanRun = await dbContext.ScanRuns.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var snapshots = await dbContext.ContainerSnapshots.OrderBy(entity => entity.Name)
                                                                  .ToListAsync(TestContext.CancellationToken)
                                                                  .ConfigureAwait(false);

                Assert.AreEqual(ScanRunStatus.Partial,
                                scanRun.Status,
                                "The runtime scan must degrade to partial when a single container cannot be processed");
                Assert.HasCount(3,
                                snapshots,
                                "The runtime scan must preserve the failed container snapshot and continue with the remaining containers");
                Assert.AreSequenceEqual(["broken", "web-1", "web-3"], snapshots.Select(entity => entity.Name).ToArray(), "The runtime scan must keep processing containers after an invalid image reference");
                Assert.AreEqual(UpdateAssessmentStatus.Failed,
                                snapshots.Single(entity => entity.Name == "broken").UpdateAssessmentStatus,
                                "The invalid container must be marked as failed instead of aborting the complete scan");
                Assert.Contains(entry => entry.EventId.Id == 2098
                                         && entry.Message.Contains("broken", StringComparison.Ordinal),
                                logger.Entries,
                                "The runtime scan must log which container was skipped when per-container processing fails");
            }
        }
    }

    /// <summary>
    /// Verify runtime scans merge exact current-tag metadata into paged registry results
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncMergesCurrentTagMetadataAsync()
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
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "telemetry",
                                                                                                                                  ImageReference = "docker.io/networlddev/f1-telemetry:latest@sha256:old",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "networlddev/f1-telemetry",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "2.4.1",
                                                                                                                           Digest = "sha256:new",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Is<ImageReference>(entity => entity != null
                                                                                     && entity.Repository == "networlddev/f1-telemetry"
                                                                                     && entity.Tag == "latest"),
                                                    cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "latest",
                                                                                                        Digest = "sha256:new",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        logger,
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var snapshot = await dbContext.ContainerSnapshots.SingleAsync(TestContext.CancellationToken)
                                                                 .ConfigureAwait(false);
                var finding = await dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                            .SingleAsync(TestContext.CancellationToken)
                                                            .ConfigureAwait(false);
                var recommendedImage = await dbContext.ImageVersions.SingleAsync(entity => entity.Id == finding.RecommendedImageVersionId, TestContext.CancellationToken)
                                                                    .ConfigureAwait(false);

                Assert.AreEqual("latest",
                                recommendedImage.Tag,
                                "Digest-only updates must keep the current alias tag as the persisted recommendation");
                Assert.AreSequenceEqual(["latest", "2.4.1"],
                                        finding.TagCandidates.OrderBy(entity => entity.Rank)
                                                             .Select(entity => entity.Tag)
                                                             .ToArray(),
                                        "The current alias tag must be merged into the persisted tag candidates");
                Assert.AreEqual("2.4.1",
                                snapshot.AvailableUpdateVersionTag,
                                "Digest-only runtime updates must persist the semantic version tag behind the recommended latest digest");
            }
        }
    }

    /// <summary>
    /// Verify digest-only updates display the update version from the running variant family
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncPrefersRunningVariantFamilyForUpdateDisplayAsync()
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
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "heimdall",
                                                                                                                                  ImageReference = "docker.io/linuxserver/heimdall:latest@sha256:old",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "linuxserver/heimdall",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "2.7.6",
                                                                                                                           Digest = "sha256:new",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2026, 07, 03, 12, 00, 10, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "v2.7.6-ls352",
                                                                                                                           Digest = "sha256:new",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2026, 07, 03, 12, 00, 05, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "v2.7.6-ls350",
                                                                                                                           Digest = "sha256:old",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2026, 06, 12, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Is<ImageReference>(entity => entity != null
                                                                                     && entity.Repository == "linuxserver/heimdall"
                                                                                     && entity.Tag == "latest"),
                                                    cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "latest",
                                                                                                        Digest = "sha256:new",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2026, 07, 03, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        logger,
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var snapshot = await dbContext.ContainerSnapshots.SingleAsync(TestContext.CancellationToken)
                                                                 .ConfigureAwait(false);

                Assert.AreEqual("v2.7.6-ls350",
                                snapshot.ResolvedVersionTag,
                                "The running digest must resolve to its build-specific version tag");
                Assert.AreEqual("v2.7.6-ls352",
                                snapshot.AvailableUpdateVersionTag,
                                "The available update must display the tag from the running variant family instead of the later-published plain alias");
            }
        }
    }

    /// <summary>
    /// Verify the resolved version survives registry tag rotation and updates display the plain version tag
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncKeepsResolvedVersionAfterRegistryTagRotationAsync()
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
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "mariadbadmin",
                                                                                                                                  ImageReference = "docker.io/library/phpmyadmin:latest@sha256:old",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/phpmyadmin",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "5.2.3",
                                                                                                                           Digest = "sha256:old",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2026, 07, 03, 10, 00, 05, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "5.2.3-apache",
                                                                                                                           Digest = "sha256:old",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2026, 07, 03, 10, 00, 08, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]),
                                                ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "5.2.3",
                                                                                                                           Digest = "sha256:new",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2026, 07, 04, 12, 00, 05, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "5.2.3-apache",
                                                                                                                           Digest = "sha256:new",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2026, 07, 04, 12, 00, 08, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Is<ImageReference>(entity => entity != null
                                                                                     && entity.Repository == "library/phpmyadmin"
                                                                                     && entity.Tag == "latest"),
                                                    cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "latest",
                                                                                                        Digest = "sha256:old",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2026, 07, 03, 10, 00, 00, TimeSpan.Zero),
                                                                                                    }),
                                                ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "latest",
                                                                                                        Digest = "sha256:new",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2026, 07, 04, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        logger,
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);
                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var snapshots = await dbContext.ContainerSnapshots.ToListAsync(TestContext.CancellationToken)
                                                                  .ConfigureAwait(false);

                Assert.HasCount(2, snapshots, "Each scan run must record its own container snapshot");

                var upToDateSnapshot = snapshots.Single(entity => entity.UpdateAssessmentStatus == UpdateAssessmentStatus.UpToDate);
                var updateSnapshot = snapshots.Single(entity => entity.UpdateAssessmentStatus == UpdateAssessmentStatus.UpdateAvailable);

                Assert.AreEqual("5.2.3",
                                upToDateSnapshot.ResolvedVersionTag,
                                "The running latest digest must resolve to the plain version tag instead of the later-published variant tag");
                Assert.AreEqual("5.2.3",
                                updateSnapshot.ResolvedVersionTag,
                                "The resolved version must be carried over from the earlier snapshot after the registry tags moved to a new digest");
                Assert.AreEqual("5.2.3",
                                updateSnapshot.AvailableUpdateVersionTag,
                                "The available update must display the plain version tag instead of the later-published variant tag");
            }
        }
    }

    /// <summary>
    /// Verify runtime scans use the reported local image digest when the image reference does not include one
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncUsesReportedImageDigestForLatestAliasEvaluationAsync()
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
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "telemetry",
                                                                                                                                  ImageReference = "docker.io/networlddev/f1-telemetry:latest",
                                                                                                                                  ImageDigest = "sha256:241",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "networlddev/f1-telemetry",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "latest",
                                                                                                                           Digest = "sha256:241",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "2.4.1",
                                                                                                                           Digest = "sha256:241",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "2.5.0",
                                                                                                                           Digest = "sha256:250",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Is<ImageReference>(entity => entity != null
                                                                                     && entity.Repository == "networlddev/f1-telemetry"
                                                                                     && entity.Tag == "latest"),
                                                    cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "latest",
                                                                                                        Digest = "sha256:241",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        logger,
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var snapshot = await dbContext.ContainerSnapshots.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var findingCount = await dbContext.UpdateFindings.CountAsync(TestContext.CancellationToken)
                                                                 .ConfigureAwait(false);

                Assert.AreEqual(UpdateAssessmentStatus.UpToDate,
                                snapshot.UpdateAssessmentStatus,
                                "Latest aliases with a current latest digest must stay up to date even when higher semantic tags exist");
                Assert.AreEqual("2.4.1",
                                snapshot.ResolvedVersionTag,
                                "Latest aliases with a matching semantic digest must persist the resolved current version tag");
                Assert.IsNull(snapshot.AvailableUpdateVersionTag,
                              "No available update version tag must be persisted when the runtime image is already up to date");
                Assert.AreEqual(0,
                                findingCount,
                                "No runtime update finding must be persisted when the running latest digest already matches the registry latest digest");
            }
        }
    }

    /// <summary>
    /// Verify runtime scans use the running image architecture when resolving current and available version tags
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncUsesRuntimeArchitectureForVersionTagResolutionAsync()
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
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "telemetry",
                                                                                                                                  ImageReference = "docker.io/networlddev/f1-telemetry:latest",
                                                                                                                                  ImageDigest = "sha256:arm-current",
                                                                                                                                  LocalImageId = "sha256:local-arm-image",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), "sha256:local-arm-image", Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.Succeeded(new DockerImageInspectData
                                                                                                       {
                                                                                                           Id = "sha256:local-arm-image",
                                                                                                           OperatingSystem = "linux",
                                                                                                           Architecture = "arm64",
                                                                                                       }));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), "sha256:local-arm-image", Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.Succeeded([]));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "networlddev/f1-telemetry",
                                                     "linux",
                                                     "arm64",
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "latest",
                                                                                                                           Digest = "sha256:arm-new",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.0.0",
                                                                                                                           Digest = "sha256:arm-current",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.1.0",
                                                                                                                           Digest = "sha256:arm-new",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Is<ImageReference>(entity => entity != null
                                                                                     && entity.Repository == "networlddev/f1-telemetry"
                                                                                     && entity.Tag == "latest"),
                                                    "linux",
                                                    "arm64",
                                                    Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "latest",
                                                                                                        Digest = "sha256:arm-new",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        logger,
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var snapshot = await dbContext.ContainerSnapshots.SingleAsync(TestContext.CancellationToken)
                                                                 .ConfigureAwait(false);

                Assert.AreEqual(UpdateAssessmentStatus.UpdateAvailable,
                                snapshot.UpdateAssessmentStatus,
                                "A newer latest digest for the running architecture must produce an update assessment");
                Assert.AreEqual("1.0.0",
                                snapshot.ResolvedVersionTag,
                                "The current resolved version tag must be derived from the running architecture digest");
                Assert.AreEqual("1.1.0",
                                snapshot.AvailableUpdateVersionTag,
                                "The available update version tag must be resolved for the running architecture");
                await registryMetadataService.Received(1)
                                             .GetTagsAsync("docker.io",
                                                           "networlddev/f1-telemetry",
                                                           "linux",
                                                           "arm64",
                                                           Arg.Is<RegistryTagQueryOptions>(options => options != null
                                                                                                      && options.CurrentDigest == "sha256:arm-current"
                                                                                                      && options.CurrentTag == "latest"
                                                                                                      && options.MaximumTags == 250
                                                                                                      && options.PublishedSinceUtc == new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero)),
                                                           Arg.Any<CancellationToken>())
                                             .ConfigureAwait(false);
                await registryMetadataService.Received(1)
                                             .GetTagAsync(Arg.Any<ImageReference>(),
                                                          "linux",
                                                          "arm64",
                                                          Arg.Any<CancellationToken>())
                                             .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Verify runtime scans resolve MCR channel tags to exact tags from the same variant family
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncUsesSameMcrVariantFamilyForResolvedVersionTagsAsync()
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
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var derivedBaseRuntimeDetector = Substitute.For<IDerivedBaseRuntimeDetector>();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "telemetry",
                                                                                                                                  ImageReference = "mcr.microsoft.com/dotnet/runtime:10.0-alpine",
                                                                                                                                  ImageDigest = "sha256:current",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("mcr.microsoft.com",
                                                     "dotnet/runtime",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "10.0-alpine",
                                                                                                                           Digest = "sha256:current",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "10.0.7-alpine3.23",
                                                                                                                           Digest = "sha256:current",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "10.0.8-alpine3.24",
                                                                                                                           Digest = "sha256:update",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "10.0.8-jammy",
                                                                                                                           Digest = "sha256:other",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 04, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Is<ImageReference>(entity => entity != null
                                                                                     && entity.Repository == "dotnet/runtime"
                                                                                     && entity.Tag == "10.0-alpine"),
                                                    cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "10.0-alpine",
                                                                                                        Digest = "sha256:current",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        logger,
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var snapshot = await dbContext.ContainerSnapshots.SingleAsync(TestContext.CancellationToken)
                                                                 .ConfigureAwait(false);

                Assert.AreEqual("10.0.7-alpine3.23",
                                snapshot.ResolvedVersionTag,
                                "Runtime scans must resolve the current MCR exact version from the same variant family");
                Assert.AreEqual("10.0.8-alpine3.24",
                                snapshot.AvailableUpdateVersionTag,
                                "Runtime scans must resolve available MCR updates from the same variant family");
            }
        }
    }

    /// <summary>
    /// Verify runtime scans persist a derived base-runtime finding for outdated .NET runtimes
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncCreatesDerivedBaseRuntimeFindingAsync()
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
                var derivedBaseRuntimeDetector = new DerivedBaseRuntimeDetector();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);
                var ownImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                "company/api",
                                                                                                "1.0.0",
                                                                                                "sha256:current",
                                                                                                cancellationToken: CancellationToken.None)
                                                                  .ConfigureAwait(false);

                dbContext.ObservedImages.Add(new ObservedImage
                                             {
                                                 Name = "Company API",
                                                 CurrentImageVersionId = ownImageVersion.Id,
                                                 Source = RegistrationSource.Discovery,
                                             });
                await dbContext.SaveChangesAsync(CancellationToken.None)
                               .ConfigureAwait(false);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
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
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.Succeeded(new DockerImageInspectData
                                                                                                       {
                                                                                                           Id = "sha256:local-image",
                                                                                                           EnvironmentVariables = ["DOTNET_VERSION=9.0.13"],
                                                                                                       }));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), "sha256:local-image", Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.Succeeded([]));
                dotNetReleaseMetadataService.GetChannelReleaseAsync("9.0", Arg.Any<CancellationToken>())
                                            .Returns(ExternalOperationResult<DotNetChannelReleaseData>.Succeeded(new DotNetChannelReleaseData
                                                                                                                 {
                                                                                                                     ChannelVersion = "9.0",
                                                                                                                     LatestRuntimeVersion = new Version(9, 0, 15),
                                                                                                                     IsSecurityRelease = true,
                                                                                                                 }));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "company/api",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.0.0",
                                                                                                                           Digest = "sha256:current",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:current",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        logger,
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var finding = await dbContext.UpdateFindings.SingleAsync(entity => entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate, TestContext.CancellationToken)
                                                            .ConfigureAwait(false);

                Assert.AreEqual("Own image uses an outdated .NET base runtime",
                                finding.Summary,
                                "Runtime scans must persist a strong warning for own images with an outdated .NET base runtime");
                Assert.Contains(".NET 9.0.13",
                                finding.Details ?? string.Empty,
                                "The derived runtime finding must mention the detected .NET version");
                Assert.Contains("9.0.15",
                                finding.Details ?? string.Empty,
                                "The derived runtime finding must mention the latest channel runtime");
            }
        }
    }

    /// <summary>
    /// Verify runtime scans persist a derived base-runtime finding for outdated NGINX runtimes
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncCreatesDerivedNginxBaseRuntimeFindingAsync()
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
                var derivedBaseRuntimeDetector = new DerivedBaseRuntimeDetector();
                var dotNetReleaseMetadataService = Substitute.For<IDotNetReleaseMetadataService>();
                var nginxReleaseMetadataService = Substitute.For<INginxReleaseMetadataService>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);
                var ownImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                "company/web",
                                                                                                "1.0.0",
                                                                                                "sha256:web",
                                                                                                cancellationToken: CancellationToken.None)
                                                                  .ConfigureAwait(false);

                dbContext.ObservedImages.Add(new ObservedImage
                                             {
                                                 Name = "Company Web",
                                                 CurrentImageVersionId = ownImageVersion.Id,
                                                 Source = RegistrationSource.Discovery,
                                             });
                await dbContext.SaveChangesAsync(CancellationToken.None)
                               .ConfigureAwait(false);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "web",
                                                                                                                                  ImageReference = "docker.io/company/web:1.0.0",
                                                                                                                                  LocalImageId = "sha256:local-nginx-image",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), "sha256:local-nginx-image", Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.Succeeded(new DockerImageInspectData
                                                                                                       {
                                                                                                           Id = "sha256:local-nginx-image",
                                                                                                           EnvironmentVariables = ["NGINX_VERSION=1.29.1"],
                                                                                                       }));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), "sha256:local-nginx-image", Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.Succeeded([]));
                nginxReleaseMetadataService.GetChannelReleaseAsync("1.29", Arg.Any<CancellationToken>())
                                           .Returns(ExternalOperationResult<NginxChannelReleaseData>.Succeeded(new NginxChannelReleaseData
                                                                                                               {
                                                                                                                   ChannelVersion = "1.29",
                                                                                                                   LatestVersion = new Version(1, 29, 8),
                                                                                                               }));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "company/web",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Succeeded([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.0.0",
                                                                                                                           Digest = "sha256:web",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.Succeeded(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "1.0.0",
                                                                                                        Digest = "sha256:web",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                    }));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        derivedBaseRuntimeDetector,
                                                                        dotNetReleaseMetadataService,
                                                                        nginxReleaseMetadataService,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        logger,
                                                                        optionsMonitor,
                                                                        registryMetadataService,
                                                                        new UpdateDetectionService(optionsMonitor));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var finding = await dbContext.UpdateFindings.SingleAsync(entity => entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate, TestContext.CancellationToken)
                                                            .ConfigureAwait(false);

                Assert.AreEqual("Own image uses an outdated NGINX base runtime",
                                finding.Summary,
                                "Runtime scans must persist a strong warning for own images with an outdated NGINX base runtime");
                Assert.Contains("NGINX 1.29.1",
                                finding.Details ?? string.Empty,
                                "The derived runtime finding must mention the detected NGINX version");
                Assert.Contains("1.29.8",
                                finding.Details ?? string.Empty,
                                "The derived runtime finding must mention the latest channel release");
            }
        }
    }

    /// <summary>
    /// Verify a failing Docker instance scan does not abort the remaining batch items
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncContinuesAfterInstanceScanFailureAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var discoveryContext = database.CreateDbContext();

            await using (discoveryContext.ConfigureAwait(false))
            {
                var failingContext = database.CreateSaveChangesFailingDbContext();

                await using (failingContext.ConfigureAwait(false))
                {
                    var options = new DockerUpdateGuardOptions
                                  {
                                      DockerInstances = [
                                                            new DockerInstanceOptions
                                                            {
                                                                Name = "Production",
                                                                BaseUrl = "https://docker.example.test",
                                                                Enabled = true,
                                                                RequestTimeoutSeconds = 15,
                                                            },
                                                        ],
                                  };
                    var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(options);
                    var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                    var instanceDiscoveryService = new InstanceDiscoveryService(discoveryContext,
                                                                                new TestLogger<InstanceDiscoveryService>(),
                                                                                optionsMonitor);
                    var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                            failingContext,
                                                                            Substitute.For<IDockerInstanceClient>(),
                                                                            Substitute.For<IDerivedBaseRuntimeDetector>(),
                                                                            Substitute.For<IDotNetReleaseMetadataService>(),
                                                                            Substitute.For<INginxReleaseMetadataService>(),
                                                                            new ImageCatalogRepository(failingContext),
                                                                            new ImageReferenceParser(),
                                                                            instanceDiscoveryService,
                                                                            logger,
                                                                            optionsMonitor,
                                                                            Substitute.For<IRegistryMetadataService>(),
                                                                            new UpdateDetectionService(optionsMonitor));

                    failingContext.FailOnSaveChanges = true;

                    await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                      .ConfigureAwait(false);

                    Assert.Contains(entry => entry.EventId.Id == 2042, logger.Entries, "A failing Docker instance scan must be logged so later instances are still attempted");
                    Assert.Contains(entry => entry.EventId.Id == 2072, logger.Entries, "The batch must complete even when individual Docker instance scans fail");
                }
            }
        }
    }

    /// <summary>
    /// Verify cancellation during a Docker instance scan aborts the batch instead of being swallowed
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncRethrowsCancellationFromInstanceScanAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var discoveryContext = database.CreateDbContext();

            await using (discoveryContext.ConfigureAwait(false))
            {
                var failingContext = database.CreateSaveChangesFailingDbContext();

                await using (failingContext.ConfigureAwait(false))
                {
                    var options = new DockerUpdateGuardOptions
                                  {
                                      DockerInstances = [
                                                            new DockerInstanceOptions
                                                            {
                                                                Name = "Production",
                                                                BaseUrl = "https://docker.example.test",
                                                                Enabled = true,
                                                                RequestTimeoutSeconds = 15,
                                                            },
                                                        ],
                                  };
                    var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(options);
                    var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                    var instanceDiscoveryService = new InstanceDiscoveryService(discoveryContext,
                                                                                new TestLogger<InstanceDiscoveryService>(),
                                                                                optionsMonitor);
                    var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                            failingContext,
                                                                            Substitute.For<IDockerInstanceClient>(),
                                                                            Substitute.For<IDerivedBaseRuntimeDetector>(),
                                                                            Substitute.For<IDotNetReleaseMetadataService>(),
                                                                            Substitute.For<INginxReleaseMetadataService>(),
                                                                            new ImageCatalogRepository(failingContext),
                                                                            new ImageReferenceParser(),
                                                                            instanceDiscoveryService,
                                                                            logger,
                                                                            optionsMonitor,
                                                                            Substitute.For<IRegistryMetadataService>(),
                                                                            new UpdateDetectionService(optionsMonitor));

                    failingContext.SaveChangesException = new OperationCanceledException();
                    failingContext.FailOnSaveChanges = true;

                    await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None),
                                                                                "Cancellation during a Docker instance scan must abort the batch instead of being swallowed")
                                .ConfigureAwait(false);

                    Assert.DoesNotContain(entry => entry.EventId.Id == 2042, logger.Entries, "A cancelled Docker instance scan must not be reported as an item failure");
                }
            }
        }
    }

    /// <summary>
    /// Verify an exception while discovering containers reports a fixed reason in the scan run
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncWhenDiscoveryThrowsReportsFixedReasonAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var orchestrator = CreateFailureOrchestrator(dbContext, dockerInstanceClient, registryMetadataService, logger);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(Task.FromException<ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>>(new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (docker.internal.test:2376)")));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var scanRun = await dbContext.ScanRuns.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(ScanRunStatus.Failed, scanRun.Status, "An exception during discovery must fail the scan run");
                Assert.AreEqual("Runtime container scan failed: connection failed",
                                scanRun.ErrorMessage,
                                "The scan run must carry the fixed reason instead of the exception text");
                Assert.IsNotNull(scanRun.ErrorMessage, "A failed result must carry a message");
                Assert.DoesNotContain("internal.test", scanRun.ErrorMessage, "The exception text must not reach the scan run");
            }
        }
    }

    /// <summary>
    /// Verify an exception while processing one container reports a fixed reason in the scan run and in the snapshot
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncWhenContainerProcessingThrowsReportsFixedReasonAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var orchestrator = CreateFailureOrchestrator(dbContext, dockerInstanceClient, registryMetadataService, logger);
                var injected = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (registry.internal.test:443)");

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "web-1",
                                                                                                                                  ImageReference = "docker.io/library/nginx:1.0.0",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(Task.FromException<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>>(injected));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(Task.FromException<ExternalOperationResult<DockerHubTagData>>(injected));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var scanRun = await dbContext.ScanRuns.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var snapshot = await dbContext.ContainerSnapshots.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual("Container 'web-1' could not be processed: connection failed",
                                scanRun.ErrorMessage,
                                "The scan run must carry the fixed reason instead of the exception text");
                Assert.AreEqual("Container 'web-1' could not be processed: connection failed",
                                snapshot.UpdateAssessmentMessage,
                                "The snapshot must carry the fixed reason instead of the exception text");
                Assert.AreEqual(UpdateAssessmentStatus.Failed, snapshot.UpdateAssessmentStatus, "A failed container must be marked as failed");
                Assert.IsNotNull(scanRun.ErrorMessage, "A failed result must carry a message");
                Assert.DoesNotContain("internal.test", scanRun.ErrorMessage, "The exception text must not reach the scan run");
                Assert.IsNotNull(snapshot.UpdateAssessmentMessage, "A failed result must carry a message");
                Assert.DoesNotContain("internal.test", snapshot.UpdateAssessmentMessage, "The exception text must not reach the snapshot");
                Assert.Contains(entry => entry.EventId.Id == 2098 && ReferenceEquals(entry.Exception, injected),
                                logger.Entries,
                                "The failure must still be logged with the exception");
            }
        }
    }

    /// <summary>
    /// Verify a container whose image reference is unparsable or invalid produces no snapshot, no catalog entry and no log entry, and ends the run partial with a fixed message
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncWhenImageReferenceIsInvalidRecordsNoSnapshotAsync()
    {
        string[] imageReferences = [$"docker.io/{new string('a', 256)}:1.0.0", "/"];

        foreach (var imageReference in imageReferences)
        {
            using (var database = new SqliteTestDatabase())
            {
                var dbContext = database.CreateDbContext();

                await using (dbContext.ConfigureAwait(false))
                {
                    var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                    var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                    var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                    var orchestrator = CreateFailureOrchestrator(dbContext, dockerInstanceClient, registryMetadataService, logger);

                    dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                        .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                                  new RuntimeContainerDescriptor
                                                                                                                                  {
                                                                                                                                      ContainerId = "container-1",
                                                                                                                                      Name = "web-1",
                                                                                                                                      ImageReference = imageReference,
                                                                                                                                      RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                      IsRunning = true,
                                                                                                                                  },
                                                                                                                              ]));

                    await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                      .ConfigureAwait(false);

                    var scanRun = await dbContext.ScanRuns.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                    var snapshotCount = await dbContext.ContainerSnapshots.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);
                    var versionCount = await dbContext.ImageVersions.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);
                    var repositoryCount = await dbContext.RegistryRepositories.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);

                    Assert.AreEqual(0, snapshotCount, $"An invalid image reference must produce no snapshot (length {imageReference.Length})");
                    Assert.AreEqual(0, versionCount, $"An invalid image reference must produce no image version (length {imageReference.Length})");
                    Assert.AreEqual(0, repositoryCount, $"An invalid image reference must produce no catalog repository (length {imageReference.Length})");
                    Assert.AreEqual(ScanRunStatus.Partial, scanRun.Status, $"An invalid image reference must end the run partial (length {imageReference.Length})");
                    Assert.AreEqual("Container 'web-1' could not be processed: invalid image reference",
                                    scanRun.ErrorMessage,
                                    $"The run must carry the fixed message (length {imageReference.Length})");
                    Assert.DoesNotContain(entry => entry.EventId.Id == 2098,
                                          logger.Entries,
                                          $"The raw reference must not be written to the log (length {imageReference.Length})");
                    Assert.DoesNotContain(entry => entry.Message.Contains(imageReference, StringComparison.Ordinal),
                                          logger.Entries,
                                          $"No log entry may carry the raw reference (length {imageReference.Length})");
                }
            }
        }
    }

    /// <summary>
    /// Verify a digest outside the digest grammar, from the reference or from the repository digests, leaves the catalog entry without digest
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncWhenImageDigestIsInvalidStoresVersionWithoutDigestAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var orchestrator = CreateFailureOrchestrator(dbContext, dockerInstanceClient, registryMetadataService, logger);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "from-reference",
                                                                                                                                  ImageReference = "docker.io/library/nginx:1.0.0@sha256:ab cd",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-2",
                                                                                                                                  Name = "from-repository-digests",
                                                                                                                                  ImageReference = "docker.io/library/redis:7",
                                                                                                                                  ImageDigest = "sha256:ab cd",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync(Arg.Any<string>(),
                                                     Arg.Any<string>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.NotFound("No tag data"));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.NotFound("No exact tag metadata is available"));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var versions = await dbContext.ImageVersions.ToListAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var snapshots = await dbContext.ContainerSnapshots.ToListAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.HasCount(2, versions, "Both containers must keep their image version despite the invalid digest");
                Assert.HasCount(2, snapshots, "An invalid digest must not remove the snapshots");
                Assert.IsTrue(versions.TrueForAll(version => version.Digest is null), "A digest outside the digest grammar must leave the catalog entry without digest");
            }
        }
    }

    /// <summary>
    /// Verify the fallback messages and their log entries carry the normalized reference instead of the raw one
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncWhenTagResultHasNoMessageUsesNormalizedReferenceAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var orchestrator = CreateFailureOrchestrator(dbContext, dockerInstanceClient, registryMetadataService, logger);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>.Succeeded([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "incomplete",
                                                                                                                                  ImageReference = "  docker.io/library/nginx:1.0.0  ",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-2",
                                                                                                                                  Name = "unsupported",
                                                                                                                                  ImageReference = "  docker.io/library/redis:7  ",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<DockerImageInspectData>.NotFound("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult<IReadOnlyList<DockerImageHistoryEntryData>>.NotFound("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Unknown(null!));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/redis",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Unsupported(null!));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult<DockerHubTagData>.NotFound("No exact tag metadata is available"));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var snapshots = await dbContext.ContainerSnapshots.ToListAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var incomplete = snapshots.Single(entity => entity.Name == "incomplete");
                var unsupported = snapshots.Single(entity => entity.Name == "unsupported");

                Assert.AreEqual("Unable to evaluate runtime image 'docker.io/library/nginx:1.0.0'",
                                incomplete.UpdateAssessmentMessage,
                                "The incomplete-evaluation message must name the normalized reference");
                Assert.AreEqual("Runtime image 'docker.io/library/redis:7' cannot be evaluated by the current registry adapters",
                                unsupported.UpdateAssessmentMessage,
                                "The unsupported-evaluation message must name the normalized reference");
                Assert.Contains(entry => entry.EventId.Id == 2077
                                         && entry.Message.Contains("image docker.io/library/nginx:1.0.0 returned", StringComparison.Ordinal),
                                logger.Entries,
                                "The incomplete-evaluation log entry must carry the normalized reference");
                Assert.Contains(entry => entry.EventId.Id == 2078
                                         && entry.Message.Contains("image docker.io/library/redis:7 is not supported", StringComparison.Ordinal),
                                logger.Entries,
                                "The unsupported-evaluation log entry must carry the normalized reference");
            }
        }
    }

    /// <summary>
    /// Create an orchestrator for a single configured Docker instance
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="dockerInstanceClient">Docker instance client</param>
    /// <param name="registryMetadataService">Registry metadata service</param>
    /// <param name="logger">Logger that captures the entries</param>
    /// <returns>Orchestrator</returns>
    private static RuntimeContainerScanOrchestrator CreateFailureOrchestrator(DockerUpdateGuardDbContext dbContext,
                                                                              IDockerInstanceClient dockerInstanceClient,
                                                                              IRegistryMetadataService registryMetadataService,
                                                                              TestLogger<RuntimeContainerScanOrchestrator> logger)
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
        var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                    new TestLogger<InstanceDiscoveryService>(),
                                                                    optionsMonitor);

        return new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                    dbContext,
                                                    dockerInstanceClient,
                                                    Substitute.For<IDerivedBaseRuntimeDetector>(),
                                                    Substitute.For<IDotNetReleaseMetadataService>(),
                                                    Substitute.For<INginxReleaseMetadataService>(),
                                                    new ImageCatalogRepository(dbContext),
                                                    new ImageReferenceParser(),
                                                    instanceDiscoveryService,
                                                    logger,
                                                    optionsMonitor,
                                                    registryMetadataService,
                                                    new UpdateDetectionService(optionsMonitor));
    }

    #endregion // Methods
}