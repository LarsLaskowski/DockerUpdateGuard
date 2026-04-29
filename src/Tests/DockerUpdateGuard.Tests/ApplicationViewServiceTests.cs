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
                                             Type = UpdateFindingType.BaseImageUpdate,
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

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     new SharedBaseImageQueryService(dbContext));
            var dashboard = await service.GetDashboardAsync(CancellationToken.None)
                                         .ConfigureAwait(false);

            Assert.AreEqual(2,
                            dashboard.ObservedImageCount,
                            "The dashboard must report the observed image count");
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

            await dbContext.SaveChangesAsync(CancellationToken.None)
                           .ConfigureAwait(false);

            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     new SharedBaseImageQueryService(dbContext));

            var detail = await service.GetRuntimeContainerDetailAsync(dockerInstance.Id, "container-a", CancellationToken.None)
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
                            observedImages.First().Id,
                            "Own images must be prioritized ahead of manual images in the observed image list");
            Assert.AreEqual(1,
                            observedImages.First().LinkedRuntimeContainerCount,
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

    #endregion // Methods
}