using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Infrastructure;

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
                var dockerHubClient = Substitute.For<IDockerHubClient>();
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
                dockerHubClient.GetTagsAsync("docker.io",
                                             "library/nginx",
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

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        dockerHubClient,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        logger,
                                                                        optionsMonitor,
                                                                        new UpdateDetectionService());

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var dockerInstance = await dbContext.DockerInstances.SingleAsync()
                                                                    .ConfigureAwait(false);
                var scanRun = await dbContext.ScanRuns.SingleAsync().ConfigureAwait(false);
                var snapshot = await dbContext.ContainerSnapshots.SingleAsync().ConfigureAwait(false);
                var finding = await dbContext.UpdateFindings.SingleAsync().ConfigureAwait(false);
                var runtimeImageVersionTask = dbContext.ImageVersions.SingleAsync(entity => entity.Id == snapshot.ImageVersionId);
                var runtimeImageVersion = await runtimeImageVersionTask.ConfigureAwait(false);
                Assert.IsNotNull(finding.RecommendedImageVersionId, "Runtime update findings must persist the recommended image version");

                var recommendedImageTask = dbContext.ImageVersions.SingleAsync(entity => entity.Id == finding.RecommendedImageVersionId.Value);
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
                var imageRelationshipCount = await dbContext.ImageRelationships.CountAsync().ConfigureAwait(false);

                Assert.AreEqual(0,
                                imageRelationshipCount,
                                "Runtime scans must not create observed-image base relationships");
                Assert.IsTrue(instanceDiscoveryLogger.Entries.Any(entry => entry.EventId.Id == 2090), "Runtime scans must log the start of Docker instance synchronization");
                Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 2070), "Runtime scan batches must log when batch processing starts");
                Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 2072
                                                          && entry.Message.Contains("processing 1 Docker instances", StringComparison.Ordinal)),
                              "Runtime scan batches must log a completion summary");
                Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 2073
                                                          && entry.Message.Contains("Production", StringComparison.Ordinal)),
                              "Runtime scans must log when each Docker instance scan starts");
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
                var dockerHubClient = Substitute.For<IDockerHubClient>();
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
                dockerHubClient.GetTagsAsync("docker.io",
                                             "library/nginx",
                                             Arg.Any<CancellationToken>())
                               .Returns(ExternalOperationResult<IReadOnlyList<DockerHubTagData>>.Unsupported("Docker Hub cannot evaluate this registry"));

                var orchestrator = new RuntimeContainerScanOrchestrator(new ApplicationTelemetry(),
                                                                        dbContext,
                                                                        dockerInstanceClient,
                                                                        dockerHubClient,
                                                                        imageCatalogRepository,
                                                                        new ImageReferenceParser(),
                                                                        instanceDiscoveryService,
                                                                        logger,
                                                                        optionsMonitor,
                                                                        new UpdateDetectionService());

                await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, CancellationToken.None)
                                  .ConfigureAwait(false);

                var scanRun = await dbContext.ScanRuns.SingleAsync().ConfigureAwait(false);

                Assert.AreEqual(ScanRunStatus.Partial,
                                scanRun.Status,
                                "Unsupported registry evaluations must keep the runtime scan in a partial state");
                Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 2078
                                                          && entry.LogLevel == LogLevel.Warning),
                              "Unsupported runtime registry evaluations must be logged as warnings");
                Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 2041
                                                          && entry.Message.Contains("Partial", StringComparison.Ordinal)),
                              "Runtime scans with unsupported registry evaluations must log a partial completion summary");
            }
        }
    }

    #endregion // Methods
}