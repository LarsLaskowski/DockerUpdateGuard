using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Queries;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.UI;
using DockerUpdateGuard.Vulnerabilities;

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
    /// Test context providing information about and functionality for the current test run
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Create an options monitor with the default application options
    /// </summary>
    /// <returns>Options monitor</returns>
    private static TestOptionsMonitor<DockerUpdateGuardOptions> CreateOptionsMonitor()
    {
        return new TestOptionsMonitor<DockerUpdateGuardOptions>(new DockerUpdateGuardOptions());
    }

    /// <summary>
    /// Create an options monitor with an explicit vulnerability configuration
    /// </summary>
    /// <param name="enabled">Whether vulnerability scanning is enabled</param>
    /// <param name="provider">Configured vulnerability provider</param>
    /// <param name="refreshIntervalMinutes">Vulnerability refresh interval in minutes</param>
    /// <returns>Options monitor</returns>
    private static TestOptionsMonitor<DockerUpdateGuardOptions> CreateOptionsMonitor(bool enabled,
                                                                                     VulnerabilityProviderKind provider,
                                                                                     int refreshIntervalMinutes = 180)
    {
        return new TestOptionsMonitor<DockerUpdateGuardOptions>(new DockerUpdateGuardOptions
                                                                {
                                                                    Vulnerabilities = new VulnerabilityOptions
                                                                                      {
                                                                                          Enabled = enabled,
                                                                                          Provider = provider,
                                                                                      },
                                                                    Scanning = new ScanningOptions
                                                                               {
                                                                                   VulnerabilityRefreshIntervalMinutes = refreshIntervalMinutes,
                                                                               },
                                                                });
    }

    /// <summary>
    /// Create a completed vulnerability scan run
    /// </summary>
    /// <param name="status">Scan run status</param>
    /// <param name="errorMessage">Scan run error message</param>
    /// <param name="startedAtUtc">Scan run start timestamp</param>
    /// <returns>Vulnerability scan run</returns>
    private static ScanRun CreateVulnerabilityScanRun(ScanRunStatus status, string? errorMessage, DateTimeOffset startedAtUtc)
    {
        return new ScanRun
               {
                   Type = ScanRunType.Vulnerability,
                   Status = status,
                   TriggerSource = ScanTriggerSource.Scheduled,
                   StartedAtUtc = startedAtUtc,
                   CompletedAtUtc = startedAtUtc.AddMinutes(1),
                   ErrorMessage = errorMessage,
               };
    }

    /// <summary>
    /// Read the dashboard for a vulnerability configuration and an optionally seeded vulnerability scan run
    /// </summary>
    /// <param name="optionsMonitor">Options monitor</param>
    /// <param name="scanRun">Vulnerability scan run to seed, or null when no run exists</param>
    /// <returns>Dashboard view data</returns>
    private static async Task<DashboardViewData> ReadDashboardAsync(TestOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, ScanRun? scanRun)
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            if (scanRun is not null)
            {
                dbContext.ScanRuns.Add(scanRun);

                await dbContext.SaveChangesAsync(CancellationToken.None)
                               .ConfigureAwait(false);
            }

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     optionsMonitor,
                                                     new SharedBaseImageQueryService(dbContext));

            return await service.GetDashboardAsync(CancellationToken.None)
                                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Read the vulnerability schedule for a vulnerability configuration and an optionally seeded vulnerability scan run
    /// </summary>
    /// <param name="optionsMonitor">Options monitor</param>
    /// <param name="scanRun">Vulnerability scan run to seed, or null when no run exists</param>
    /// <returns>Vulnerability schedule view data</returns>
    private static async Task<VulnerabilityScheduleViewData> ReadVulnerabilityScheduleAsync(TestOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor, ScanRun? scanRun)
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            if (scanRun is not null)
            {
                dbContext.ScanRuns.Add(scanRun);

                await dbContext.SaveChangesAsync(CancellationToken.None)
                               .ConfigureAwait(false);
            }

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     optionsMonitor,
                                                     new SharedBaseImageQueryService(dbContext));

            return await service.GetVulnerabilityScheduleAsync(CancellationToken.None)
                                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Create an in-memory database context for the shared base image tests
    /// </summary>
    /// <returns>Database context</returns>
    private static DockerUpdateGuardDbContext CreateSharedBaseDbContext()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        return new DockerUpdateGuardDbContext(options);
    }

    /// <summary>
    /// Create the application view service with the real shared base image query service
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <returns>Application view service</returns>
    private static ApplicationViewService CreateSharedBaseService(DockerUpdateGuardDbContext dbContext)
    {
        return new ApplicationViewService(dbContext,
                                          new ImageReferenceParser(),
                                          CreateOptionsMonitor(),
                                          new SharedBaseImageQueryService(dbContext));
    }

    /// <summary>
    /// Create an image version of a docker.io repository whose digest names the tag
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="repository">Repository path</param>
    /// <param name="tag">Tag</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Image version</returns>
    private static async Task<ImageVersion> CreateSharedBaseVersionAsync(DockerUpdateGuardDbContext dbContext, string repository, string tag, CancellationToken cancellationToken)
    {
        var imageCatalogRepository = new ImageCatalogRepository(dbContext);

        return await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                         repository,
                                                                         tag,
                                                                         $"sha256:{repository}-{tag}",
                                                                         cancellationToken: cancellationToken)
                                           .ConfigureAwait(false);
    }

    /// <summary>
    /// Add an observed image and the base image relationship of its current version
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="name">Observed image name</param>
    /// <param name="currentVersionId">Current image version identifier</param>
    /// <param name="isEnabled">Whether the observed image is enabled</param>
    private static void AddObservedImage(DockerUpdateGuardDbContext dbContext, string name, Guid currentVersionId, bool isEnabled = true)
    {
        dbContext.ObservedImages.Add(new ObservedImage
                                     {
                                         Name = name,
                                         CurrentImageVersionId = currentVersionId,
                                         IsEnabled = isEnabled,
                                     });
    }

    /// <summary>
    /// Add a base image relationship
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="childVersionId">Child image version identifier</param>
    /// <param name="baseVersionId">Base image version identifier</param>
    private static void AddBaseRelationship(DockerUpdateGuardDbContext dbContext, Guid childVersionId, Guid baseVersionId)
    {
        dbContext.ImageRelationships.Add(new ImageRelationship
                                         {
                                             ChildImageVersionId = childVersionId,
                                             BaseImageVersionId = baseVersionId,
                                             RelationshipType = ImageRelationshipType.BaseImage,
                                             Depth = 1,
                                         });
    }

    /// <summary>
    /// Add a Docker instance whose latest runtime scan lists the named containers of one image version
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="imageVersionId">Image version identifier of the containers</param>
    /// <param name="containerNames">Container names</param>
    private static void AddCurrentContainers(DockerUpdateGuardDbContext dbContext, Guid imageVersionId, params string[] containerNames)
    {
        var dockerInstance = new DockerInstance
                             {
                                 Name = $"Engine {Guid.NewGuid()}",
                                 EndpointUri = "https://docker.example.test",
                                 ConnectionKind = DockerConnectionKind.Https,
                             };
        var runtimeScanRun = new ScanRun
                             {
                                 Type = ScanRunType.RuntimeContainer,
                                 Status = ScanRunStatus.Succeeded,
                                 TriggerSource = ScanTriggerSource.Scheduled,
                             };

        dbContext.DockerInstances.Add(dockerInstance);
        dbContext.ScanRuns.Add(runtimeScanRun);

        foreach (var containerName in containerNames)
        {
            dbContext.ContainerSnapshots.Add(new ContainerSnapshot
                                             {
                                                 DockerInstance = dockerInstance,
                                                 ImageVersionId = imageVersionId,
                                                 ScanRun = runtimeScanRun,
                                                 ContainerId = $"id-{containerName}",
                                                 Name = containerName,
                                                 Status = ContainerRuntimeStatus.Running,
                                                 IsRunning = true,
                                             });
        }
    }

    /// <summary>
    /// Create an image version whose stored vulnerability assessment source is given
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="assessmentSource">Stored assessment source</param>
    /// <returns>Image version</returns>
    private static async Task<ImageVersion> CreateRetiredValueImageVersionAsync(DockerUpdateGuardDbContext dbContext, VulnerabilitySource assessmentSource)
    {
        var imageCatalogRepository = new ImageCatalogRepository(dbContext);
        var imageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("mcr.microsoft.com",
                                                                                     "dotnet/aspnet",
                                                                                     "8.0",
                                                                                     "sha256:image",
                                                                                     cancellationToken: CancellationToken.None)
                                                       .ConfigureAwait(false);

        imageVersion.VulnerabilityAssessmentStatus = VulnerabilityAssessmentStatus.Failed;
        imageVersion.VulnerabilityAssessmentSource = assessmentSource;
        imageVersion.VulnerabilityAssessmentCheckedAtUtc = DateTimeOffset.UtcNow;

        return imageVersion;
    }

    /// <summary>
    /// Read the observed image detail through the view service
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="observedImageId">Observed image identifier</param>
    /// <returns>Observed image detail</returns>
    private static async Task<ObservedImageDetailViewData?> ReadObservedImageDetailAsync(DockerUpdateGuardDbContext dbContext, Guid observedImageId)
    {
        var service = new ApplicationViewService(dbContext,
                                                 new ImageReferenceParser(),
                                                 CreateOptionsMonitor(),
                                                 new SharedBaseImageQueryService(dbContext));

        return await service.GetObservedImageDetailAsync(observedImageId, CancellationToken.None)
                            .ConfigureAwait(false);
    }

    /// <summary>
    /// Seed a runtime container with tag candidates, a manual tag selection and vulnerability findings
    /// </summary>
    /// <param name="dbContext">Database context to seed</param>
    /// <returns>Identifier of the seeded Docker instance</returns>
    private static async Task<Guid> SeedRuntimeContainerDetailDataAsync(DockerUpdateGuardDbContext dbContext)
    {
        var imageCatalogRepository = new ImageCatalogRepository(dbContext);
        var imageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("ghcr.io",
                                                                                     "acme/api",
                                                                                     "1.0.0",
                                                                                     "sha256:current",
                                                                                     new DateTimeOffset(2025, 06, 01, 12, 00, 00, TimeSpan.Zero),
                                                                                     cancellationToken: CancellationToken.None)
                                                       .ConfigureAwait(false);
        imageVersion.VulnerabilityAssessmentStatus = VulnerabilityAssessmentStatus.FindingsDetected;
        imageVersion.VulnerabilityAssessmentSource = VulnerabilitySource.Trivy;
        imageVersion.VulnerabilityAssessmentMessage = "Trivy reported active findings";
        imageVersion.VulnerabilityAssessmentCheckedAtUtc = DateTimeOffset.UtcNow;

        var recommendedImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("ghcr.io",
                                                                                                "acme/api",
                                                                                                "1.1.0",
                                                                                                "sha256:recommended",
                                                                                                cancellationToken: CancellationToken.None)
                                                                  .ConfigureAwait(false);
        var dockerInstance = new DockerInstance
                             {
                                 Name = "Production",
                                 EndpointUri = "https://docker.example.test",
                                 ConnectionKind = DockerConnectionKind.Https,
                             };
        var scanRun = new ScanRun
                      {
                          Type = ScanRunType.RuntimeContainer,
                          Status = ScanRunStatus.Succeeded,
                          TriggerSource = ScanTriggerSource.Manual,
                          DockerInstance = dockerInstance,
                      };
        var snapshot = new ContainerSnapshot
                       {
                           DockerInstance = dockerInstance,
                           ImageVersionId = imageVersion.Id,
                           ScanRun = scanRun,
                           ContainerId = "container-a",
                           Name = "api",
                           Status = ContainerRuntimeStatus.Running,
                           IsRunning = true,
                           UpdateAssessmentStatus = UpdateAssessmentStatus.ManualReviewRequired,
                           UpdateAssessmentMessage = "Alternative tags are available and require manual review",
                       };
        var updateFinding = new UpdateFinding
                            {
                                ScanRun = scanRun,
                                ContainerSnapshot = snapshot,
                                SubjectImageVersionId = imageVersion.Id,
                                RecommendedImageVersionId = recommendedImageVersion.Id,
                                Type = UpdateFindingType.TagRecommendation,
                                Summary = "Alternative tags are available",
                                Details = "Choose a compatible tag manually",
                                IsActive = true,
                            };

        AddManualSelectionTagCandidates(updateFinding);

        dbContext.ContainerSnapshots.Add(snapshot);
        dbContext.UpdateFindings.Add(updateFinding);
        dbContext.RuntimeContainerTagSelections.Add(new RuntimeContainerTagSelection
                                                    {
                                                        DockerInstance = dockerInstance,
                                                        RegistryRepositoryId = imageVersion.RegistryRepositoryId,
                                                        ContainerId = "container-a",
                                                        Tag = "1.0.5",
                                                        Digest = "sha256:manual",
                                                    });

        dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                            {
                                                ImageVersionId = imageVersion.Id,
                                                ScanRun = scanRun,
                                                AdvisoryId = "CVE-2026-1000",
                                                Title = "Remote code execution",
                                                Severity = VulnerabilitySeverity.Critical,
                                                Source = VulnerabilitySource.Trivy,
                                                AffectedPackage = "openssl",
                                                FixedVersion = "3.0.1",
                                                IsActive = true,
                                            });
        dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                            {
                                                ImageVersionId = imageVersion.Id,
                                                ScanRun = scanRun,
                                                AdvisoryId = "CVE-2026-1001",
                                                Title = "Denial of service",
                                                Severity = VulnerabilitySeverity.High,
                                                Source = VulnerabilitySource.Trivy,
                                                AffectedPackage = "libcurl",
                                                IsActive = true,
                                            });
        dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                            {
                                                ImageVersionId = imageVersion.Id,
                                                ScanRun = scanRun,
                                                AdvisoryId = "CVE-2026-1002",
                                                Title = "Resolved information disclosure",
                                                Severity = VulnerabilitySeverity.Medium,
                                                Source = VulnerabilitySource.Trivy,
                                                AffectedPackage = "zlib",
                                                FixedVersion = "1.3.1",
                                                IsActive = false,
                                                ResolvedAtUtc = DateTimeOffset.UtcNow,
                                            });

        await dbContext.SaveChangesAsync(CancellationToken.None)
                       .ConfigureAwait(false);

        return dockerInstance.Id;
    }

    /// <summary>
    /// Seed observed images, a container, update findings and a vulnerability finding for the dashboard counts
    /// </summary>
    /// <param name="dbContext">Database context to seed</param>
    /// <returns>Task</returns>
    private static async Task SeedDashboardAggregateDataAsync(DockerUpdateGuardDbContext dbContext)
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
                                Source = RegistrationSource.Discovery,
                            };

        var manualObservedImage = new ObservedImage
                                  {
                                      Name = "Manual API",
                                      CurrentImageVersionId = imageVersion.Id,
                                      Source = RegistrationSource.Manual,
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
        dbContext.ObservedImages.Add(manualObservedImage);
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
                                         Type = UpdateFindingType.RuntimeImageUpdate,
                                         Summary = "Update available",
                                         IsActive = true,
                                     });

        dbContext.UpdateFindings.Add(new UpdateFinding
                                     {
                                         ObservedImage = observedImage,
                                         SubjectImageVersionId = imageVersion.Id,
                                         ScanRun = scanRun,
                                         Type = UpdateFindingType.DerivedBaseRuntimeUpdate,
                                         Summary = "Own runtime warning",
                                         IsActive = true,
                                     });

        dbContext.UpdateFindings.Add(new UpdateFinding
                                     {
                                         ObservedImage = manualObservedImage,
                                         SubjectImageVersionId = imageVersion.Id,
                                         ScanRun = scanRun,
                                         Type = UpdateFindingType.DerivedBaseRuntimeUpdate,
                                         Summary = "Manual runtime hint",
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
    }

    /// <summary>
    /// Add the recommended, manual, outdated, legacy and invalid tag candidates to an update finding
    /// </summary>
    /// <param name="updateFinding">Update finding to add the tag candidates to</param>
    private static void AddManualSelectionTagCandidates(UpdateFinding updateFinding)
    {
        updateFinding.TagCandidates.Add(new TagCandidate
                                        {
                                            Tag = "1.1.0",
                                            Digest = "sha256:recommended",
                                            Rank = 0,
                                            IsRecommended = true,
                                            PublishedAtUtc = new DateTimeOffset(2025, 06, 03, 12, 00, 00, TimeSpan.Zero),
                                            Reason = "Latest compatible stable tag",
                                        });

        updateFinding.TagCandidates.Add(new TagCandidate
                                        {
                                            Tag = "1.0.5",
                                            Digest = "sha256:manual",
                                            Rank = 1,
                                            IsRecommended = false,
                                            PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                            Reason = "Latest patch in the current minor line",
                                        });

        updateFinding.TagCandidates.Add(new TagCandidate
                                        {
                                            Tag = "0.9.9",
                                            Digest = "sha256:legacy-version",
                                            Rank = 2,
                                            IsRecommended = false,
                                            PublishedAtUtc = new DateTimeOffset(2025, 06, 04, 12, 00, 00, TimeSpan.Zero),
                                            Reason = "Older major line",
                                        });

        updateFinding.TagCandidates.Add(new TagCandidate
                                        {
                                            Tag = "1.0.7",
                                            Digest = "sha256:old-publish",
                                            Rank = 3,
                                            IsRecommended = false,
                                            PublishedAtUtc = new DateTimeOffset(2025, 05, 30, 12, 00, 00, TimeSpan.Zero),
                                            Reason = "Older publication timestamp",
                                        });

        updateFinding.TagCandidates.Add(new TagCandidate
                                        {
                                            Tag = "broken",
                                            Digest = string.Empty,
                                            Rank = 4,
                                            IsRecommended = false,
                                            Reason = "Legacy invalid candidate",
                                        });
    }

    #endregion // Static methods

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

            enabledImageVersion.VulnerabilityAssessmentCheckedAtUtc = DateTimeOffset.UtcNow;

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
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));

            var runtimeContainersTask = service.GetRuntimeContainersAsync(CancellationToken.None);
            var runtimeContainers = await runtimeContainersTask.ConfigureAwait(false);
            var withPortainer = runtimeContainers.Single(entity => entity.DockerInstanceName == "With Portainer");
            var withoutPortainer = runtimeContainers.Single(entity => entity.DockerInstanceName == "Without Portainer");

            Assert.IsTrue(withPortainer.PortainerAvailable, "Runtime containers must surface Portainer availability when the instance endpoint is enabled");
            Assert.IsFalse(withoutPortainer.PortainerAvailable, "Runtime containers must hide Portainer availability when the instance endpoint is disabled");
            Assert.AreEqual(enabledImageVersion.Id,
                            withPortainer.ImageVersionId,
                            "Runtime containers must expose the image version identifier backing the container");
            Assert.AreEqual(enabledImageVersion.VulnerabilityAssessmentCheckedAtUtc,
                            withPortainer.VulnerabilityCheckedAtUtc,
                            "Runtime containers must expose when the image version was last checked for vulnerabilities");
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
            await SeedDashboardAggregateDataAsync(dbContext).ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var dashboard = await service.GetDashboardAsync(CancellationToken.None)
                                         .ConfigureAwait(false);

            Assert.AreEqual(1,
                            dashboard.ObservedImageCount,
                            "The dashboard must report only manually-registered observed images");
            Assert.AreEqual(1,
                            dashboard.MyImageCount,
                            "The dashboard must report only discovery-owned images in the My Images count");
            Assert.AreEqual(1,
                            dashboard.DockerInstanceCount,
                            "The dashboard must report the Docker instance count");
            Assert.AreEqual(1,
                            dashboard.RuntimeContainerCount,
                            "The dashboard must report the runtime container count");
            Assert.AreEqual(3,
                            dashboard.ActiveUpdateFindingCount,
                            "The dashboard must report the active update finding count");
            Assert.AreEqual(1,
                            dashboard.OwnImageBaseRuntimeWarningCount,
                            "The dashboard must only count derived base-runtime warnings for own images");
            Assert.AreEqual(1,
                            dashboard.ActiveVulnerabilityFindingCount,
                            "The dashboard must report the active vulnerability finding count");
            Assert.HasCount(1,
                            dashboard.RecentScans,
                            "The dashboard must include the recent scan entry");
        }
    }

    /// <summary>
    /// Verify dashboard vulnerability totals only include image versions used by current runtime containers
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceDashboardExcludesFindingsForInactiveRuntimeImageVersionsAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var currentImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                "company/api",
                                                                                                "2.0.0",
                                                                                                "sha256:current",
                                                                                                cancellationToken: CancellationToken.None)
                                                                  .ConfigureAwait(false);
            var retiredImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                "company/api",
                                                                                                "1.0.0",
                                                                                                "sha256:retired",
                                                                                                cancellationToken: CancellationToken.None)
                                                                  .ConfigureAwait(false);
            var dockerInstance = new DockerInstance
                                 {
                                     Name = "Production Engine",
                                     EndpointUri = "https://docker.example.test",
                                     ConnectionKind = DockerConnectionKind.Https,
                                 };
            var runtimeScanRun = new ScanRun
                                 {
                                     Type = ScanRunType.RuntimeContainer,
                                     Status = ScanRunStatus.Succeeded,
                                     TriggerSource = ScanTriggerSource.Scheduled,
                                 };

            dbContext.DockerInstances.Add(dockerInstance);
            dbContext.ScanRuns.Add(runtimeScanRun);
            dbContext.ContainerSnapshots.Add(new ContainerSnapshot
                                             {
                                                 DockerInstance = dockerInstance,
                                                 ImageVersionId = currentImageVersion.Id,
                                                 ScanRun = runtimeScanRun,
                                                 ContainerId = "container-a",
                                                 Name = "api",
                                                 Status = ContainerRuntimeStatus.Running,
                                                 IsRunning = true,
                                             });
            dbContext.VulnerabilityFindings.AddRange(new VulnerabilityFinding
                                                     {
                                                         ImageVersionId = currentImageVersion.Id,
                                                         AdvisoryId = "CVE-2026-0001",
                                                         Title = "Current runtime vulnerability",
                                                         Severity = VulnerabilitySeverity.High,
                                                         Source = VulnerabilitySource.Trivy,
                                                         IsActive = true,
                                                     },
                                                     new VulnerabilityFinding
                                                     {
                                                         ImageVersionId = retiredImageVersion.Id,
                                                         AdvisoryId = "CVE-2026-0002",
                                                         Title = "Retired runtime vulnerability",
                                                         Severity = VulnerabilitySeverity.Critical,
                                                         Source = VulnerabilitySource.Trivy,
                                                         IsActive = true,
                                                     });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var dashboard = await service.GetDashboardAsync(CancellationToken.None)
                                         .ConfigureAwait(false);

            Assert.AreEqual(1,
                            dashboard.ActiveVulnerabilityFindingCount,
                            "The dashboard must exclude active findings for image versions not used by current runtime containers");
            Assert.AreEqual(0,
                            dashboard.VulnerabilitySeveritySummary.CriticalCount,
                            "The dashboard must exclude the critical finding from the retired image version");
            Assert.AreEqual(1,
                            dashboard.VulnerabilitySeveritySummary.HighCount,
                            "The dashboard must retain the high finding from the current runtime image version");
        }
    }

    /// <summary>
    /// Verify base images are exposed through the application view service
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceBaseImagesReturnsAggregatedUsageAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var sharedBaseVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                              "library/debian",
                                                                                              "12-slim",
                                                                                              "sha256:base",
                                                                                              cancellationToken: CancellationToken.None)
                                                                .ConfigureAwait(false);
            var appVersionA = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                        "company/app-a",
                                                                                        "1.0.0",
                                                                                        "sha256:app-a",
                                                                                        cancellationToken: CancellationToken.None)
                                                          .ConfigureAwait(false);
            var appVersionB = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                        "company/app-b",
                                                                                        "2.0.0",
                                                                                        "sha256:app-b",
                                                                                        cancellationToken: CancellationToken.None)
                                                          .ConfigureAwait(false);

            dbContext.ObservedImages.AddRange(new ObservedImage
                                              {
                                                  Name = "App A",
                                                  CurrentImageVersionId = appVersionA.Id,
                                                  Source = RegistrationSource.Discovery,
                                              },
                                              new ObservedImage
                                              {
                                                  Name = "App B",
                                                  CurrentImageVersionId = appVersionB.Id,
                                                  Source = RegistrationSource.Manual,
                                              });
            dbContext.ImageRelationships.AddRange(new ImageRelationship
                                                  {
                                                      ChildImageVersionId = appVersionA.Id,
                                                      BaseImageVersionId = sharedBaseVersion.Id,
                                                      RelationshipType = ImageRelationshipType.BaseImage,
                                                      Depth = 1,
                                                  },
                                                  new ImageRelationship
                                                  {
                                                      ChildImageVersionId = appVersionB.Id,
                                                      BaseImageVersionId = sharedBaseVersion.Id,
                                                      RelationshipType = ImageRelationshipType.BaseImage,
                                                      Depth = 1,
                                                  });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var hasBaseImages = await service.HasBaseImagesAsync(CancellationToken.None)
                                             .ConfigureAwait(false);
            var baseImages = await service.GetBaseImagesAsync(CancellationToken.None)
                                          .ConfigureAwait(false);

            Assert.IsTrue(hasBaseImages,
                          "The application view service must report available base images when at least one relationship is stored");
            Assert.HasCount(1,
                            baseImages,
                            "The application view service must surface one base image entry for the seeded base image");
            Assert.IsTrue(baseImages[0].ImageReference.StartsWith("docker.io/library/debian:12-slim@",
                                                                  StringComparison.Ordinal),
                          "The base image must expose the formatted image reference including its digest");
            Assert.AreEqual(2,
                            baseImages[0].ObservedImageCount,
                            "The base image must report the number of observed images that depend on it");
            Assert.AreSequenceEqual(["docker.io/company/app-a:1.0.0@sha256:app-a", "docker.io/company/app-b:2.0.0@sha256:app-b"], baseImages[0].ParentImageReferences.ToArray(), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder, "The base image must list the parent images that use it");
        }
    }

    /// <summary>
    /// Verify runtime container detail exposes manual selection and explicit assessment states
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceRuntimeContainerDetailReturnsManualSelectionAndAssessmentStatesAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var dockerInstanceId = await SeedRuntimeContainerDetailDataAsync(dbContext).ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));

            var detail = await service.GetRuntimeContainerDetailAsync(dockerInstanceId, "container-a", CancellationToken.None)
                                      .ConfigureAwait(false);

            Assert.IsNotNull(detail, "Runtime container detail must be returned for the latest snapshot");
            Assert.AreEqual("Manual review required",
                            detail.UpdateStatus,
                            "The runtime detail must expose the explicit update assessment label");
            Assert.AreEqual("Findings detected",
                            detail.VulnerabilityAssessment.Status,
                            "The runtime detail must expose the explicit vulnerability assessment label");
            Assert.AreEqual("Trivy",
                            detail.VulnerabilityAssessment.Source,
                            "The runtime detail must expose the provider that produced the vulnerability assessment");
            Assert.AreEqual(1,
                            detail.VulnerabilityAssessment.FixableFindingCount,
                            "Only the active finding with a non-empty fixed version must count as fixable");
            Assert.AreEqual("ghcr.io/acme/api:1.0.5@sha256:manual",
                            detail.ManualSelectionImage,
                            "The runtime detail must show the persisted manual tag preference");
            Assert.IsTrue(detail.AvailableTagCandidates.Single(entity => entity.Tag == "1.0.5").IsSelected,
                          "The saved manual tag candidate must be marked as selected");
            Assert.DoesNotContain(entity => entity.Tag == "broken",
                                  detail.AvailableTagCandidates,
                                  "Runtime detail must hide legacy tag candidates without digests");
            Assert.DoesNotContain(entity => entity.Tag == "0.9.9",
                                  detail.AvailableTagCandidates,
                                  "Runtime detail must hide candidates with an older version than the current image");
            Assert.DoesNotContain(entity => entity.Tag == "1.0.7",
                                  detail.AvailableTagCandidates,
                                  "Runtime detail must hide candidates published before the current image");
        }
    }

    /// <summary>
    /// Verify vulnerability finding reference URLs are sanitized so only absolute http(s) URLs reach the UI
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceSanitizesVulnerabilityReferenceUrlsAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var imageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                         "company/api",
                                                                                         "1.0.0",
                                                                                         "sha256:api",
                                                                                         cancellationToken: CancellationToken.None)
                                                           .ConfigureAwait(false);

            var observedImage = new ObservedImage
                                {
                                    Name = "Company API",
                                    CurrentImageVersionId = imageVersion.Id,
                                };

            dbContext.ObservedImages.Add(observedImage);
            dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                                {
                                                    ImageVersionId = imageVersion.Id,
                                                    AdvisoryId = "CVE-2026-2001",
                                                    Title = "Safe advisory link",
                                                    Severity = VulnerabilitySeverity.High,
                                                    Source = VulnerabilitySource.Trivy,
                                                    ReferenceUrl = "https://nvd.nist.gov/vuln/detail/CVE-2026-2001",
                                                    IsActive = true,
                                                });
            dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                                {
                                                    ImageVersionId = imageVersion.Id,
                                                    AdvisoryId = "CVE-2026-2002",
                                                    Title = "Malicious advisory link",
                                                    Severity = VulnerabilitySeverity.Critical,
                                                    Source = VulnerabilitySource.Trivy,
                                                    ReferenceUrl = "javascript:alert(document.cookie)",
                                                    IsActive = true,
                                                });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));

            var detail = await service.GetObservedImageDetailAsync(observedImage.Id, CancellationToken.None)
                                      .ConfigureAwait(false);

            Assert.IsNotNull(detail, "The observed image detail must be returned");

            var safeFinding = detail.VulnerabilityFindings.Single(entity => entity.AdvisoryId == "CVE-2026-2001");
            var maliciousFinding = detail.VulnerabilityFindings.Single(entity => entity.AdvisoryId == "CVE-2026-2002");

            Assert.AreEqual("https://nvd.nist.gov/vuln/detail/CVE-2026-2001",
                            safeFinding.ReferenceUrl,
                            "A valid absolute https reference URL must be preserved for rendering");
            Assert.IsNull(maliciousFinding.ReferenceUrl,
                          "A javascript: reference URL must be dropped so it can never be rendered as an href");
        }
    }

    /// <summary>
    /// Verify observed image detail counts only active findings with a non-empty fixed version as fixable
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceObservedImageDetailComputesFixableFindingCountAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var imageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                         "company/api",
                                                                                         "1.0.0",
                                                                                         "sha256:api",
                                                                                         cancellationToken: CancellationToken.None)
                                                           .ConfigureAwait(false);

            var observedImage = new ObservedImage
                                {
                                    Name = "Company API",
                                    CurrentImageVersionId = imageVersion.Id,
                                };

            dbContext.ObservedImages.Add(observedImage);
            dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                                {
                                                    ImageVersionId = imageVersion.Id,
                                                    AdvisoryId = "CVE-2026-3001",
                                                    Title = "Active finding with a fix available",
                                                    Severity = VulnerabilitySeverity.High,
                                                    Source = VulnerabilitySource.Trivy,
                                                    AffectedPackage = "openssl",
                                                    FixedVersion = "3.0.1",
                                                    IsActive = true,
                                                });
            dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                                {
                                                    ImageVersionId = imageVersion.Id,
                                                    AdvisoryId = "CVE-2026-3002",
                                                    Title = "Active finding without a fix",
                                                    Severity = VulnerabilitySeverity.Medium,
                                                    Source = VulnerabilitySource.Trivy,
                                                    AffectedPackage = "musl",
                                                    IsActive = true,
                                                });
            dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                                {
                                                    ImageVersionId = imageVersion.Id,
                                                    AdvisoryId = "CVE-2026-3003",
                                                    Title = "Resolved finding with a fix",
                                                    Severity = VulnerabilitySeverity.Low,
                                                    Source = VulnerabilitySource.Trivy,
                                                    AffectedPackage = "zlib",
                                                    FixedVersion = "1.3.1",
                                                    IsActive = false,
                                                    ResolvedAtUtc = DateTimeOffset.UtcNow,
                                                });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));

            var detail = await service.GetObservedImageDetailAsync(observedImage.Id, CancellationToken.None)
                                      .ConfigureAwait(false);

            Assert.IsNotNull(detail, "The observed image detail must be returned");
            Assert.AreEqual(2,
                            detail.VulnerabilityAssessment.ActiveFindingCount,
                            "Both active findings must count toward the active finding total");
            Assert.AreEqual(1,
                            detail.VulnerabilityAssessment.FixableFindingCount,
                            "Only the active finding with a non-empty fixed version must count as fixable");
        }
    }

    /// <summary>
    /// Verify observed image detail computes new/resolved finding deltas from the scan run that produced the
    /// latest assessment and marks the newly detected finding in the returned finding list
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceObservedImageDetailComputesScanDeltaForMixedFindingsAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var imageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                         "company/api",
                                                                                         "1.0.0",
                                                                                         "sha256:api",
                                                                                         cancellationToken: CancellationToken.None)
                                                           .ConfigureAwait(false);
            var lastCheckedAtUtc = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
            var previousScanRunId = Guid.NewGuid();
            var currentScanRunId = Guid.NewGuid();

            imageVersion.VulnerabilityAssessmentCheckedAtUtc = lastCheckedAtUtc;
            imageVersion.VulnerabilityAssessmentScanRunId = currentScanRunId;

            var observedImage = new ObservedImage
                                {
                                    Name = "Company API",
                                    CurrentImageVersionId = imageVersion.Id,
                                };

            dbContext.ObservedImages.Add(observedImage);
            dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                                {
                                                    ImageVersionId = imageVersion.Id,
                                                    ScanRunId = previousScanRunId,
                                                    AdvisoryId = "CVE-2026-4001",
                                                    Title = "Long-standing active finding",
                                                    Severity = VulnerabilitySeverity.High,
                                                    Source = VulnerabilitySource.Trivy,
                                                    IsActive = true,
                                                    DetectedAtUtc = lastCheckedAtUtc.AddDays(-30),
                                                });
            dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                                {
                                                    ImageVersionId = imageVersion.Id,
                                                    ScanRunId = currentScanRunId,
                                                    AdvisoryId = "CVE-2026-4002",
                                                    Title = "Newly detected active finding",
                                                    Severity = VulnerabilitySeverity.Critical,
                                                    Source = VulnerabilitySource.Trivy,
                                                    IsActive = true,
                                                    DetectedAtUtc = lastCheckedAtUtc,
                                                });
            dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                                {
                                                    ImageVersionId = imageVersion.Id,
                                                    ScanRunId = previousScanRunId,
                                                    ResolvedByScanRunId = currentScanRunId,
                                                    AdvisoryId = "CVE-2026-4003",
                                                    Title = "Recently resolved finding",
                                                    Severity = VulnerabilitySeverity.Medium,
                                                    Source = VulnerabilitySource.Trivy,
                                                    IsActive = false,
                                                    DetectedAtUtc = lastCheckedAtUtc.AddDays(-60),
                                                    ResolvedAtUtc = lastCheckedAtUtc,
                                                });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));

            var detail = await service.GetObservedImageDetailAsync(observedImage.Id, CancellationToken.None)
                                      .ConfigureAwait(false);

            Assert.IsNotNull(detail, "The observed image detail must be returned");
            Assert.AreEqual(1,
                            detail.VulnerabilityAssessment.NewFindingCount,
                            "Only the finding detected by the most recent scan run must count as new");
            Assert.AreEqual(1,
                            detail.VulnerabilityAssessment.ResolvedFindingCount,
                            "Only the finding resolved by the most recent scan run must count as resolved");

            var newFinding = detail.VulnerabilityFindings.Single(finding => finding.AdvisoryId == "CVE-2026-4002");
            var oldFinding = detail.VulnerabilityFindings.Single(finding => finding.AdvisoryId == "CVE-2026-4001");

            Assert.IsTrue(newFinding.IsNewSinceLastScan, "The newly detected finding must be marked as new since the last scan");
            Assert.IsFalse(oldFinding.IsNewSinceLastScan, "The long-standing finding must not be marked as new since the last scan");
        }
    }

    /// <summary>
    /// Verify observed image detail reports zero deltas when the whole finding set was resolved and re-inserted
    /// by an earlier scan run and a second scan run followed shortly afterwards
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceObservedImageDetailReportsZeroDeltaForConsecutiveScanRunsAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var imageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                         "library/phpmyadmin",
                                                                                         "latest",
                                                                                         "sha256:phpmyadmin",
                                                                                         cancellationToken: CancellationToken.None)
                                                           .ConfigureAwait(false);
            var churnScanRunAtUtc = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
            var originScanRunId = Guid.NewGuid();
            var churnScanRunId = Guid.NewGuid();
            var currentScanRunId = Guid.NewGuid();

            // The manual refresh follows the scheduled refresh one second later, which is well inside the
            // five-minute wall-clock window the previous heuristic used to attribute findings to a scan
            imageVersion.VulnerabilityAssessmentCheckedAtUtc = churnScanRunAtUtc.AddSeconds(1);
            imageVersion.VulnerabilityAssessmentScanRunId = currentScanRunId;

            var observedImage = new ObservedImage
                                {
                                    Name = "phpMyAdmin",
                                    CurrentImageVersionId = imageVersion.Id,
                                };

            dbContext.ObservedImages.Add(observedImage);

            for (var findingIndex = 0; findingIndex < 3; findingIndex++)
            {
                dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                                    {
                                                        ImageVersionId = imageVersion.Id,
                                                        ScanRunId = originScanRunId,
                                                        ResolvedByScanRunId = churnScanRunId,
                                                        AdvisoryId = $"CVE-2026-43{findingIndex:D2}",
                                                        AffectedPackage = "openssl",
                                                        Title = "Resolved copy of a re-inserted finding",
                                                        Severity = VulnerabilitySeverity.High,
                                                        Source = VulnerabilitySource.Trivy,
                                                        IsActive = false,
                                                        DetectedAtUtc = churnScanRunAtUtc.AddDays(-30),
                                                        ResolvedAtUtc = churnScanRunAtUtc,
                                                    });
                dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                                    {
                                                        ImageVersionId = imageVersion.Id,
                                                        ScanRunId = churnScanRunId,
                                                        AdvisoryId = $"CVE-2026-43{findingIndex:D2}",
                                                        AffectedPackage = "openssl",
                                                        Title = "Re-inserted copy of an unchanged finding",
                                                        Severity = VulnerabilitySeverity.High,
                                                        Source = VulnerabilitySource.Trivy,
                                                        IsActive = true,
                                                        DetectedAtUtc = churnScanRunAtUtc,
                                                    });
            }

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));

            var detail = await service.GetObservedImageDetailAsync(observedImage.Id, CancellationToken.None)
                                      .ConfigureAwait(false);

            Assert.IsNotNull(detail, "The observed image detail must be returned");
            Assert.AreEqual(3,
                            detail.VulnerabilityAssessment.ActiveFindingCount,
                            "The re-inserted findings must remain the active finding set");
            Assert.AreEqual(0,
                            detail.VulnerabilityAssessment.NewFindingCount,
                            "A scan run that changed nothing must not report the re-inserted findings as new");
            Assert.AreEqual(0,
                            detail.VulnerabilityAssessment.ResolvedFindingCount,
                            "A scan run that changed nothing must not report the earlier resolve batch as resolved");
            Assert.IsTrue(detail.VulnerabilityFindings.All(finding => finding.IsNewSinceLastScan == false),
                          "No finding must be marked as new when the most recent scan run changed nothing");
        }
    }

    /// <summary>
    /// Verify observed image detail suppresses new/resolved finding deltas on an image's first recorded scan
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceObservedImageDetailSuppressesScanDeltaOnFirstScanAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var imageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                         "company/api",
                                                                                         "1.0.0",
                                                                                         "sha256:api",
                                                                                         cancellationToken: CancellationToken.None)
                                                           .ConfigureAwait(false);
            var lastCheckedAtUtc = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
            var currentScanRunId = Guid.NewGuid();

            imageVersion.VulnerabilityAssessmentCheckedAtUtc = lastCheckedAtUtc;
            imageVersion.VulnerabilityAssessmentScanRunId = currentScanRunId;

            var observedImage = new ObservedImage
                                {
                                    Name = "Company API",
                                    CurrentImageVersionId = imageVersion.Id,
                                };

            dbContext.ObservedImages.Add(observedImage);
            dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                                {
                                                    ImageVersionId = imageVersion.Id,
                                                    ScanRunId = currentScanRunId,
                                                    AdvisoryId = "CVE-2026-4101",
                                                    Title = "First-scan finding",
                                                    Severity = VulnerabilitySeverity.High,
                                                    Source = VulnerabilitySource.Trivy,
                                                    IsActive = true,
                                                    DetectedAtUtc = lastCheckedAtUtc,
                                                });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));

            var detail = await service.GetObservedImageDetailAsync(observedImage.Id, CancellationToken.None)
                                      .ConfigureAwait(false);

            Assert.IsNotNull(detail, "The observed image detail must be returned");
            Assert.AreEqual(0,
                            detail.VulnerabilityAssessment.NewFindingCount,
                            "The first scan of an image must not report any finding as new");
            Assert.AreEqual(0,
                            detail.VulnerabilityAssessment.ResolvedFindingCount,
                            "The first scan of an image must not report any finding as resolved");
            Assert.IsFalse(detail.VulnerabilityFindings.Single().IsNewSinceLastScan,
                           "Findings from the first scan must not be individually marked as new");
        }
    }

    /// <summary>
    /// Verify observed image detail reports zero deltas once findings have settled into a steady state
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceObservedImageDetailReportsZeroDeltaInSteadyStateAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var imageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                         "company/api",
                                                                                         "1.0.0",
                                                                                         "sha256:api",
                                                                                         cancellationToken: CancellationToken.None)
                                                           .ConfigureAwait(false);
            var lastCheckedAtUtc = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
            var originScanRunId = Guid.NewGuid();
            var previousScanRunId = Guid.NewGuid();
            var currentScanRunId = Guid.NewGuid();

            imageVersion.VulnerabilityAssessmentCheckedAtUtc = lastCheckedAtUtc;
            imageVersion.VulnerabilityAssessmentScanRunId = currentScanRunId;

            var observedImage = new ObservedImage
                                {
                                    Name = "Company API",
                                    CurrentImageVersionId = imageVersion.Id,
                                };

            dbContext.ObservedImages.Add(observedImage);
            dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                                {
                                                    ImageVersionId = imageVersion.Id,
                                                    ScanRunId = originScanRunId,
                                                    AdvisoryId = "CVE-2026-4201",
                                                    Title = "Steady-state active finding",
                                                    Severity = VulnerabilitySeverity.Low,
                                                    Source = VulnerabilitySource.Trivy,
                                                    IsActive = true,
                                                    DetectedAtUtc = lastCheckedAtUtc.AddDays(-90),
                                                });
            dbContext.VulnerabilityFindings.Add(new VulnerabilityFinding
                                                {
                                                    ImageVersionId = imageVersion.Id,
                                                    ScanRunId = originScanRunId,
                                                    ResolvedByScanRunId = previousScanRunId,
                                                    AdvisoryId = "CVE-2026-4202",
                                                    Title = "Long-resolved finding",
                                                    Severity = VulnerabilitySeverity.Low,
                                                    Source = VulnerabilitySource.Trivy,
                                                    IsActive = false,
                                                    DetectedAtUtc = lastCheckedAtUtc.AddDays(-90),
                                                    ResolvedAtUtc = lastCheckedAtUtc.AddDays(-45),
                                                });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));

            var detail = await service.GetObservedImageDetailAsync(observedImage.Id, CancellationToken.None)
                                      .ConfigureAwait(false);

            Assert.IsNotNull(detail, "The observed image detail must be returned");
            Assert.AreEqual(0,
                            detail.VulnerabilityAssessment.NewFindingCount,
                            "Steady state must not report any finding as new");
            Assert.AreEqual(0,
                            detail.VulnerabilityAssessment.ResolvedFindingCount,
                            "Steady state must not report any finding as resolved");
            Assert.IsTrue(detail.VulnerabilityFindings.All(finding => finding.IsNewSinceLastScan == false),
                          "No finding must be marked as new in steady state");
        }
    }

    /// <summary>
    /// Verify observed image list and detail expose vulnerability assessment metadata
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceObservedImagesExposeVulnerabilityAssessmentMetadataAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var imageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("mcr.microsoft.com",
                                                                                         "dotnet/aspnet",
                                                                                         "8.0",
                                                                                         "sha256:image",
                                                                                         cancellationToken: CancellationToken.None)
                                                           .ConfigureAwait(false);
            imageVersion.VulnerabilityAssessmentStatus = VulnerabilityAssessmentStatus.Failed;
            imageVersion.VulnerabilityAssessmentSource = VulnerabilitySource.Trivy;
            imageVersion.VulnerabilityAssessmentMessage = "Trivy returned 500";
            imageVersion.VulnerabilityAssessmentCheckedAtUtc = DateTimeOffset.UtcNow;

            var observedImage = new ObservedImage
                                {
                                    Name = "ASP.NET Runtime",
                                    CurrentImageVersionId = imageVersion.Id,
                                };
            var scanRun = new ScanRun
                          {
                              Type = ScanRunType.ObservedImage,
                              Status = ScanRunStatus.Failed,
                              TriggerSource = ScanTriggerSource.Manual,
                              ObservedImage = observedImage,
                              ErrorMessage = "Scan failed",
                          };

            dbContext.ObservedImages.Add(observedImage);
            dbContext.ScanRuns.Add(scanRun);

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var listItems = await service.GetObservedImagesAsync(CancellationToken.None)
                                         .ConfigureAwait(false);
            var detail = await service.GetObservedImageDetailAsync(observedImage.Id, CancellationToken.None)
                                      .ConfigureAwait(false);
            var listItem = listItems.Single();

            Assert.AreEqual("Failed",
                            listItem.VulnerabilityStatus,
                            "The observed image list must expose the explicit vulnerability assessment label");
            Assert.AreEqual("Trivy returned 500",
                            listItem.VulnerabilityMessage,
                            "The observed image list must expose the assessment message");
            Assert.AreEqual(imageVersion.Id,
                            listItem.CurrentImageVersionId,
                            "The observed image list must expose the current image version identifier");
            Assert.AreEqual(imageVersion.VulnerabilityAssessmentCheckedAtUtc,
                            listItem.VulnerabilityCheckedAtUtc,
                            "The observed image list must expose when the current image version was last checked for vulnerabilities");
            Assert.IsNotNull(detail, "The observed image detail must be returned for a stored observed image");
            Assert.AreEqual("Failed",
                            detail.VulnerabilityAssessment.Status,
                            "The observed image detail must expose the vulnerability assessment status");
            Assert.AreEqual("Trivy",
                            detail.VulnerabilityAssessment.Source,
                            "The observed image detail must expose the vulnerability assessment source");
        }
    }

    /// <summary>
    /// Verify the observed image detail shows the stored number of a retired update finding type
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task ApplicationViewServiceObservedImageDetailRetiredUpdateFindingTypeShowsNumberAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageVersion = await CreateRetiredValueImageVersionAsync(dbContext, VulnerabilitySource.Trivy).ConfigureAwait(false);
            var observedImage = new ObservedImage
                                {
                                    Name = "Retired finding type",
                                    CurrentImageVersionId = imageVersion.Id,
                                };
            var scanRun = new ScanRun
                          {
                              Type = ScanRunType.ObservedImage,
                              Status = ScanRunStatus.Succeeded,
                              TriggerSource = ScanTriggerSource.Manual,
                              ObservedImage = observedImage,
                          };

            dbContext.ObservedImages.Add(observedImage);
            dbContext.ScanRuns.Add(scanRun);
            dbContext.UpdateFindings.Add(new UpdateFinding
                                         {
                                             ObservedImage = observedImage,
                                             SubjectImageVersionId = imageVersion.Id,
                                             ScanRun = scanRun,
                                             Type = (UpdateFindingType)1,
                                             Summary = "Retired type",
                                             IsActive = true,
                                         });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var detail = await ReadObservedImageDetailAsync(dbContext, observedImage.Id).ConfigureAwait(false);

            Assert.IsNotNull(detail, "The observed image detail must be returned for a stored observed image");
            Assert.AreEqual("1",
                            detail.UpdateFindings.Single().Type,
                            "A stored update finding type without a member must be shown as its number");
        }
    }

    /// <summary>
    /// Verify the observed image detail shows a retired vulnerability assessment source as not set
    /// </summary>
    /// <param name="storedSource">Retired stored source number</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow(3)]
    [DataRow(4)]
    public async Task ApplicationViewServiceObservedImageDetailRetiredVulnerabilitySourceShowsNotSetAsync(int storedSource)
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageVersion = await CreateRetiredValueImageVersionAsync(dbContext, (VulnerabilitySource)storedSource).ConfigureAwait(false);
            var observedImage = new ObservedImage
                                {
                                    Name = "Retired source",
                                    CurrentImageVersionId = imageVersion.Id,
                                };

            dbContext.ObservedImages.Add(observedImage);

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var detail = await ReadObservedImageDetailAsync(dbContext, observedImage.Id).ConfigureAwait(false);

            Assert.IsNotNull(detail, "The observed image detail must be returned for a stored observed image");
            Assert.AreEqual("Not set",
                            detail.VulnerabilityAssessment.Source,
                            "A stored assessment source without a member must be shown as not set");
        }
    }

    /// <summary>
    /// Verify own images are linked to runtime containers by normalized repository even when the runtime tag is older
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceOwnImagesLinkRuntimeContainersAcrossTagsAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var discoveredImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                   "company/api",
                                                                                                   "2.0.0",
                                                                                                   "sha256:2",
                                                                                                   cancellationToken: CancellationToken.None)
                                                                     .ConfigureAwait(false);
            var runtimeImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                "company/api",
                                                                                                "1.5.0",
                                                                                                "sha256:1",
                                                                                                cancellationToken: CancellationToken.None)
                                                                  .ConfigureAwait(false);
            var manualImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                               "company/worker",
                                                                                               "1.0.0",
                                                                                               "sha256:worker",
                                                                                               cancellationToken: CancellationToken.None)
                                                                 .ConfigureAwait(false);

            var ownObservedImage = new ObservedImage
                                   {
                                       Name = "Company API",
                                       CurrentImageVersionId = discoveredImageVersion.Id,
                                       Source = RegistrationSource.Discovery,
                                   };
            var manualObservedImage = new ObservedImage
                                      {
                                          Name = "Company Worker",
                                          CurrentImageVersionId = manualImageVersion.Id,
                                          Source = RegistrationSource.Manual,
                                      };
            var dockerInstance = new DockerInstance
                                 {
                                     Name = "Production",
                                     EndpointUri = "https://docker.example.test",
                                     ConnectionKind = DockerConnectionKind.Https,
                                 };

            dbContext.ObservedImages.AddRange(ownObservedImage, manualObservedImage);
            dbContext.ContainerSnapshots.Add(new ContainerSnapshot
                                             {
                                                 DockerInstance = dockerInstance,
                                                 ImageVersionId = runtimeImageVersion.Id,
                                                 ContainerId = "container-api",
                                                 Name = "company-api",
                                                 Status = ContainerRuntimeStatus.Running,
                                                 IsRunning = true,
                                             });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var observedImages = await service.GetObservedImagesAsync(CancellationToken.None)
                                              .ConfigureAwait(false);
            var observedDetail = await service.GetObservedImageDetailAsync(ownObservedImage.Id, CancellationToken.None)
                                              .ConfigureAwait(false);
            var runtimeContainers = await service.GetRuntimeContainersAsync(CancellationToken.None)
                                                 .ConfigureAwait(false);
            var runtimeDetail = await service.GetRuntimeContainerDetailAsync(dockerInstance.Id, "container-api", CancellationToken.None)
                                             .ConfigureAwait(false);

            Assert.AreEqual(ownObservedImage.Id,
                            observedImages[0].Id,
                            "Own images must be prioritized ahead of manual images in the observed image list");
            Assert.AreEqual(1,
                            observedImages[0].LinkedRuntimeContainerCount,
                            "Own images must show the number of linked runtime containers by repository");
            Assert.IsNotNull(observedDetail, "The own observed image detail must be returned");
            Assert.HasCount(1,
                            observedDetail.LinkedRuntimeContainers,
                            "The own observed image detail must list linked runtime containers");
            Assert.AreEqual(ownObservedImage.Id,
                            runtimeContainers.Single().LinkedObservedImageId,
                            "Runtime containers must link back to the matching discovered own image");
            Assert.IsNotNull(runtimeDetail, "The runtime container detail must be returned");
            Assert.AreEqual(ownObservedImage.Id,
                            runtimeDetail.LinkedObservedImageId,
                            "The runtime container detail must keep the link to the matching discovered own image");
        }
    }

    /// <summary>
    /// Verify derived base-runtime findings are projected into observed-image and runtime-container views
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceProjectsDerivedBaseRuntimeAlertsAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var observedImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                 "company/api",
                                                                                                 "1.0.0",
                                                                                                 "sha256:current",
                                                                                                 cancellationToken: CancellationToken.None)
                                                                   .ConfigureAwait(false);
            var runtimeImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                "company/api",
                                                                                                "1.0.0",
                                                                                                "sha256:current",
                                                                                                cancellationToken: CancellationToken.None)
                                                                  .ConfigureAwait(false);
            var observedImage = new ObservedImage
                                {
                                    Name = "Company API",
                                    CurrentImageVersionId = observedImageVersion.Id,
                                    Source = RegistrationSource.Discovery,
                                };
            var dockerInstance = new DockerInstance
                                 {
                                     Name = "Production",
                                     EndpointUri = "https://docker.example.test",
                                     ConnectionKind = DockerConnectionKind.Https,
                                 };
            var scanRun = new ScanRun
                          {
                              Type = ScanRunType.RuntimeContainer,
                              Status = ScanRunStatus.Succeeded,
                              TriggerSource = ScanTriggerSource.Manual,
                              DockerInstance = dockerInstance,
                          };
            var snapshot = new ContainerSnapshot
                           {
                               DockerInstance = dockerInstance,
                               ImageVersionId = runtimeImageVersion.Id,
                               ScanRun = scanRun,
                               ContainerId = "container-api",
                               Name = "api",
                               Status = ContainerRuntimeStatus.Running,
                               IsRunning = true,
                           };

            dbContext.ObservedImages.Add(observedImage);
            dbContext.ContainerSnapshots.Add(snapshot);
            dbContext.UpdateFindings.Add(new UpdateFinding
                                         {
                                             ScanRun = scanRun,
                                             ContainerSnapshot = snapshot,
                                             SubjectImageVersionId = runtimeImageVersion.Id,
                                             Type = UpdateFindingType.DerivedBaseRuntimeUpdate,
                                             Summary = "Own image uses an outdated .NET base runtime",
                                             Details = "Container 'api' on 'Production' appears to use .NET 9.0.13. Latest .NET 9.0 runtime is 9.0.15.",
                                             IsActive = true,
                                         });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var observedImages = await service.GetObservedImagesAsync(CancellationToken.None)
                                              .ConfigureAwait(false);
            var observedDetail = await service.GetObservedImageDetailAsync(observedImage.Id, CancellationToken.None)
                                              .ConfigureAwait(false);
            var runtimeDetail = await service.GetRuntimeContainerDetailAsync(dockerInstance.Id, "container-api", CancellationToken.None)
                                             .ConfigureAwait(false);

            Assert.AreEqual("Own image uses an outdated .NET base runtime",
                            observedImages.Single().BaseRuntimeAlertSummary,
                            "The observed image list must surface the linked base-runtime alert summary");
            Assert.IsNotNull(observedDetail, "The observed image detail must be returned");
            Assert.AreEqual("Own image uses an outdated .NET base runtime",
                            observedDetail.BaseRuntimeAlertSummary,
                            "The observed image detail must surface the linked base-runtime alert summary");
            Assert.Contains(entity => entity.Type == "Base runtime update",
                            observedDetail.UpdateFindings,
                            "The observed image detail must include derived base-runtime findings in the update findings list");
            Assert.IsNotNull(runtimeDetail, "The runtime container detail must be returned");
            Assert.AreEqual("Own image uses an outdated .NET base runtime",
                            runtimeDetail.BaseRuntimeAlertSummary,
                            "The runtime container detail must surface the derived base-runtime alert summary");
        }
    }

    /// <summary>
    /// Verify runtime-container and Docker-instance projections expose current resource usage and recent history
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceResourceUsageProjectionsExposeCurrentValuesAndHistoryAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var imageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("ghcr.io",
                                                                                         "acme/api",
                                                                                         "1.0.0",
                                                                                         "sha256:api",
                                                                                         cancellationToken: CancellationToken.None)
                                                           .ConfigureAwait(false);
            var dockerInstance = new DockerInstance
                                 {
                                     Name = "Metrics Engine",
                                     EndpointUri = "https://docker.example.test",
                                     ConnectionKind = DockerConnectionKind.Https,
                                 };
            var olderTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10);
            var newerTimestamp = DateTimeOffset.UtcNow.AddMinutes(-2);

            dbContext.ContainerSnapshots.Add(new ContainerSnapshot
                                             {
                                                 DockerInstance = dockerInstance,
                                                 ImageVersionId = imageVersion.Id,
                                                 ContainerId = "container-api",
                                                 Name = "api",
                                                 Status = ContainerRuntimeStatus.Running,
                                                 IsRunning = true,
                                                 RecordedAtUtc = newerTimestamp,
                                             });
            dbContext.RuntimeContainerResourceSamples.AddRange(new RuntimeContainerResourceSample
                                                               {
                                                                   DockerInstance = dockerInstance,
                                                                   ContainerId = "container-api",
                                                                   ContainerName = "api",
                                                                   CpuPercent = 12.5m,
                                                                   MemoryUsageBytes = 256 * 1024 * 1024,
                                                                   MemoryLimitBytes = 512 * 1024 * 1024,
                                                                   NetworkRxBytesPerSecond = 2048,
                                                                   NetworkTxBytesPerSecond = 1024,
                                                                   RecordedAtUtc = olderTimestamp,
                                                               },
                                                               new RuntimeContainerResourceSample
                                                               {
                                                                   DockerInstance = dockerInstance,
                                                                   ContainerId = "container-api",
                                                                   ContainerName = "api",
                                                                   CpuPercent = 18.0m,
                                                                   MemoryUsageBytes = 300 * 1024 * 1024,
                                                                   MemoryLimitBytes = 512 * 1024 * 1024,
                                                                   NetworkRxBytesPerSecond = 4096,
                                                                   NetworkTxBytesPerSecond = 1536,
                                                                   RecordedAtUtc = newerTimestamp,
                                                               });
            dbContext.DockerInstanceResourceSamples.AddRange(new DockerInstanceResourceSample
                                                             {
                                                                 DockerInstance = dockerInstance,
                                                                 ContainerCount = 1,
                                                                 CpuPercent = 12.5m,
                                                                 MemoryUsageBytes = 256 * 1024 * 1024,
                                                                 MemoryLimitBytes = 512 * 1024 * 1024,
                                                                 NetworkRxBytesPerSecond = 2048,
                                                                 NetworkTxBytesPerSecond = 1024,
                                                                 RecordedAtUtc = olderTimestamp,
                                                             },
                                                             new DockerInstanceResourceSample
                                                             {
                                                                 DockerInstance = dockerInstance,
                                                                 ContainerCount = 1,
                                                                 CpuPercent = 18.0m,
                                                                 MemoryUsageBytes = 300 * 1024 * 1024,
                                                                 MemoryLimitBytes = 512 * 1024 * 1024,
                                                                 NetworkRxBytesPerSecond = 4096,
                                                                 NetworkTxBytesPerSecond = 1536,
                                                                 RecordedAtUtc = newerTimestamp,
                                                             });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var runtimeContainers = await service.GetRuntimeContainersAsync(CancellationToken.None)
                                                 .ConfigureAwait(false);
            var runtimeDetail = await service.GetRuntimeContainerDetailAsync(dockerInstance.Id, "container-api", CancellationToken.None)
                                             .ConfigureAwait(false);
            var dockerInstances = await service.GetDockerInstancesAsync(CancellationToken.None)
                                               .ConfigureAwait(false);
            var dockerInstanceDetail = await service.GetDockerInstanceDetailAsync(dockerInstance.Id, CancellationToken.None)
                                                    .ConfigureAwait(false);

            Assert.AreEqual(18.0m,
                            runtimeContainers.Single().CurrentResourceUsage?.CpuPercent,
                            "Runtime container list rows must expose the latest CPU value");
            Assert.HasCount(2,
                            runtimeContainers.Single().ResourceUsageHistory,
                            "Runtime container list rows must expose recent resource history for mini charts");
            Assert.IsNotNull(runtimeDetail, "The runtime container detail must be returned");
            Assert.HasCount(2,
                            runtimeDetail.ResourceUsageHistory,
                            "Runtime container detail must expose recent resource history");
            Assert.AreEqual(18.0m,
                            runtimeDetail.CurrentResourceUsage?.CpuPercent,
                            "Runtime container detail must expose the latest resource sample");
            Assert.AreEqual(18.0m,
                            dockerInstances.Single().CurrentResourceUsage?.CpuPercent,
                            "Docker instance list rows must expose the latest aggregated CPU value");
            Assert.AreEqual(512 * 1024 * 1024,
                            dockerInstances.Single().CurrentResourceUsage?.MemoryLimitBytes,
                            "Docker instance list rows must expose host-total memory for the latest sample");
            Assert.IsNotNull(dockerInstanceDetail, "The Docker instance detail must be returned");
            Assert.HasCount(2,
                            dockerInstanceDetail.ResourceUsageHistory,
                            "Docker instance detail must expose recent resource history");
            Assert.AreEqual(18.0m,
                            dockerInstanceDetail.CurrentResourceUsage?.CpuPercent,
                            "Docker instance detail must expose the latest aggregated resource sample");
            Assert.AreEqual(512 * 1024 * 1024,
                            dockerInstanceDetail.CurrentResourceUsage?.MemoryLimitBytes,
                            "Docker instance detail must expose host-total memory for the latest sample");
        }
    }

    /// <summary>
    /// Verify runtime container projections expose resolved semantic version tags for alias tags
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceRuntimeContainersExposeResolvedVersionTagsAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var imageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                         "networlddev/f1-telemetry",
                                                                                         "latest",
                                                                                         "sha256:new",
                                                                                         cancellationToken: CancellationToken.None)
                                                           .ConfigureAwait(false);

            var dockerInstance = new DockerInstance
                                 {
                                     Name = "Production",
                                     EndpointUri = "https://docker.example.test",
                                     ConnectionKind = DockerConnectionKind.Https,
                                 };

            var scanRun = new ScanRun
                          {
                              Type = ScanRunType.RuntimeContainer,
                              Status = ScanRunStatus.Succeeded,
                              TriggerSource = ScanTriggerSource.Manual,
                              DockerInstance = dockerInstance,
                              StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                              CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-4),
                          };

            var snapshot = new ContainerSnapshot
                           {
                               DockerInstance = dockerInstance,
                               ImageVersionId = imageVersion.Id,
                               ScanRun = scanRun,
                               ContainerId = "container-latest",
                               Name = "telemetry",
                               Status = ContainerRuntimeStatus.Running,
                               IsRunning = true,
                               UpdateAssessmentStatus = UpdateAssessmentStatus.UpdateAvailable,
                               UpdateAssessmentMessage = "Update available A newer image is available for tag 'latest'",
                           };

            var updateFinding = new UpdateFinding
                                {
                                    ScanRun = scanRun,
                                    ContainerSnapshot = snapshot,
                                    SubjectImageVersionId = imageVersion.Id,
                                    Type = UpdateFindingType.RuntimeImageUpdate,
                                    Summary = "Update available",
                                    Details = "A newer image is available for tag 'latest'",
                                    IsActive = true,
                                };

            updateFinding.TagCandidates.Add(new TagCandidate
                                            {
                                                Tag = "latest",
                                                Digest = "sha256:new",
                                                Rank = 1,
                                                IsRecommended = true,
                                                Reason = "Update available",
                                            });

            updateFinding.TagCandidates.Add(new TagCandidate
                                            {
                                                Tag = "2.4.1",
                                                Digest = "sha256:new",
                                                Rank = 2,
                                                IsRecommended = false,
                                                PublishedAtUtc = new DateTimeOffset(2025, 06, 02, 12, 00, 00, TimeSpan.Zero),
                                                Reason = "Update available",
                                            });

            updateFinding.TagCandidates.Add(new TagCandidate
                                            {
                                                Tag = "2021.11.28",
                                                Digest = "sha256:new",
                                                Rank = 3,
                                                IsRecommended = false,
                                                PublishedAtUtc = new DateTimeOffset(2021, 11, 28, 12, 00, 00, TimeSpan.Zero),
                                                Reason = "Update available",
                                            });

            dbContext.ContainerSnapshots.Add(snapshot);
            dbContext.UpdateFindings.Add(updateFinding);

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var runtimeContainers = await service.GetRuntimeContainersAsync(CancellationToken.None)
                                                 .ConfigureAwait(false);
            var runtimeDetail = await service.GetRuntimeContainerDetailAsync(dockerInstance.Id, "container-latest", CancellationToken.None)
                                             .ConfigureAwait(false);

            Assert.AreEqual("2.4.1",
                            runtimeContainers.Single().ResolvedVersionTag,
                            "Runtime container rows must expose the semantic version behind a latest alias");
            Assert.IsNotNull(runtimeDetail, "The runtime container detail must be returned");
            Assert.AreEqual("2.4.1",
                            runtimeDetail.ResolvedVersionTag,
                            "Runtime container detail must expose the semantic version behind a latest alias");
            Assert.AreEqual("2.4.1",
                            runtimeDetail.AvailableTagCandidates.Single(entity => entity.Tag == "latest").ResolvedVersionTag,
                            "The matching latest tag candidate must expose the resolved semantic version");
        }
    }

    /// <summary>
    /// Verify runtime container projections expose persisted current and available version tags without relying on update findings
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceRuntimeContainersExposePersistedVersionTagsAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var imageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                         "networlddev/f1-telemetry",
                                                                                         "latest",
                                                                                         "sha256:current",
                                                                                         cancellationToken: CancellationToken.None)
                                                           .ConfigureAwait(false);

            var dockerInstance = new DockerInstance
                                 {
                                     Name = "Production",
                                     EndpointUri = "https://docker.example.test",
                                     ConnectionKind = DockerConnectionKind.Https,
                                 };

            var scanRun = new ScanRun
                          {
                              Type = ScanRunType.RuntimeContainer,
                              Status = ScanRunStatus.Succeeded,
                              TriggerSource = ScanTriggerSource.Manual,
                              DockerInstance = dockerInstance,
                              StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                              CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-4),
                          };

            dbContext.ContainerSnapshots.Add(new ContainerSnapshot
                                             {
                                                 DockerInstance = dockerInstance,
                                                 ImageVersionId = imageVersion.Id,
                                                 ScanRun = scanRun,
                                                 ContainerId = "container-latest",
                                                 Name = "telemetry",
                                                 Status = ContainerRuntimeStatus.Running,
                                                 IsRunning = true,
                                                 UpdateAssessmentStatus = UpdateAssessmentStatus.UpdateAvailable,
                                                 UpdateAssessmentMessage = "Update available",
                                                 ResolvedVersionTag = "2.4.1",
                                                 AvailableUpdateVersionTag = "2.5.0",
                                             });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var runtimeContainers = await service.GetRuntimeContainersAsync(CancellationToken.None)
                                                 .ConfigureAwait(false);
            var runtimeDetail = await service.GetRuntimeContainerDetailAsync(dockerInstance.Id, "container-latest", CancellationToken.None)
                                             .ConfigureAwait(false);

            Assert.AreEqual("2.4.1",
                            runtimeContainers.Single().ResolvedVersionTag,
                            "Runtime container rows must expose the persisted resolved current version tag");
            Assert.AreEqual("2.5.0",
                            runtimeContainers.Single().AvailableUpdateVersionTag,
                            "Runtime container rows must expose the persisted available update version tag");
            Assert.IsNotNull(runtimeDetail, "The runtime container detail must be returned");
            Assert.AreEqual("2.4.1",
                            runtimeDetail.ResolvedVersionTag,
                            "Runtime container detail must expose the persisted resolved current version tag");
            Assert.AreEqual("2.5.0",
                            runtimeDetail.AvailableUpdateVersionTag,
                            "Runtime container detail must expose the persisted available update version tag");
        }
    }

    /// <summary>
    /// Verify current runtime container projections ignore stale snapshots from older scans
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceRuntimeContainersExcludeSnapshotsFromOlderScansAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var imageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                         "company/api",
                                                                                         "1.0.0",
                                                                                         "sha256:api",
                                                                                         cancellationToken: CancellationToken.None)
                                                           .ConfigureAwait(false);
            var dockerInstance = new DockerInstance
                                 {
                                     Name = "Production",
                                     EndpointUri = "https://docker.example.test",
                                     ConnectionKind = DockerConnectionKind.Https,
                                 };
            var olderScanRun = new ScanRun
                               {
                                   Type = ScanRunType.RuntimeContainer,
                                   Status = ScanRunStatus.Succeeded,
                                   TriggerSource = ScanTriggerSource.Manual,
                                   DockerInstance = dockerInstance,
                                   StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
                                   CompletedAtUtc = DateTimeOffset.UtcNow.AddHours(-2).AddMinutes(1),
                               };
            var latestScanRun = new ScanRun
                                {
                                    Type = ScanRunType.RuntimeContainer,
                                    Status = ScanRunStatus.Succeeded,
                                    TriggerSource = ScanTriggerSource.Manual,
                                    DockerInstance = dockerInstance,
                                    StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                                    CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-9),
                                };

            dbContext.ContainerSnapshots.AddRange(new ContainerSnapshot
                                                  {
                                                      DockerInstance = dockerInstance,
                                                      ImageVersionId = imageVersion.Id,
                                                      ScanRun = olderScanRun,
                                                      ContainerId = "container-old",
                                                      Name = "api-old",
                                                      Status = ContainerRuntimeStatus.Running,
                                                      IsRunning = true,
                                                      RecordedAtUtc = olderScanRun.CompletedAtUtc.GetValueOrDefault(),
                                                  },
                                                  new ContainerSnapshot
                                                  {
                                                      DockerInstance = dockerInstance,
                                                      ImageVersionId = imageVersion.Id,
                                                      ScanRun = latestScanRun,
                                                      ContainerId = "container-new",
                                                      Name = "api-new",
                                                      Status = ContainerRuntimeStatus.Running,
                                                      IsRunning = true,
                                                      RecordedAtUtc = latestScanRun.CompletedAtUtc.GetValueOrDefault(),
                                                  });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var runtimeContainers = await service.GetRuntimeContainersAsync(CancellationToken.None)
                                                 .ConfigureAwait(false);
            var dockerInstanceDetail = await service.GetDockerInstanceDetailAsync(dockerInstance.Id, CancellationToken.None)
                                                    .ConfigureAwait(false);

            Assert.HasCount(1,
                            runtimeContainers,
                            "Current runtime container projections must only expose containers from the latest runtime scan");
            Assert.AreEqual("container-new",
                            runtimeContainers.Single().ContainerId,
                            "Current runtime container projections must ignore stale snapshots from older scans");
            Assert.IsNotNull(dockerInstanceDetail, "The Docker instance detail must be returned");
            Assert.HasCount(1,
                            dockerInstanceDetail.RuntimeContainers,
                            "Docker instance detail must also exclude stale snapshots from older scans");
            Assert.AreEqual("container-new",
                            dockerInstanceDetail.RuntimeContainers.Single().ContainerId,
                            "Docker instance detail must keep only the latest scan inventory");
        }
    }

    /// <summary>
    /// Verify concurrent UI reads on the same service instance do not overlap the scoped DbContext
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceConcurrentReadsCompleteWithoutDbContextOverlapAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var repository = new RegistryRepository
                             {
                                 Registry = "docker.io",
                                 Repository = "company/app",
                             };
            var imageVersion = new ImageVersion
                               {
                                   RegistryRepository = repository,
                                   Tag = "1.0.0",
                                   Digest = "sha256:app",
                               };
            var observedImage = new ObservedImage
                                {
                                    Name = "Company App",
                                    CurrentImageVersion = imageVersion,
                                };

            dbContext.ObservedImages.Add(observedImage);

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var dashboardTask = service.GetDashboardAsync(CancellationToken.None);
            var observedImagesTask = service.GetObservedImagesAsync(CancellationToken.None);

            await Task.WhenAll(dashboardTask, observedImagesTask)
                      .ConfigureAwait(false);

            Assert.AreEqual(1,
                            dashboardTask.Result.ObservedImageCount,
                            "Concurrent dashboard reads must still report the seeded observed image");
            Assert.HasCount(1,
                            observedImagesTask.Result,
                            "Concurrent observed image reads must complete successfully");
        }
    }

    /// <summary>
    /// Verify direct observed-image .NET runtime findings are preferred over runtime-linked fallback findings
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceObservedImagesPreferDirectDerivedBaseRuntimeFindingsAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var runtimeImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("ghcr.io",
                                                                                                "acme/api",
                                                                                                "1.0.0",
                                                                                                "sha256:api",
                                                                                                cancellationToken: CancellationToken.None)
                                                                  .ConfigureAwait(false);
            var observedImage = new ObservedImage
                                {
                                    Name = "Acme API",
                                    Source = RegistrationSource.Discovery,
                                    CurrentImageVersionId = runtimeImageVersion.Id,
                                };
            var dockerInstance = new DockerInstance
                                 {
                                     Name = "Production",
                                     EndpointUri = "https://docker.example.test",
                                     ConnectionKind = DockerConnectionKind.Https,
                                 };
            var runtimeScanRun = new ScanRun
                                 {
                                     Type = ScanRunType.RuntimeContainer,
                                     Status = ScanRunStatus.Succeeded,
                                     TriggerSource = ScanTriggerSource.Manual,
                                     DockerInstance = dockerInstance,
                                 };
            var observedScanRun = new ScanRun
                                  {
                                      Type = ScanRunType.ObservedImage,
                                      Status = ScanRunStatus.Succeeded,
                                      TriggerSource = ScanTriggerSource.Manual,
                                      ObservedImage = observedImage,
                                  };
            var snapshot = new ContainerSnapshot
                           {
                               DockerInstance = dockerInstance,
                               ImageVersionId = runtimeImageVersion.Id,
                               ScanRun = runtimeScanRun,
                               ContainerId = "container-api",
                               Name = "api",
                               Status = ContainerRuntimeStatus.Running,
                               IsRunning = true,
                           };

            dbContext.ObservedImages.Add(observedImage);
            dbContext.ContainerSnapshots.Add(snapshot);
            dbContext.UpdateFindings.AddRange(new UpdateFinding
                                              {
                                                  ScanRun = observedScanRun,
                                                  ObservedImage = observedImage,
                                                  SubjectImageVersionId = runtimeImageVersion.Id,
                                                  Type = UpdateFindingType.DerivedBaseRuntimeUpdate,
                                                  Summary = "Own image uses an outdated .NET base runtime",
                                                  Details = "Observed image direct finding",
                                                  IsActive = true,
                                              },
                                              new UpdateFinding
                                              {
                                                  ScanRun = runtimeScanRun,
                                                  ContainerSnapshot = snapshot,
                                                  SubjectImageVersionId = runtimeImageVersion.Id,
                                                  Type = UpdateFindingType.DerivedBaseRuntimeUpdate,
                                                  Summary = "Runtime fallback finding",
                                                  Details = "Linked runtime fallback finding",
                                                  IsActive = true,
                                              });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var observedImages = await service.GetObservedImagesAsync(CancellationToken.None)
                                              .ConfigureAwait(false);
            var observedDetail = await service.GetObservedImageDetailAsync(observedImage.Id, CancellationToken.None)
                                              .ConfigureAwait(false);
            var runtimeDetail = await service.GetRuntimeContainerDetailAsync(dockerInstance.Id, "container-api", CancellationToken.None)
                                             .ConfigureAwait(false);

            Assert.AreEqual("Own image uses an outdated .NET base runtime",
                            observedImages.Single().BaseRuntimeAlertSummary,
                            "The observed image list must prefer the direct observed-image base-runtime finding");
            Assert.IsNotNull(observedDetail, "The observed image detail must be returned");
            Assert.AreEqual("Own image uses an outdated .NET base runtime",
                            observedDetail.BaseRuntimeAlertSummary,
                            "The observed image detail must prefer the direct observed-image base-runtime finding");
            Assert.IsNotNull(runtimeDetail, "The runtime container detail must be returned");
            Assert.AreEqual("Runtime fallback finding",
                            runtimeDetail.BaseRuntimeAlertSummary,
                            "Runtime container detail must keep the runtime-specific finding when it exists");
        }
    }

    /// <summary>
    /// Verify GetManualObservedImagesAsync returns only manually-registered images
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceGetManualObservedImagesReturnsOnlyManualImagesAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var manualVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                          "company/manual",
                                                                                          "1.0.0",
                                                                                          "sha256:manual",
                                                                                          cancellationToken: CancellationToken.None)
                                                            .ConfigureAwait(false);
            var discoveryVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                             "company/own",
                                                                                             "2.0.0",
                                                                                             "sha256:own",
                                                                                             cancellationToken: CancellationToken.None)
                                                               .ConfigureAwait(false);

            dbContext.ObservedImages.AddRange(new ObservedImage
                                              {
                                                  Name = "Manual Image",
                                                  CurrentImageVersionId = manualVersion.Id,
                                                  Source = RegistrationSource.Manual,
                                              },
                                              new ObservedImage
                                              {
                                                  Name = "Own Image",
                                                  CurrentImageVersionId = discoveryVersion.Id,
                                                  Source = RegistrationSource.Discovery,
                                              });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var manualImages = await service.GetManualObservedImagesAsync(CancellationToken.None)
                                            .ConfigureAwait(false);

            Assert.HasCount(1,
                            manualImages,
                            "GetManualObservedImagesAsync must return only manually-registered images");
            Assert.AreEqual("Manual Image",
                            manualImages.Single().Name,
                            "GetManualObservedImagesAsync must return the manually-registered image by name");
            Assert.IsFalse(manualImages.Single().IsOwnImage,
                           "Manual images must not be flagged as own images");
        }
    }

    /// <summary>
    /// Verify GetDiscoveryObservedImagesAsync returns only discovery-owned images
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceGetDiscoveryObservedImagesReturnsOnlyDiscoveryImagesAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var manualVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                          "company/manual",
                                                                                          "1.0.0",
                                                                                          "sha256:manual",
                                                                                          cancellationToken: CancellationToken.None)
                                                            .ConfigureAwait(false);
            var discoveryVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                             "company/own",
                                                                                             "2.0.0",
                                                                                             "sha256:own",
                                                                                             cancellationToken: CancellationToken.None)
                                                               .ConfigureAwait(false);

            dbContext.ObservedImages.AddRange(new ObservedImage
                                              {
                                                  Name = "Manual Image",
                                                  CurrentImageVersionId = manualVersion.Id,
                                                  Source = RegistrationSource.Manual,
                                              },
                                              new ObservedImage
                                              {
                                                  Name = "Own Image",
                                                  CurrentImageVersionId = discoveryVersion.Id,
                                                  Source = RegistrationSource.Discovery,
                                              });

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var discoveryImages = await service.GetDiscoveryObservedImagesAsync(CancellationToken.None)
                                               .ConfigureAwait(false);

            Assert.HasCount(1,
                            discoveryImages,
                            "GetDiscoveryObservedImagesAsync must return only discovery-owned images");
            Assert.AreEqual("Own Image",
                            discoveryImages.Single().Name,
                            "GetDiscoveryObservedImagesAsync must return the discovery-owned image by name");
            Assert.IsTrue(discoveryImages.Single().IsOwnImage,
                          "Discovery images must be flagged as own images");
        }
    }

    /// <summary>
    /// Verify runtime container projections collapse historical snapshots without a runtime scan to the latest per container
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceRuntimeContainersCollapseHistoricalSnapshotsToLatestAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var imageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                         "company/api",
                                                                                         "1.0.0",
                                                                                         "sha256:api",
                                                                                         cancellationToken: CancellationToken.None)
                                                           .ConfigureAwait(false);
            var dockerInstance = new DockerInstance
                                 {
                                     Name = "Production",
                                     EndpointUri = "https://docker.example.test",
                                     ConnectionKind = DockerConnectionKind.Https,
                                 };

            var recordedAt = DateTimeOffset.UtcNow.AddHours(-3);

            for (var index = 0; index < 5; index++)
            {
                dbContext.ContainerSnapshots.Add(new ContainerSnapshot
                                                 {
                                                     DockerInstance = dockerInstance,
                                                     ImageVersionId = imageVersion.Id,
                                                     ContainerId = "container-history",
                                                     Name = $"api-{index}",
                                                     Status = ContainerRuntimeStatus.Running,
                                                     IsRunning = true,
                                                     RecordedAtUtc = recordedAt.AddMinutes(index * 10),
                                                 });
            }

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var runtimeContainers = await service.GetRuntimeContainersAsync(CancellationToken.None)
                                                 .ConfigureAwait(false);

            Assert.HasCount(1,
                            runtimeContainers,
                            "Historical snapshots without a runtime scan must collapse to a single latest projection per container");
            Assert.AreEqual("api-4",
                            runtimeContainers.Single().ContainerName,
                            "The collapsed runtime container projection must expose the most recently recorded snapshot");
        }
    }

    /// <summary>
    /// Verify the dashboard reports the disabled hint when vulnerability scanning is switched off
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceDashboardScanningDisabledReportsDisabledHintAsync()
    {
        var dashboard = await ReadDashboardAsync(CreateOptionsMonitor(enabled: false, VulnerabilityProviderKind.None), scanRun: null).ConfigureAwait(false);

        Assert.IsFalse(dashboard.VulnerabilityScanningEnabled, "The dashboard must report vulnerability scanning as disabled when the option is switched off");
        Assert.AreEqual(VulnerabilityConfigurationHintKind.ScanningDisabled,
                        dashboard.VulnerabilityConfigurationHint,
                        "Disabled vulnerability scanning must resolve to the disabled configuration hint");
    }

    /// <summary>
    /// Verify the dashboard reports no hint when a Trivy run reported a missing base URL, since an enabled Trivy configuration always has one
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ApplicationViewServiceDashboardTrivyNotConfiguredMessageReportsNoHintAsync()
    {
        var scanRun = CreateVulnerabilityScanRun(ScanRunStatus.Partial,
                                                 VulnerabilityProviderMessages.TrivyBaseUrlNotConfigured,
                                                 DateTimeOffset.UtcNow.AddMinutes(-30));
        var dashboard = await ReadDashboardAsync(CreateOptionsMonitor(enabled: true, VulnerabilityProviderKind.Trivy), scanRun).ConfigureAwait(false);

        Assert.IsTrue(dashboard.VulnerabilityScanningEnabled, "The dashboard must report vulnerability scanning as enabled when the option is switched on");
        Assert.AreEqual(VulnerabilityConfigurationHintKind.None,
                        dashboard.VulnerabilityConfigurationHint,
                        "A Trivy run reporting a missing base URL must not resolve to a hint, as the configured URL cannot be missing");
    }

    /// <summary>
    /// Verify the dashboard names the missing Docker Hub credentials when the latest vulnerability run reported them
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceDashboardDockerScoutNotConfiguredReportsCredentialsHintAsync()
    {
        var scanRun = CreateVulnerabilityScanRun(ScanRunStatus.Failed,
                                                 VulnerabilityProviderMessages.DockerScoutCredentialsNotConfigured,
                                                 DateTimeOffset.UtcNow.AddMinutes(-30));
        var dashboard = await ReadDashboardAsync(CreateOptionsMonitor(enabled: true, VulnerabilityProviderKind.DockerScout), scanRun).ConfigureAwait(false);

        Assert.AreEqual(VulnerabilityConfigurationHintKind.DockerHubCredentialsMissing,
                        dashboard.VulnerabilityConfigurationHint,
                        "A Docker Scout run reporting missing credentials must resolve to the Docker Hub configuration hint");
    }

    /// <summary>
    /// Verify the dashboard reports no hint when the latest vulnerability run succeeded
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceDashboardConfiguredProviderReportsNoHintAsync()
    {
        var scanRun = CreateVulnerabilityScanRun(ScanRunStatus.Succeeded, errorMessage: null, DateTimeOffset.UtcNow.AddMinutes(-30));
        var dashboard = await ReadDashboardAsync(CreateOptionsMonitor(enabled: true, VulnerabilityProviderKind.Trivy), scanRun).ConfigureAwait(false);

        Assert.AreEqual(VulnerabilityConfigurationHintKind.None,
                        dashboard.VulnerabilityConfigurationHint,
                        "A correctly configured provider must not surface a configuration hint");
    }

    /// <summary>
    /// Verify the dashboard ignores unrelated failures of the latest vulnerability run
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceDashboardUnrelatedFailureReportsNoHintAsync()
    {
        var scanRun = CreateVulnerabilityScanRun(ScanRunStatus.Failed, "Trivy scan timed out after 30 seconds", DateTimeOffset.UtcNow.AddMinutes(-30));
        var dashboard = await ReadDashboardAsync(CreateOptionsMonitor(enabled: true, VulnerabilityProviderKind.Trivy), scanRun).ConfigureAwait(false);

        Assert.AreEqual(VulnerabilityConfigurationHintKind.None,
                        dashboard.VulnerabilityConfigurationHint,
                        "A failure unrelated to the provider configuration must not surface a configuration hint");
    }

    /// <summary>
    /// Verify the next scheduled vulnerability refresh is derived from the latest run and the configured interval
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceVulnerabilityScheduleWithRecordedRunReturnsEstimatedNextRefreshAsync()
    {
        var startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-45);
        var scanRun = CreateVulnerabilityScanRun(ScanRunStatus.Succeeded, errorMessage: null, startedAtUtc);
        var schedule = await ReadVulnerabilityScheduleAsync(CreateOptionsMonitor(enabled: true, VulnerabilityProviderKind.Trivy, refreshIntervalMinutes: 120), scanRun).ConfigureAwait(false);

        Assert.IsTrue(schedule.VulnerabilityScanningEnabled, "The schedule must report vulnerability scanning as enabled when the option is switched on");
        Assert.AreEqual(startedAtUtc.AddMinutes(120),
                        schedule.NextVulnerabilityRefreshUtc,
                        "The next scheduled refresh must be the start of the latest run plus the configured refresh interval");
    }

    /// <summary>
    /// Verify the next scheduled vulnerability refresh stays unset when no run was recorded yet
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceVulnerabilityScheduleWithoutRecordedRunReturnsNoNextRefreshAsync()
    {
        var schedule = await ReadVulnerabilityScheduleAsync(CreateOptionsMonitor(enabled: true, VulnerabilityProviderKind.Trivy), scanRun: null).ConfigureAwait(false);

        Assert.IsTrue(schedule.VulnerabilityScanningEnabled, "The schedule must report vulnerability scanning as enabled when the option is switched on");
        Assert.IsNull(schedule.NextVulnerabilityRefreshUtc, "The next scheduled refresh must stay unset when no vulnerability run was recorded yet");
    }

    /// <summary>
    /// Verify the vulnerability schedule stays empty when scanning is disabled
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceVulnerabilityScheduleWithDisabledScanningReturnsDisabledScheduleAsync()
    {
        var scanRun = CreateVulnerabilityScanRun(ScanRunStatus.Succeeded, errorMessage: null, DateTimeOffset.UtcNow.AddMinutes(-45));
        var schedule = await ReadVulnerabilityScheduleAsync(CreateOptionsMonitor(enabled: false, VulnerabilityProviderKind.None), scanRun).ConfigureAwait(false);

        Assert.IsFalse(schedule.VulnerabilityScanningEnabled, "The schedule must report vulnerability scanning as disabled when the option is switched off");
        Assert.IsNull(schedule.NextVulnerabilityRefreshUtc, "The next scheduled refresh must stay unset when vulnerability scanning is disabled");
    }

    /// <summary>
    /// Verify the Docker instance list masks the user info of a stored endpoint written before the masking existed
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceDockerInstancesMaskStoredEndpointUserInfoAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            dbContext.DockerInstances.Add(new DockerInstance
                                          {
                                              Name = "Credentialed",
                                              EndpointUri = "https://user:secret@docker.example.test",
                                              ConnectionKind = DockerConnectionKind.Https,
                                          });
            dbContext.DockerInstances.Add(new DockerInstance
                                          {
                                              Name = "Local",
                                              EndpointUri = "unix:///var/run/docker.sock",
                                              ConnectionKind = DockerConnectionKind.UnixSocket,
                                          });
            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var dockerInstances = await service.GetDockerInstancesAsync(CancellationToken.None)
                                               .ConfigureAwait(false);

            Assert.AreEqual("https://***@docker.example.test/",
                            dockerInstances.Single(item => item.Name == "Credentialed").EndpointUri,
                            "A stored endpoint with user info must be listed with the user info masked");
            Assert.AreEqual("unix:///var/run/docker.sock",
                            dockerInstances.Single(item => item.Name == "Local").EndpointUri,
                            "A stored endpoint without user info must be listed unchanged");
        }
    }

    /// <summary>
    /// Verify the Docker instance detail masks the user info of a stored endpoint written before the masking existed
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceDockerInstanceDetailMasksStoredEndpointUserInfoAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            dbContext.DockerInstances.Add(new DockerInstance
                                          {
                                              Name = "Credentialed",
                                              EndpointUri = "https://user:secret@docker.example.test",
                                              ConnectionKind = DockerConnectionKind.Https,
                                          });
            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var instanceId = await dbContext.DockerInstances.Select(entity => entity.Id)
                                                            .SingleAsync(TestContext.CancellationToken)
                                                            .ConfigureAwait(false);
            var detail = await service.GetDockerInstanceDetailAsync(instanceId, CancellationToken.None)
                                      .ConfigureAwait(false);

            Assert.IsNotNull(detail, "The Docker instance detail must be returned");
            Assert.AreEqual("https://***@docker.example.test/",
                            detail.EndpointUri,
                            "A stored endpoint with user info must be shown with the user info masked");
        }
    }

    /// <summary>
    /// Verify an already masked stored endpoint is shown unchanged in the list and the detail
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationViewServiceDockerInstancesKeepAlreadyMaskedEndpointAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            dbContext.DockerInstances.Add(new DockerInstance
                                          {
                                              Name = "Masked",
                                              EndpointUri = "https://***@docker.example.test/",
                                              ConnectionKind = DockerConnectionKind.Https,
                                          });
            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));
            var dockerInstances = await service.GetDockerInstancesAsync(CancellationToken.None)
                                               .ConfigureAwait(false);
            var detail = await service.GetDockerInstanceDetailAsync(dockerInstances.Single().Id, CancellationToken.None)
                                      .ConfigureAwait(false);

            Assert.AreEqual("https://***@docker.example.test/",
                            dockerInstances.Single().EndpointUri,
                            "An already masked endpoint must be listed unchanged");
            Assert.IsNotNull(detail, "The Docker instance detail must be returned");
            Assert.AreEqual("https://***@docker.example.test/",
                            detail.EndpointUri,
                            "An already masked endpoint must be shown unchanged in the detail");
        }
    }

    /// <summary>
    /// Verify the detail lists the 20 most recent action runs of the container's snapshots on its instance, newest first, with their fields
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC10")]
    public async Task ApplicationViewServiceGetRuntimeContainerDetailAsyncLoadsTwentyMostRecentActionRunsOfContainerAsync()
    {
        var baseTime = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            var imageVersion = await new ImageCatalogRepository(dbContext).GetOrCreateImageVersionAsync("docker.io",
                                                                                                        "company/api",
                                                                                                        "1.0.0",
                                                                                                        "sha256:api",
                                                                                                        cancellationToken: TestContext.CancellationToken)
                                                                          .ConfigureAwait(false);
            var dockerInstance = CreateActionHistoryInstance("Production");
            var otherInstance = CreateActionHistoryInstance("Staging");
            var olderSnapshot = CreateActionHistorySnapshot(dockerInstance, imageVersion.Id, "container-api", baseTime.AddDays(-1));
            var currentSnapshot = CreateActionHistorySnapshot(dockerInstance, imageVersion.Id, "container-api", baseTime);
            var otherContainerSnapshot = CreateActionHistorySnapshot(dockerInstance, imageVersion.Id, "container-db", baseTime);
            var otherInstanceSnapshot = CreateActionHistorySnapshot(otherInstance, imageVersion.Id, "container-api", baseTime);

            dbContext.DockerInstances.AddRange(dockerInstance, otherInstance);
            dbContext.ContainerSnapshots.AddRange(olderSnapshot, currentSnapshot, otherContainerSnapshot, otherInstanceSnapshot);
            dbContext.ContainerActionRuns.AddRange(Enumerable.Range(0, 22)
                                                             .Select(index => CreateActionHistoryRun(index % 2 == 0 ? currentSnapshot : olderSnapshot, baseTime.AddMinutes(index))));
            dbContext.ContainerActionRuns.Add(CreateActionHistoryRun(otherContainerSnapshot, baseTime.AddHours(1)));
            dbContext.ContainerActionRuns.Add(CreateActionHistoryRun(otherInstanceSnapshot, baseTime.AddHours(2)));

            await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     CreateOptionsMonitor(),
                                                     new SharedBaseImageQueryService(dbContext));

            var detail = await service.GetRuntimeContainerDetailAsync(dockerInstance.Id, "container-api", TestContext.CancellationToken)
                                      .ConfigureAwait(false);

            Assert.IsNotNull(detail, "The runtime container detail must be returned");
            Assert.AreSequenceEqual(Enumerable.Range(2, 20)
                                              .Reverse()
                                              .Select(index => baseTime.AddMinutes(index))
                                              .ToList(),
                                    detail.ActionHistory.Select(run => run.RequestedAtUtc).ToList(),
                                    "The history must hold the 20 most recent runs of the container on its instance, newest first, from every snapshot of the container");
            Assert.AreEqual("Restart", detail.ActionHistory[0].Action, "A run must show the display name of its action");
            Assert.AreEqual("Succeeded", detail.ActionHistory[0].Status, "A run must show its status");
            Assert.AreEqual(baseTime.AddMinutes(21).AddSeconds(5), detail.ActionHistory[0].CompletedAtUtc, "A run must show its completion time");
            Assert.AreEqual("UI", detail.ActionHistory[0].RequestedBy, "A run must show who requested it");
            Assert.AreEqual("message 21", detail.ActionHistory[0].Message, "A run must show its stored message");
        }
    }

    /// <summary>
    /// Verify a base used by one enabled observed image and no container is not listed
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task ApplicationViewServiceGetBaseImagesSingleObservedImageIsExcludedAsync()
    {
        var dbContext = CreateSharedBaseDbContext();

        await using (dbContext.ConfigureAwait(false))
        {
            var baseVersion = await CreateSharedBaseVersionAsync(dbContext, "library/debian", "12", TestContext.CancellationToken).ConfigureAwait(false);
            var appVersion = await CreateSharedBaseVersionAsync(dbContext, "company/app-a", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);

            AddObservedImage(dbContext, "App A", appVersion.Id);
            AddBaseRelationship(dbContext, appVersion.Id, baseVersion.Id);

            await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            var baseImages = await CreateSharedBaseService(dbContext).GetBaseImagesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsEmpty(baseImages, "A base used by a single enabled observed image and no container must not be listed as shared");
        }
    }

    /// <summary>
    /// Verify the navigation flag is false when the only base is unshared
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task ApplicationViewServiceHasBaseImagesOnlyUnsharedBaseReturnsFalseAsync()
    {
        var dbContext = CreateSharedBaseDbContext();

        await using (dbContext.ConfigureAwait(false))
        {
            var baseVersion = await CreateSharedBaseVersionAsync(dbContext, "library/debian", "12", TestContext.CancellationToken).ConfigureAwait(false);
            var appVersion = await CreateSharedBaseVersionAsync(dbContext, "company/app-a", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);

            AddObservedImage(dbContext, "App A", appVersion.Id);
            AddBaseRelationship(dbContext, appVersion.Id, baseVersion.Id);

            await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            var hasBaseImages = await CreateSharedBaseService(dbContext).HasBaseImagesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsFalse(hasBaseImages, "The navigation flag must be false while the only base is used by a single enabled image and no container");
        }
    }

    /// <summary>
    /// Verify the dashboard base image count includes only shared bases
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task ApplicationViewServiceDashboardCountsOnlySharedBaseImagesAsync()
    {
        var dbContext = CreateSharedBaseDbContext();

        await using (dbContext.ConfigureAwait(false))
        {
            var sharedBase = await CreateSharedBaseVersionAsync(dbContext, "library/debian", "12", TestContext.CancellationToken).ConfigureAwait(false);
            var unsharedBase = await CreateSharedBaseVersionAsync(dbContext, "library/alpine", "3", TestContext.CancellationToken).ConfigureAwait(false);
            var appA = await CreateSharedBaseVersionAsync(dbContext, "company/app-a", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);
            var appB = await CreateSharedBaseVersionAsync(dbContext, "company/app-b", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);
            var appC = await CreateSharedBaseVersionAsync(dbContext, "company/app-c", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);

            AddObservedImage(dbContext, "App A", appA.Id);
            AddObservedImage(dbContext, "App B", appB.Id);
            AddObservedImage(dbContext, "App C", appC.Id);
            AddBaseRelationship(dbContext, appA.Id, sharedBase.Id);
            AddBaseRelationship(dbContext, appB.Id, sharedBase.Id);
            AddBaseRelationship(dbContext, appC.Id, unsharedBase.Id);

            await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            var dashboard = await CreateSharedBaseService(dbContext).GetDashboardAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(1, dashboard.BaseImageCount, "The dashboard must count only the base used by two enabled images");
        }
    }

    /// <summary>
    /// Verify a base used by one enabled and one disabled image is not shared
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ApplicationViewServiceGetBaseImagesEnabledAndDisabledImageIsNotSharedAsync()
    {
        var dbContext = CreateSharedBaseDbContext();

        await using (dbContext.ConfigureAwait(false))
        {
            var baseVersion = await CreateSharedBaseVersionAsync(dbContext, "library/debian", "12", TestContext.CancellationToken).ConfigureAwait(false);
            var appA = await CreateSharedBaseVersionAsync(dbContext, "company/app-a", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);
            var appB = await CreateSharedBaseVersionAsync(dbContext, "company/app-b", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);

            AddObservedImage(dbContext, "App A", appA.Id);
            AddObservedImage(dbContext, "App B", appB.Id, false);
            AddBaseRelationship(dbContext, appA.Id, baseVersion.Id);
            AddBaseRelationship(dbContext, appB.Id, baseVersion.Id);

            await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            var baseImages = await CreateSharedBaseService(dbContext).GetBaseImagesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsEmpty(baseImages, "A disabled image must not count as the second user of a base");
        }
    }

    /// <summary>
    /// Verify a disabled image is neither counted nor listed as a parent of a shared base
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ApplicationViewServiceGetBaseImagesDisabledImageIsNotListedAsParentAsync()
    {
        var dbContext = CreateSharedBaseDbContext();

        await using (dbContext.ConfigureAwait(false))
        {
            var baseVersion = await CreateSharedBaseVersionAsync(dbContext, "library/debian", "12", TestContext.CancellationToken).ConfigureAwait(false);
            var appA = await CreateSharedBaseVersionAsync(dbContext, "company/app-a", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);
            var appB = await CreateSharedBaseVersionAsync(dbContext, "company/app-b", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);
            var appC = await CreateSharedBaseVersionAsync(dbContext, "company/app-c", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);

            AddObservedImage(dbContext, "App A", appA.Id);
            AddObservedImage(dbContext, "App B", appB.Id);
            AddObservedImage(dbContext, "App C", appC.Id, false);
            AddBaseRelationship(dbContext, appA.Id, baseVersion.Id);
            AddBaseRelationship(dbContext, appB.Id, baseVersion.Id);
            AddBaseRelationship(dbContext, appC.Id, baseVersion.Id);

            await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            var baseImages = await CreateSharedBaseService(dbContext).GetBaseImagesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.HasCount(1, baseImages, "The base used by two enabled images must be listed");
            Assert.AreEqual(2, baseImages[0].ObservedImageCount, "The disabled image must not be counted");
            Assert.AreSequenceEqual(["docker.io/company/app-a:1.0.0@sha256:company/app-a-1.0.0", "docker.io/company/app-b:1.0.0@sha256:company/app-b-1.0.0"],
                                    baseImages[0].ParentImageReferences.ToArray(),
                                    SequenceOrder.InAnyOrder,
                                    "Only the enabled images must be listed as parents");
        }
    }

    /// <summary>
    /// Verify a container running only the version of a disabled image does not make a base shared
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ApplicationViewServiceGetBaseImagesContainerOfDisabledImageIsNotCountedAsync()
    {
        var dbContext = CreateSharedBaseDbContext();

        await using (dbContext.ConfigureAwait(false))
        {
            var baseVersion = await CreateSharedBaseVersionAsync(dbContext, "library/debian", "12", TestContext.CancellationToken).ConfigureAwait(false);
            var appA = await CreateSharedBaseVersionAsync(dbContext, "company/app-a", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);
            var appB = await CreateSharedBaseVersionAsync(dbContext, "company/app-b", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);

            AddObservedImage(dbContext, "App A", appA.Id);
            AddObservedImage(dbContext, "App B", appB.Id, false);
            AddBaseRelationship(dbContext, appA.Id, baseVersion.Id);
            AddBaseRelationship(dbContext, appB.Id, baseVersion.Id);
            AddCurrentContainers(dbContext, appB.Id, "worker");

            await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            var baseImages = await CreateSharedBaseService(dbContext).GetBaseImagesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsEmpty(baseImages, "A container of a disabled image must not count as a user of the base");
        }
    }

    /// <summary>
    /// Verify relationships of a non-current image version contribute no parent, no container and no usage
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task ApplicationViewServiceGetBaseImagesIgnoresNonCurrentChildVersionsAsync()
    {
        var dbContext = CreateSharedBaseDbContext();

        await using (dbContext.ConfigureAwait(false))
        {
            var baseVersion = await CreateSharedBaseVersionAsync(dbContext, "library/debian", "12", TestContext.CancellationToken).ConfigureAwait(false);
            var appA1 = await CreateSharedBaseVersionAsync(dbContext, "company/app-a", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);
            var appA2 = await CreateSharedBaseVersionAsync(dbContext, "company/app-a", "2.0.0", TestContext.CancellationToken).ConfigureAwait(false);
            var appB = await CreateSharedBaseVersionAsync(dbContext, "company/app-b", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);

            AddObservedImage(dbContext, "App A", appA2.Id);
            AddObservedImage(dbContext, "App B", appB.Id);
            AddBaseRelationship(dbContext, appA1.Id, baseVersion.Id);
            AddBaseRelationship(dbContext, appA2.Id, baseVersion.Id);
            AddBaseRelationship(dbContext, appB.Id, baseVersion.Id);
            AddCurrentContainers(dbContext, appA1.Id, "legacy");

            await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            var baseImages = await CreateSharedBaseService(dbContext).GetBaseImagesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.HasCount(1, baseImages, "The base used by two current versions must be listed");
            Assert.AreEqual(2, baseImages[0].ObservedImageCount, "The entry must count the two observed images");
            Assert.AreSequenceEqual(["docker.io/company/app-a:2.0.0@sha256:company/app-a-2.0.0", "docker.io/company/app-b:1.0.0@sha256:company/app-b-1.0.0"],
                                    baseImages[0].ParentImageReferences.ToArray(),
                                    SequenceOrder.InAnyOrder,
                                    "Only the current versions must be listed as parents");
            Assert.IsEmpty(baseImages[0].RuntimeContainers, "A container of a non-current version must not be listed");
        }
    }

    /// <summary>
    /// Verify a container of a non-current image version does not make a base shared
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task ApplicationViewServiceGetBaseImagesNonCurrentVersionContainerDoesNotShareAsync()
    {
        var dbContext = CreateSharedBaseDbContext();

        await using (dbContext.ConfigureAwait(false))
        {
            var baseVersion = await CreateSharedBaseVersionAsync(dbContext, "library/debian", "12", TestContext.CancellationToken).ConfigureAwait(false);
            var appA1 = await CreateSharedBaseVersionAsync(dbContext, "company/app-a", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);
            var appA2 = await CreateSharedBaseVersionAsync(dbContext, "company/app-a", "2.0.0", TestContext.CancellationToken).ConfigureAwait(false);

            AddObservedImage(dbContext, "App A", appA2.Id);
            AddBaseRelationship(dbContext, appA1.Id, baseVersion.Id);
            AddBaseRelationship(dbContext, appA2.Id, baseVersion.Id);
            AddCurrentContainers(dbContext, appA1.Id, "legacy");

            await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            var baseImages = await CreateSharedBaseService(dbContext).GetBaseImagesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsEmpty(baseImages, "A container of a non-current version must not count as a user of the base");
        }
    }

    /// <summary>
    /// Verify a base used by two enabled observed images is listed with their count
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ApplicationViewServiceGetBaseImagesTwoEnabledImagesAreSharedAsync()
    {
        var dbContext = CreateSharedBaseDbContext();

        await using (dbContext.ConfigureAwait(false))
        {
            var baseVersion = await CreateSharedBaseVersionAsync(dbContext, "library/debian", "12", TestContext.CancellationToken).ConfigureAwait(false);
            var appA = await CreateSharedBaseVersionAsync(dbContext, "company/app-a", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);
            var appB = await CreateSharedBaseVersionAsync(dbContext, "company/app-b", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);

            AddObservedImage(dbContext, "App A", appA.Id);
            AddObservedImage(dbContext, "App B", appB.Id);
            AddBaseRelationship(dbContext, appA.Id, baseVersion.Id);
            AddBaseRelationship(dbContext, appB.Id, baseVersion.Id);

            await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            var baseImages = await CreateSharedBaseService(dbContext).GetBaseImagesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.HasCount(1, baseImages, "A base used by two enabled observed images must be listed");
            Assert.AreEqual(2, baseImages[0].ObservedImageCount, "The entry must count both observed images");
            Assert.AreSequenceEqual(["docker.io/company/app-a:1.0.0@sha256:company/app-a-1.0.0", "docker.io/company/app-b:1.0.0@sha256:company/app-b-1.0.0"],
                                    baseImages[0].ParentImageReferences.ToArray(),
                                    SequenceOrder.InAnyOrder,
                                    "Both images must be listed as parents");
        }
    }

    /// <summary>
    /// Verify a base used by one enabled image whose version runs in current containers is listed with exactly those containers
    /// </summary>
    /// <param name="containerCount">Number of current containers</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow(1)]
    [DataRow(2)]
    public async Task ApplicationViewServiceGetBaseImagesOneImageWithContainerIsSharedAsync(int containerCount)
    {
        var dbContext = CreateSharedBaseDbContext();

        await using (dbContext.ConfigureAwait(false))
        {
            var baseVersion = await CreateSharedBaseVersionAsync(dbContext, "library/debian", "12", TestContext.CancellationToken).ConfigureAwait(false);
            var appA = await CreateSharedBaseVersionAsync(dbContext, "company/app-a", "1.0.0", TestContext.CancellationToken).ConfigureAwait(false);
            var containerNames = Enumerable.Range(1, containerCount)
                                           .Select(index => $"worker-{index}")
                                           .ToArray();

            AddObservedImage(dbContext, "App A", appA.Id);
            AddBaseRelationship(dbContext, appA.Id, baseVersion.Id);
            AddCurrentContainers(dbContext, appA.Id, containerNames);

            await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            var baseImages = await CreateSharedBaseService(dbContext).GetBaseImagesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.HasCount(1, baseImages, "A base used by one image running in a current container must be listed");
            Assert.AreEqual(1, baseImages[0].ObservedImageCount, "The entry must count the single observed image");
            Assert.AreSequenceEqual(containerNames,
                                    baseImages[0].RuntimeContainers.Select(container => container.ContainerName).ToArray(),
                                    SequenceOrder.InAnyOrder,
                                    "Exactly the current containers must be listed");
        }
    }

    /// <summary>
    /// Create a Docker instance of the action history test
    /// </summary>
    /// <param name="name">Instance name</param>
    /// <returns>Docker instance</returns>
    private static DockerInstance CreateActionHistoryInstance(string name)
    {
        return new DockerInstance
               {
                   Name = name,
                   EndpointUri = $"https://{name.ToLowerInvariant()}.example.test",
                   ConnectionKind = DockerConnectionKind.Https,
               };
    }

    /// <summary>
    /// Create a container snapshot of the action history test
    /// </summary>
    /// <param name="dockerInstance">Docker instance</param>
    /// <param name="imageVersionId">Image version identifier</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="recordedAtUtc">Recording time</param>
    /// <returns>Snapshot</returns>
    private static ContainerSnapshot CreateActionHistorySnapshot(DockerInstance dockerInstance, Guid imageVersionId, string containerId, DateTimeOffset recordedAtUtc)
    {
        return new ContainerSnapshot
               {
                   DockerInstance = dockerInstance,
                   ImageVersionId = imageVersionId,
                   ContainerId = containerId,
                   Name = containerId,
                   Status = ContainerRuntimeStatus.Running,
                   IsRunning = true,
                   RecordedAtUtc = recordedAtUtc,
               };
    }

    /// <summary>
    /// Create a succeeded restart run of a snapshot, whose message names its request minute
    /// </summary>
    /// <param name="snapshot">Snapshot</param>
    /// <param name="requestedAtUtc">Request time</param>
    /// <returns>Run</returns>
    private static ContainerActionRun CreateActionHistoryRun(ContainerSnapshot snapshot, DateTimeOffset requestedAtUtc)
    {
        return new ContainerActionRun
               {
                   DockerInstance = snapshot.DockerInstance,
                   ContainerSnapshot = snapshot,
                   ActionType = ContainerActionType.RestartContainer,
                   ResourceType = PortainerResourceType.Container,
                   ResourceName = snapshot.Name,
                   Status = ContainerActionStatus.Succeeded,
                   RequestedBy = "UI",
                   RequestedAtUtc = requestedAtUtc,
                   CompletedAtUtc = requestedAtUtc.AddSeconds(5),
                   ErrorMessage = $"message {requestedAtUtc.Minute}",
               };
    }

    #endregion // Methods
}