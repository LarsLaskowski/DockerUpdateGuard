using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Queries;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.UI;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ApplicationViewService"/>
/// </summary>
[TestClass]
public class ApplicationViewServiceTests
{
    #region Properties

    /// <summary>
    /// Test context providing information about and functionality for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify runtime container view data exposes Portainer availability per instance
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceRuntimeContainersExposePortainerAvailabilityAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var enabledImageVersionTask = imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                              "company/api",
                                                                                              "1.0.0",
                                                                                              "sha256:api",
                                                                                              cancellationToken: CancellationToken.None);
            var enabledImageVersion = await enabledImageVersionTask.ConfigureAwait(false);
            var disabledImageVersionTask = imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                               "company/worker",
                                                                                               "2.0.0",
                                                                                               "sha256:worker",
                                                                                               cancellationToken: CancellationToken.None);
            var disabledImageVersion = await disabledImageVersionTask.ConfigureAwait(false);
            var enabledInstance = new DockerInstance
                                  {
                                      Name = "With Portainer",
                                      EndpointUri = "https://docker-a.example.test",
                                      ConnectionKind = DockerConnectionKind.Https,
                                      PortainerEndpoint = new PortainerEndpoint
                                                          {
                                                              Name = "Portainer A",
                                                              BaseUrl = "https://portainer-a.example.test",
                                                              IsEnabled = true,
                                                          },
                                  };
            var disabledInstance = new DockerInstance
                                   {
                                       Name = "Without Portainer",
                                       EndpointUri = "https://docker-b.example.test",
                                       ConnectionKind = DockerConnectionKind.Https,
                                       PortainerEndpoint = new PortainerEndpoint
                                                           {
                                                               Name = "Portainer B",
                                                               BaseUrl = "https://portainer-b.example.test",
                                                               IsEnabled = false,
                                                           },
                                   };

            dbContext.DockerInstances.AddRange(enabledInstance, disabledInstance);
            dbContext.ContainerSnapshots.AddRange(new ContainerSnapshot
                                                  {
                                                      DockerInstance = enabledInstance,
                                                      ImageVersionId = enabledImageVersion.Id,
                                                      ContainerId = "container-a",
                                                      Name = "api",
                                                      Status = ContainerRuntimeStatus.Running,
                                                      IsRunning = true,
                                                  },
                                                  new ContainerSnapshot
                                                  {
                                                      DockerInstance = disabledInstance,
                                                      ImageVersionId = disabledImageVersion.Id,
                                                      ContainerId = "container-b",
                                                      Name = "worker",
                                                      Status = ContainerRuntimeStatus.Running,
                                                      IsRunning = true,
                                                  });

            await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     new SharedBaseImageQueryService(dbContext));

            var runtimeContainersTask = service.GetRuntimeContainersAsync(CancellationToken.None);
            var runtimeContainers = await runtimeContainersTask.ConfigureAwait(false);
            var withPortainer = runtimeContainers.Single(entity => entity.DockerInstanceName == "With Portainer");
            var withoutPortainer = runtimeContainers.Single(entity => entity.DockerInstanceName == "Without Portainer");

            Assert.IsTrue(withPortainer.PortainerAvailable, "Runtime containers must surface Portainer availability when the instance endpoint is enabled");
            Assert.IsFalse(withoutPortainer.PortainerAvailable, "Runtime containers must hide Portainer availability when the instance endpoint is disabled");
        }
    }

    /// <summary>
    /// Verify dashboard queries complete successfully and expose aggregate counts
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceDashboardReturnsAggregateCountsAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var imageVersionTask = imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                       "company/api",
                                                                                       "1.0.0",
                                                                                       "sha256:api",
                                                                                       cancellationToken: CancellationToken.None);
            var imageVersion = await imageVersionTask.ConfigureAwait(false);
            var observedImage = new ObservedImage
                                {
                                    Name = "Company API",
                                    CurrentImageVersionId = imageVersion.Id,
                                };
            var dockerInstance = new DockerInstance
                                 {
                                     Name = "Production Engine",
                                     EndpointUri = "https://docker.example.test",
                                     ConnectionKind = DockerConnectionKind.Https,
                                 };
            var scanRun = new ScanRun
                          {
                              Type = ScanRunType.ObservedImage,
                              Status = ScanRunStatus.Succeeded,
                              TriggerSource = ScanTriggerSource.Manual,
                              ObservedImage = observedImage,
                          };

            dbContext.ObservedImages.Add(observedImage);
            dbContext.DockerInstances.Add(dockerInstance);
            dbContext.ScanRuns.Add(scanRun);
            dbContext.ContainerSnapshots.Add(new ContainerSnapshot
                                             {
                                                 DockerInstance = dockerInstance,
                                                 ImageVersionId = imageVersion.Id,
                                                 ScanRun = scanRun,
                                                 ContainerId = "container-a",
                                                 Name = "api",
                                                 Status = ContainerRuntimeStatus.Running,
                                                 IsRunning = true,
                                             });
            dbContext.UpdateFindings.Add(new UpdateFinding
                                         {
                                             ObservedImage = observedImage,
                                             SubjectImageVersionId = imageVersion.Id,
                                             ScanRun = scanRun,
                                             Type = UpdateFindingType.BaseImageUpdate,
                                             Summary = "Update available",
                                             IsActive = true,
                                         });
            dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                                {
                                                    ImageVersionId = imageVersion.Id,
                                                    ScanRun = scanRun,
                                                    AdvisoryId = "CVE-2026-0001",
                                                    Title = "Sample vulnerability",
                                                    Severity = VulnerabilitySeverity.High,
                                                    Source = VulnerabilitySource.Trivy,
                                                    IsActive = true,
                                                });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     new SharedBaseImageQueryService(dbContext));
            var dashboard = await service.GetDashboardAsync(CancellationToken.None)
                                         .ConfigureAwait(false);

            Assert.AreEqual(1,
                            dashboard.ObservedImageCount,
                            "The dashboard must report the observed image count");
            Assert.AreEqual(1,
                            dashboard.DockerInstanceCount,
                            "The dashboard must report the Docker instance count");
            Assert.AreEqual(1,
                            dashboard.RuntimeContainerCount,
                            "The dashboard must report the runtime container count");
            Assert.AreEqual(1,
                            dashboard.ActiveUpdateFindingCount,
                            "The dashboard must report the active update finding count");
            Assert.AreEqual(1,
                            dashboard.ActiveVulnerabilityFindingCount,
                            "The dashboard must report the active vulnerability finding count");
            Assert.AreEqual(1,
                            dashboard.RecentScans.Count,
                            "The dashboard must include the recent scan entry");
        }
    }

    #endregion // Methods
}