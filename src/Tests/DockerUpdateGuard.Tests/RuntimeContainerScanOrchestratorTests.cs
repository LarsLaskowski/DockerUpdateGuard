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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
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
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
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
    /// Verify an instance configured with surrounding whitespace in its name is scanned and not skipped
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncWithUntrimmedConfiguredNameScansInstanceAsync()
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
                                                            Name = " Production ",
                                                            BaseUrl = "https://docker.example.test",
                                                            Enabled = true,
                                                            RequestTimeoutSeconds = 15,
                                                        },
                                                    ],
                              };
                var optionsMonitor = PostConfiguredOptions.CreateMonitor(options);
                var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                var instanceDiscoveryService = new InstanceDiscoveryService(dbContext,
                                                                            new TestLogger<InstanceDiscoveryService>(),
                                                                            optionsMonitor);

                dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([]));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
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

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var dockerInstance = await dbContext.DockerInstances.SingleAsync(TestContext.CancellationToken)
                                                                    .ConfigureAwait(false);
                var scanRun = await dbContext.ScanRuns.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual("Production", dockerInstance.Name, "The stored instance must carry the trimmed name");
                Assert.AreEqual(ScanRunType.RuntimeContainer, scanRun.Type, "The instance must get a runtime container scan run");
                Assert.DoesNotContain(entry => entry.EventId.Id == 2074,
                                      logger.Entries,
                                      "The instance must not be skipped for a missing configuration entry");
                await dockerInstanceClient.Received(1)
                                          .DiscoverContainersAsync(Arg.Is<DockerInstanceOptions>(instance => instance.Name == "Production"), Arg.Any<CancellationToken>())
                                          .ConfigureAwait(false);
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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));

                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));

                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
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
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
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
                                                ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.0.0",
                                                                                                                           Digest = "sha256:runtime-old",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
                                                                                                                              new RuntimeContainerDescriptor
                                                                                                                              {
                                                                                                                                  ContainerId = "container-1",
                                                                                                                                  Name = "web",
                                                                                                                                  ImageReference = "docker.io/library/nginx:1.0.0@sha256:runtime-old",
                                                                                                                                  RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                  IsRunning = true,
                                                                                                                              },
                                                                                                                          ]),
                                             ExternalOperationResult.Failed<IReadOnlyList<RuntimeContainerDescriptor>>("The Docker daemon is unreachable"));
                dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
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
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
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
    /// Verify a transient registry evaluation failure keeps existing runtime findings active on the snapshot of the failed scan
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
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
                                                ExternalOperationResult.Failed<IReadOnlyList<DockerHubTagData>>("The registry is temporarily unavailable"));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
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

                var firstSnapshot = await dbContext.ContainerSnapshots.SingleAsync(TestContext.CancellationToken)
                                                                      .ConfigureAwait(false);
                var firstFindings = await dbContext.UpdateFindings.ToListAsync(TestContext.CancellationToken)
                                                                  .ConfigureAwait(false);
                var firstCandidateCount = await dbContext.TagCandidates.CountAsync(TestContext.CancellationToken)
                                                                       .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                dbContext.ChangeTracker.Clear();

                var secondSnapshot = await dbContext.ContainerSnapshots.SingleAsync(entity => entity.Id != firstSnapshot.Id, TestContext.CancellationToken)
                                                                       .ConfigureAwait(false);
                var findings = await dbContext.UpdateFindings.ToListAsync(TestContext.CancellationToken)
                                                             .ConfigureAwait(false);
                var secondCandidateCount = await dbContext.TagCandidates.CountAsync(TestContext.CancellationToken)
                                                                        .ConfigureAwait(false);

                Assert.HasCount(1,
                                firstFindings,
                                "The first scan must create the runtime finding");
                Assert.IsGreaterThan(0,
                                     firstCandidateCount,
                                     "The first scan must create tag candidates for the runtime finding");
                Assert.HasCount(1,
                                findings,
                                "A transient registry evaluation failure must not create or remove runtime findings");
                Assert.AreEqual(firstFindings[0].Id,
                                findings[0].Id,
                                "A transient registry evaluation failure must keep the previous finding");
                Assert.IsTrue(findings[0].IsActive,
                              "A transient registry evaluation failure must keep the existing runtime finding active instead of flickering it away");
                Assert.IsNull(findings[0].ResolvedAtUtc,
                              "A transient registry evaluation failure must leave the kept finding without a resolution time");
                Assert.AreEqual(secondSnapshot.Id,
                                findings[0].ContainerSnapshotId,
                                "The kept finding must move to the snapshot recorded by the failed scan");
                Assert.AreEqual(firstCandidateCount,
                                secondCandidateCount,
                                "The kept finding must keep its tag candidates");
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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Unsupported<IReadOnlyList<DockerHubTagData>>("Registry adapters cannot evaluate this registry"));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.NotFound<DockerHubTagData>("No exact tag metadata is available"));

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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
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
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "networlddev/f1-telemetry",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
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
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "linuxserver/heimdall",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
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
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/phpmyadmin",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
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
                                                ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
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
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
                                                                                                    {
                                                                                                        Tag = "latest",
                                                                                                        Digest = "sha256:old",
                                                                                                        PublishedAtUtc = new DateTimeOffset(2026, 07, 03, 10, 00, 00, TimeSpan.Zero),
                                                                                                    }),
                                                ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "networlddev/f1-telemetry",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
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
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.Succeeded<DockerImageInspectData>(new DockerImageInspectData
                                                                                                       {
                                                                                                           Id = "sha256:local-arm-image",
                                                                                                           OperatingSystem = "linux",
                                                                                                           Architecture = "arm64",
                                                                                                       }));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), "sha256:local-arm-image", Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerImageHistoryEntryData>>([]));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "networlddev/f1-telemetry",
                                                     "linux",
                                                     "arm64",
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
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
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("mcr.microsoft.com",
                                                     "dotnet/runtime",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
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
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
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
                                    .Returns(ExternalOperationResult.Succeeded<DockerImageInspectData>(new DockerImageInspectData
                                                                                                       {
                                                                                                           Id = "sha256:local-image",
                                                                                                           EnvironmentVariables = ["DOTNET_VERSION=9.0.13"],
                                                                                                       }));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), "sha256:local-image", Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerImageHistoryEntryData>>([]));
                dotNetReleaseMetadataService.GetChannelReleaseAsync("9.0", Arg.Any<CancellationToken>())
                                            .Returns(ExternalOperationResult.Succeeded<DotNetChannelReleaseData>(new DotNetChannelReleaseData
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
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.0.0",
                                                                                                                           Digest = "sha256:current",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.Succeeded<DockerImageInspectData>(new DockerImageInspectData
                                                                                                       {
                                                                                                           Id = "sha256:local-nginx-image",
                                                                                                           EnvironmentVariables = ["NGINX_VERSION=1.29.1"],
                                                                                                       }));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), "sha256:local-nginx-image", Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerImageHistoryEntryData>>([]));
                nginxReleaseMetadataService.GetChannelReleaseAsync("1.29", Arg.Any<CancellationToken>())
                                           .Returns(ExternalOperationResult.Succeeded<NginxChannelReleaseData>(new NginxChannelReleaseData
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
                                       .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<DockerHubTagData>>([
                                                                                                                       new DockerHubTagData
                                                                                                                       {
                                                                                                                           Tag = "1.0.0",
                                                                                                                           Digest = "sha256:web",
                                                                                                                           PublishedAtUtc = new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                                                       },
                                                                                                                   ]));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Succeeded<DockerHubTagData>(new DockerHubTagData
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
    /// Verify a failing Docker instance scan does not abort the remaining batch items
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
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

                    var recoveryFailures = logger.Entries.Where(entry => entry.EventId.Id == 2043).ToList();

                    Assert.HasCount(1, recoveryFailures, "A failed recovery save must be logged for the instance");
                    Assert.AreEqual(LogLevel.Error, recoveryFailures[0].LogLevel, "A failed recovery save must be logged as an error");
                }
            }
        }
    }

    /// <summary>
    /// Verify a batch scan still scans the remaining Docker instance after an earlier instance fails before its scan body
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncScansRemainingInstanceAfterEarlierInstanceFailsBeforeScanAsync()
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
                                                            new DockerInstanceOptions
                                                            {
                                                                Name = "Staging",
                                                                BaseUrl = "https://staging.example.test",
                                                                Enabled = true,
                                                                RequestTimeoutSeconds = 15,
                                                            },
                                                        ],
                                  };
                    var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(options);
                    var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                    var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                    var instanceDiscoveryService = new InstanceDiscoveryService(discoveryContext,
                                                                                new TestLogger<InstanceDiscoveryService>(),
                                                                                optionsMonitor);

                    dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                        .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([]));

                    var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                            failingContext,
                                                                            dockerInstanceClient,
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

                    failingContext.FailOnSaveAttempt = 1;

                    await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                      .ConfigureAwait(false);

                    failingContext.ChangeTracker.Clear();

                    var itemFailures = logger.Entries.Count(entry => entry.EventId.Id == 2042);
                    var succeededRuns = await failingContext.ScanRuns.Where(entity => entity.Type == ScanRunType.RuntimeContainer
                                                                                      && entity.Status == ScanRunStatus.Succeeded
                                                                                      && entity.CompletedAtUtc != null)
                                                                     .CountAsync(TestContext.CancellationToken)
                                                                     .ConfigureAwait(false);

                    Assert.AreEqual(1, itemFailures, "Exactly the instance whose first save failed must be logged as a batch item failure");
                    Assert.AreEqual(1, succeededRuns, "The remaining instance must be scanned to completion after the earlier instance failed");
                    Assert.Contains(entry => entry.EventId.Id == 2072, logger.Entries, "The batch must complete after an instance failed before its scan body");

                    var allRuns = await failingContext.ScanRuns.Where(entity => entity.Type == ScanRunType.RuntimeContainer)
                                                               .ToListAsync(TestContext.CancellationToken)
                                                               .ConfigureAwait(false);
                    var failedRuns = allRuns.Where(entity => entity.Status == ScanRunStatus.Failed).ToList();

                    Assert.HasCount(2, allRuns, "The run of the failed first save must be stored besides the later run");
                    Assert.DoesNotContain(entity => entity.Status == ScanRunStatus.Running, allRuns, "No run of the batch may stay running");
                    Assert.HasCount(1, failedRuns, "The run of the failed first save must be stored as failed");
                    Assert.IsNotNull(failedRuns[0].CompletedAtUtc, "The failed run must carry a completion time");
                    Assert.AreEqual("Runtime container scan could not be saved: unexpected error", failedRuns[0].ErrorMessage, "The failed run must carry the save failure line");

                    await dockerInstanceClient.Received(1)
                                              .DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                              .ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Verify cancellation during a Docker instance scan aborts the batch instead of being swallowed
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
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
                    Assert.AreEqual(1, failingContext.SaveChangesAttemptCount, "A cancellation must be rethrown without a recovery save");
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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
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
                                        .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync(Arg.Any<string>(),
                                                     Arg.Any<string>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerHubTagData>>("No tag data"));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.NotFound<DockerHubTagData>("No exact tag metadata is available"));

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);
                dbContext.ChangeTracker.Clear();

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
                                    .Returns(ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([
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
                                    .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                dockerInstanceClient.GetImageHistoryAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                    .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerImageHistoryEntryData>>("The local image history payload is not available"));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/nginx",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Unknown<IReadOnlyList<DockerHubTagData>>(null!));
                registryMetadataService.GetTagsAsync("docker.io",
                                                     "library/redis",
                                                     Arg.Any<string?>(),
                                                     Arg.Any<string?>(),
                                                     Arg.Any<RegistryTagQueryOptions?>(),
                                                     Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.Unsupported<IReadOnlyList<DockerHubTagData>>(null!));
                registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                       .Returns(ExternalOperationResult.NotFound<DockerHubTagData>("No exact tag metadata is available"));

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
    /// Verify two concurrent scans of the same Docker instance run one after the other, the second creating no scan run before the first has finished
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncConcurrentScansOfSameInstanceRunSequentiallyAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var firstContext = database.CreateDbContext();

            await using (firstContext.ConfigureAwait(false))
            {
                var secondContext = database.CreateDbContext();

                await using (secondContext.ConfigureAwait(false))
                {
                    var discoveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var releaseDiscovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var dockerInstanceClient = CreateGatedDockerInstanceClient(discoveryStarted, releaseDiscovery.Task);
                    var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                    var firstOrchestrator = CreateFailureOrchestrator(firstContext, dockerInstanceClient, registryMetadataService, new TestLogger<RuntimeContainerScanOrchestrator>());
                    var secondOrchestrator = CreateFailureOrchestrator(secondContext, dockerInstanceClient, registryMetadataService, new TestLogger<RuntimeContainerScanOrchestrator>());

                    var firstRun = firstOrchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, TestContext.CancellationToken);

                    await discoveryStarted.Task.WaitAsync(TestContext.CancellationToken)
                                               .ConfigureAwait(false);

                    var instanceId = await ReadInstanceIdAsync(database, TestContext.CancellationToken).ConfigureAwait(false);
                    var secondRun = secondOrchestrator.ScanAllAsync(ScanTriggerSource.Manual, TestContext.CancellationToken);

                    await ScanLockObserver.WaitUntilWaitingAsync(RuntimeContainerScanOrchestrator.DockerInstanceScanLocks, instanceId, secondRun, TestContext.CancellationToken)
                                          .ConfigureAwait(false);

                    var runCountWhileWaiting = await CountRuntimeScanRunsAsync(database, TestContext.CancellationToken).ConfigureAwait(false);

                    Assert.IsFalse(secondRun.IsCompleted, "The second scan of the instance must wait while the first one runs");
                    Assert.AreEqual(1, runCountWhileWaiting, "The waiting scan must not have created its scan run");
                    _ = dockerInstanceClient.Received(1).DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>());

                    releaseDiscovery.TrySetResult();

                    await Task.WhenAll(firstRun, secondRun)
                              .WaitAsync(TestContext.CancellationToken)
                              .ConfigureAwait(false);

                    var runCountAfterBoth = await CountRuntimeScanRunsAsync(database, TestContext.CancellationToken).ConfigureAwait(false);

                    Assert.AreEqual(2, runCountAfterBoth, "Once the first scan has finished, the second one must run in full with its own scan run");
                    _ = dockerInstanceClient.Received(2).DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>());
                    Assert.AreEqual(0,
                                    RuntimeContainerScanOrchestrator.DockerInstanceScanLocks.GetReferenceCount(instanceId),
                                    "No reference of the instance may remain once both scans have finished");
                }
            }
        }
    }

    /// <summary>
    /// Verify a waiting scan of a Docker instance that is cancelled does no work and ends with an operation-cancelled exception
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncWaitingScanCancelledDoesNotRunAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var firstContext = database.CreateDbContext();

            await using (firstContext.ConfigureAwait(false))
            {
                var secondContext = database.CreateDbContext();

                await using (secondContext.ConfigureAwait(false))
                {
                    var discoveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var releaseDiscovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var dockerInstanceClient = CreateGatedDockerInstanceClient(discoveryStarted, releaseDiscovery.Task);
                    var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                    var firstOrchestrator = CreateFailureOrchestrator(firstContext, dockerInstanceClient, registryMetadataService, new TestLogger<RuntimeContainerScanOrchestrator>());
                    var secondOrchestrator = CreateFailureOrchestrator(secondContext, dockerInstanceClient, registryMetadataService, new TestLogger<RuntimeContainerScanOrchestrator>());

                    var firstRun = firstOrchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, TestContext.CancellationToken);

                    await discoveryStarted.Task.WaitAsync(TestContext.CancellationToken)
                                               .ConfigureAwait(false);

                    var instanceId = await ReadInstanceIdAsync(database, TestContext.CancellationToken).ConfigureAwait(false);

                    using (var waitCancellation = new CancellationTokenSource())
                    {
                        var secondRun = secondOrchestrator.ScanAllAsync(ScanTriggerSource.Manual, waitCancellation.Token);

                        await ScanLockObserver.WaitUntilWaitingAsync(RuntimeContainerScanOrchestrator.DockerInstanceScanLocks, instanceId, secondRun, TestContext.CancellationToken)
                                              .ConfigureAwait(false);
                        await waitCancellation.CancelAsync()
                                              .ConfigureAwait(false);
                        await Assert.ThrowsAsync<OperationCanceledException>(() => secondRun, "A waiting scan that is cancelled must end with an OperationCanceledException");
                    }

                    var runCountAfterCancel = await CountRuntimeScanRunsAsync(database, TestContext.CancellationToken).ConfigureAwait(false);

                    Assert.AreEqual(1, runCountAfterCancel, "The cancelled waiting scan must not have created a scan run");
                    _ = dockerInstanceClient.Received(1).DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>());
                    Assert.AreEqual(1,
                                    RuntimeContainerScanOrchestrator.DockerInstanceScanLocks.GetReferenceCount(instanceId),
                                    "Only the running scan may hold a reference after the wait was cancelled");

                    releaseDiscovery.TrySetResult();

                    await firstRun.WaitAsync(TestContext.CancellationToken)
                                  .ConfigureAwait(false);

                    Assert.AreEqual(0,
                                    RuntimeContainerScanOrchestrator.DockerInstanceScanLocks.GetReferenceCount(instanceId),
                                    "No reference of the instance may remain once the running scan has finished");
                }
            }
        }
    }

    /// <summary>
    /// Verify scans of different Docker instances may run at the same time
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncConcurrentScansOfDifferentInstancesMayOverlapAsync()
    {
        using (var firstDatabase = new SqliteTestDatabase())
        {
            using (var secondDatabase = new SqliteTestDatabase())
            {
                var firstContext = firstDatabase.CreateDbContext();

                await using (firstContext.ConfigureAwait(false))
                {
                    var secondContext = secondDatabase.CreateDbContext();

                    await using (secondContext.ConfigureAwait(false))
                    {
                        var discoveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var releaseDiscovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var dockerInstanceClient = CreateGatedDockerInstanceClient(discoveryStarted, releaseDiscovery.Task);
                        var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                        var firstOrchestrator = CreateFailureOrchestrator(firstContext, dockerInstanceClient, registryMetadataService, new TestLogger<RuntimeContainerScanOrchestrator>());
                        var secondOrchestrator = CreateFailureOrchestrator(secondContext, dockerInstanceClient, registryMetadataService, new TestLogger<RuntimeContainerScanOrchestrator>());

                        var firstRun = firstOrchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, TestContext.CancellationToken);

                        await discoveryStarted.Task.WaitAsync(TestContext.CancellationToken)
                                                   .ConfigureAwait(false);

                        var firstInstanceId = await ReadInstanceIdAsync(firstDatabase, TestContext.CancellationToken).ConfigureAwait(false);
                        var secondRun = secondOrchestrator.ScanAllAsync(ScanTriggerSource.Manual, TestContext.CancellationToken);

                        await secondRun.WaitAsync(TestContext.CancellationToken)
                                       .ConfigureAwait(false);

                        var secondInstanceId = await ReadInstanceIdAsync(secondDatabase, TestContext.CancellationToken).ConfigureAwait(false);

                        Assert.AreNotEqual(firstInstanceId, secondInstanceId, "The two runs must scan different Docker instances");
                        Assert.IsFalse(firstRun.IsCompleted, "The first scan must still run while the scan of the other instance has finished");
                        Assert.AreEqual(1,
                                        RuntimeContainerScanOrchestrator.DockerInstanceScanLocks.GetReferenceCount(firstInstanceId),
                                        "Only the first scan may hold a reference of its instance");
                        Assert.AreEqual(1,
                                        await CountRuntimeScanRunsAsync(secondDatabase, TestContext.CancellationToken).ConfigureAwait(false),
                                        "The scan of the other instance must have run in full while the first one was running");

                        releaseDiscovery.TrySetResult();

                        await firstRun.WaitAsync(TestContext.CancellationToken)
                                      .ConfigureAwait(false);

                        Assert.AreEqual(0,
                                        RuntimeContainerScanOrchestrator.DockerInstanceScanLocks.GetReferenceCount(firstInstanceId),
                                        "No reference of the first instance may remain once its scan has finished");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Verify a failed final save of one instance discards its changes, ends its run as failed and leaves the next instance unaffected
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    [TestCategory("AC3")]
    public async Task RuntimeContainerScanOrchestratorScanAllAsyncDiscardsFailedInstanceChangesBeforeNextInstanceAsync()
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
                                                            new DockerInstanceOptions
                                                            {
                                                                Name = "Staging",
                                                                BaseUrl = "https://staging.example.test",
                                                                Enabled = true,
                                                                RequestTimeoutSeconds = 15,
                                                            },
                                                        ],
                                  };
                    var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(options);
                    var logger = new TestLogger<RuntimeContainerScanOrchestrator>();
                    var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
                    var registryMetadataService = Substitute.For<IRegistryMetadataService>();
                    var instanceDiscoveryService = new InstanceDiscoveryService(discoveryContext,
                                                                                new TestLogger<InstanceDiscoveryService>(),
                                                                                optionsMonitor);

                    dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                                        .Returns(callInfo => ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>(callInfo.Arg<DockerInstanceOptions>().Name == "Production"
                                                                                                                                              ? [
                                                                                                                                                    new RuntimeContainerDescriptor
                                                                                                                                                    {
                                                                                                                                                        ContainerId = "container-1",
                                                                                                                                                        Name = "web",
                                                                                                                                                        ImageReference = "docker.io/library/nginx:1.0.0@sha256:runtime-old",
                                                                                                                                                        RuntimeStatus = ContainerRuntimeStatus.Running,
                                                                                                                                                        IsRunning = true,
                                                                                                                                                    },
                                                                                                                                                ]
                                                                                                                                              : []));
                    dockerInstanceClient.InspectImageAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                                        .Returns(ExternalOperationResult.NotFound<DockerImageInspectData>("The local image inspect payload is not available"));
                    registryMetadataService.GetTagAsync(Arg.Any<ImageReference>(), cancellationToken: Arg.Any<CancellationToken>())
                                           .Returns(ExternalOperationResult.NotFound<DockerHubTagData>("No tag"));
                    registryMetadataService.GetTagsAsync(Arg.Any<string>(),
                                                         Arg.Any<string>(),
                                                         Arg.Any<string?>(),
                                                         Arg.Any<string?>(),
                                                         Arg.Any<RegistryTagQueryOptions?>(),
                                                         Arg.Any<CancellationToken>())
                                           .Returns(ExternalOperationResult.NotFound<IReadOnlyList<DockerHubTagData>>("No tags"));

                    var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                            failingContext,
                                                                            dockerInstanceClient,
                                                                            Substitute.For<IDerivedBaseRuntimeDetector>(),
                                                                            Substitute.For<IDotNetReleaseMetadataService>(),
                                                                            Substitute.For<INginxReleaseMetadataService>(),
                                                                            new ImageCatalogRepository(failingContext),
                                                                            new ImageReferenceParser(),
                                                                            instanceDiscoveryService,
                                                                            logger,
                                                                            optionsMonitor,
                                                                            registryMetadataService,
                                                                            new UpdateDetectionService(optionsMonitor));

                    failingContext.FailWhen = tracker => tracker.Entries<ContainerSnapshot>()
                                                                .Any(entry => entry.State == EntityState.Added);

                    await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                      .ConfigureAwait(false);

                    var readContext = database.CreateDbContext();

                    await using (readContext.ConfigureAwait(false))
                    {
                        var runs = await readContext.ScanRuns.Include(entity => entity.DockerInstance)
                                                             .Where(entity => entity.Type == ScanRunType.RuntimeContainer)
                                                             .ToListAsync(TestContext.CancellationToken)
                                                             .ConfigureAwait(false);
                        var productionRun = runs.SingleOrDefault(entity => entity.DockerInstance!.Name == "Production");
                        var stagingRun = runs.SingleOrDefault(entity => entity.DockerInstance!.Name == "Staging");
                        var snapshotCount = await readContext.ContainerSnapshots.CountAsync(TestContext.CancellationToken)
                                                                                .ConfigureAwait(false);

                        Assert.IsNotNull(productionRun, "The failed instance must have exactly one stored run");
                        Assert.IsNotNull(stagingRun, "The later instance must have exactly one stored run");
                        Assert.AreEqual(ScanRunStatus.Failed, productionRun.Status, "A run whose final save failed must end as failed");
                        Assert.IsNotNull(productionRun.CompletedAtUtc, "The failed run must carry a completion time");
                        Assert.EndsWith("Runtime container scan could not be saved: unexpected error",
                                        productionRun.ErrorMessage ?? string.Empty,
                                        "The failed run must end with the save failure line");
                        Assert.AreEqual(0, snapshotCount, "No container snapshot of the failed run may be stored");
                        Assert.AreEqual(ScanRunStatus.Succeeded, stagingRun.Status, "The later instance must be scanned to completion");
                        Assert.DoesNotContain(entity => entity.Status == ScanRunStatus.Running, runs, "No run of the batch may stay running");
                        Assert.ContainsSingle(entry => entry.EventId.Id == 2042, logger.Entries, "Only the failed instance must be logged as a batch item failure");
                    }
                }
            }
        }
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
    /// Create a Docker instance client whose first container discovery signals and waits for a release
    /// </summary>
    /// <param name="discoveryStarted">Completed when the first discovery has started</param>
    /// <param name="releaseDiscovery">Task that lets the first discovery return</param>
    /// <returns>Docker instance client</returns>
    private static IDockerInstanceClient CreateGatedDockerInstanceClient(TaskCompletionSource discoveryStarted, Task releaseDiscovery)
    {
        var dockerInstanceClient = Substitute.For<IDockerInstanceClient>();
        var discoveryCalls = 0;

        dockerInstanceClient.DiscoverContainersAsync(Arg.Any<DockerInstanceOptions>(), Arg.Any<CancellationToken>())
                            .Returns(_ => DiscoverGatedAsync(Interlocked.Increment(ref discoveryCalls) == 1, discoveryStarted, releaseDiscovery));

        return dockerInstanceClient;
    }

    /// <summary>
    /// Report no containers, the first call signalling its start and waiting for the release
    /// </summary>
    /// <param name="isFirstCall">Whether this is the first discovery</param>
    /// <param name="discoveryStarted">Completed when the first discovery has started</param>
    /// <param name="releaseDiscovery">Task that lets the first discovery return</param>
    /// <returns>Discovery result without containers</returns>
    private static async Task<ExternalOperationResult<IReadOnlyList<RuntimeContainerDescriptor>>> DiscoverGatedAsync(bool isFirstCall,
                                                                                                                     TaskCompletionSource discoveryStarted,
                                                                                                                     Task releaseDiscovery)
    {
        if (isFirstCall)
        {
            discoveryStarted.TrySetResult();

            await releaseDiscovery.ConfigureAwait(false);
        }

        return ExternalOperationResult.Succeeded<IReadOnlyList<RuntimeContainerDescriptor>>([]);
    }

    /// <summary>
    /// Read the identifier of the only Docker instance of the database through a fresh context
    /// </summary>
    /// <param name="database">Test database</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Docker instance identifier</returns>
    private static async Task<Guid> ReadInstanceIdAsync(SqliteTestDatabase database, CancellationToken cancellationToken)
    {
        var readContext = database.CreateDbContext();

        await using (readContext.ConfigureAwait(false))
        {
            var dockerInstance = await readContext.DockerInstances.AsNoTracking()
                                                                  .SingleAsync(cancellationToken)
                                                                  .ConfigureAwait(false);

            return dockerInstance.Id;
        }
    }

    /// <summary>
    /// Count the runtime container scan runs of the database through a fresh context
    /// </summary>
    /// <param name="database">Test database</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of runtime container scan runs</returns>
    private static async Task<int> CountRuntimeScanRunsAsync(SqliteTestDatabase database, CancellationToken cancellationToken)
    {
        var readContext = database.CreateDbContext();

        await using (readContext.ConfigureAwait(false))
        {
            return await readContext.ScanRuns.CountAsync(entity => entity.Type == ScanRunType.RuntimeContainer, cancellationToken)
                                             .ConfigureAwait(false);
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
                                                                        new UpdateDetectionService(optionsMonitor));

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
                                                                        new UpdateDetectionService(optionsMonitor));

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