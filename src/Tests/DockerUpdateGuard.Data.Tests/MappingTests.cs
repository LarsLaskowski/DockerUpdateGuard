using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DockerUpdateGuard.Data.Tests;

/// <summary>
/// Tests for EF Core mappings
/// </summary>
[TestClass]
public class MappingTests
{
    #region Properties

    /// <summary>
    /// Test context provided by MSTest framework
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify important mappings and indexes
    /// </summary>
    [TestMethod]
    public void DockerUpdateGuardDbContextModelHasExpectedMappings()
    {
        using (var database = new SqliteTestDatabase())
        {
            using (var dbContext = database.CreateDbContext())
            {
                var observedImageEntity = dbContext.Model.FindEntityType(typeof(ObservedImage));

                Assert.IsNotNull(observedImageEntity, "Observed image entity must be part of the EF model");

                var observedImageIndex = FindIndex(observedImageEntity, nameof(ObservedImage.CurrentImageVersionId));

                Assert.IsNotNull(observedImageIndex, "Observed image must have an index for the current image version");

                var imageVersionEntity = dbContext.Model.FindEntityType(typeof(ImageVersion));

                Assert.IsNotNull(imageVersionEntity, "Image version entity must be part of the EF model");

                var imageVersionUniqueIndex = FindIndex(imageVersionEntity,
                                                        nameof(ImageVersion.RegistryRepositoryId),
                                                        nameof(ImageVersion.Tag),
                                                        nameof(ImageVersion.Digest));
                Assert.IsNotNull(imageVersionUniqueIndex,
                                 "Image version must have a unique index for repository, tag and digest");
                Assert.IsTrue(imageVersionUniqueIndex.IsUnique,
                              "Image version index for repository, tag and digest must be unique");

                var imageRelationshipEntity = dbContext.Model.FindEntityType(typeof(ImageRelationship));

                Assert.IsNotNull(imageRelationshipEntity, "Image relationship entity must be part of the EF model");

                var imageRelationshipForeignKey = imageRelationshipEntity.GetForeignKeys()
                                                                         .SingleOrDefault(foreignKey => foreignKey.Properties.Single().Name == nameof(ImageRelationship.BaseImageVersionId));

                Assert.IsNotNull(imageRelationshipForeignKey, "Image relationship must have a foreign key to the base image version");
                Assert.AreEqual(DeleteBehavior.Restrict,
                                imageRelationshipForeignKey.DeleteBehavior,
                                "Base image relationships must restrict deleting referenced base versions");
            }
        }
    }

    /// <summary>
    /// Verify the DbContext persists a representative data foundation graph
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerUpdateGuardDbContextPersistenceRoundTripStoresRepresentativeGraphAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var baseImageVersionRepository = new RegistryRepository
                                                 {
                                                     Registry = "docker.io",
                                                     Repository = "library/debian"
                                                 };

                var baseImageVersion = new ImageVersion
                                       {
                                           Digest = "sha256:base",
                                           RegistryRepository = baseImageVersionRepository,
                                           Tag = "12-slim"
                                       };

                var currentImageVersionRepository = new RegistryRepository
                                                    {
                                                        Registry = "docker.io",
                                                        Repository = "company/example-app"
                                                    };

                var currentImageVersion = new ImageVersion
                                          {
                                              Digest = "sha256:app",
                                              RegistryRepository = currentImageVersionRepository,
                                              Tag = "1.0.0"
                                          };

                var observedImage = new ObservedImage
                                    {
                                        CurrentImageVersion = currentImageVersion,
                                        Description = "Primary application image",
                                        Name = "Example App"
                                    };

                var dockerInstance = new DockerInstance
                                     {
                                         ConnectionKind = DockerConnectionKind.Https,
                                         EndpointUri = "https://docker.example.local",
                                         Name = "Production"
                                     };

                var portainerEndpoint = new PortainerEndpoint
                                        {
                                            BaseUrl = "https://portainer.example.local",
                                            DockerInstance = dockerInstance,
                                            ExternalEndpointId = "17",
                                            Name = "Production Portainer"
                                        };

                var scanRun = new ScanRun
                              {
                                  DockerInstance = dockerInstance,
                                  ObservedImage = observedImage,
                                  Status = ScanRunStatus.Succeeded,
                                  Type = ScanRunType.ObservedImage
                              };

                var containerSnapshot = new ContainerSnapshot
                                        {
                                            ContainerId = "container-001",
                                            DockerInstance = dockerInstance,
                                            ImageVersion = currentImageVersion,
                                            IsRunning = true,
                                            Name = "example-app-1",
                                            ScanRun = scanRun,
                                            Status = ContainerRuntimeStatus.Running
                                        };

                var imageRelationship = new ImageRelationship
                                        {
                                            BaseImageVersion = baseImageVersion,
                                            ChildImageVersion = currentImageVersion,
                                            Depth = 1,
                                            RelationshipType = ImageRelationshipType.BaseImage,
                                            ScanRun = scanRun
                                        };

                var updateFinding = new UpdateFinding
                                    {
                                        ContainerSnapshot = containerSnapshot,
                                        DetectedAtUtc = DateTimeOffset.UtcNow,
                                        IsActive = true,
                                        ObservedImage = observedImage,
                                        RecommendedImageVersion = baseImageVersion,
                                        ScanRun = scanRun,
                                        SubjectImageVersion = baseImageVersion,
                                        Summary = "Base image update available",
                                        Type = UpdateFindingType.BaseImageUpdate
                                    };

                var tagCandidate = new TagCandidate
                                   {
                                       Digest = "sha256:base-new",
                                       IsRecommended = true,
                                       Rank = 1,
                                       Reason = "Newer base image tag",
                                       Tag = "12.1-slim",
                                       UpdateFinding = updateFinding
                                   };

                var vulnerabilityFinding = new VulnerabilityFinding
                                           {
                                               AdvisoryId = "CVE-2026-0001",
                                               DetectedAtUtc = DateTimeOffset.UtcNow,
                                               ImageVersion = baseImageVersion,
                                               IsActive = true,
                                               Severity = VulnerabilitySeverity.High,
                                               Summary = "Representative advisory",
                                               Title = "glibc issue"
                                           };

                var containerActionRun = new ContainerActionRun
                                         {
                                             ActionType = ContainerActionType.RestartContainer,
                                             ContainerSnapshot = containerSnapshot,
                                             DockerInstance = dockerInstance,
                                             PortainerEndpoint = portainerEndpoint,
                                             RequestedAtUtc = DateTimeOffset.UtcNow,
                                             ResourceName = "example-app-1",
                                             ResourceType = PortainerResourceType.Container,
                                             Status = ContainerActionStatus.Succeeded
                                         };

                dbContext.AddRange(baseImageVersion,
                                   currentImageVersion,
                                   observedImage,
                                   dockerInstance,
                                   portainerEndpoint,
                                   scanRun,
                                   containerSnapshot,
                                   imageRelationship,
                                   updateFinding,
                                   tagCandidate,
                                   vulnerabilityFinding,
                                   containerActionRun);

                await dbContext.SaveChangesAsync().ConfigureAwait(false);
                dbContext.ChangeTracker.Clear();

                var persistedObservedImage = await dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion)
                                                                           .ThenInclude(entity => entity.RegistryRepository)
                                                                           .SingleAsync(TestContext.CancellationToken)
                                                                           .ConfigureAwait(false);

                var persistedUpdateFinding = await dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                                           .Include(entity => entity.SubjectImageVersion)
                                                                           .ThenInclude(entity => entity.RegistryRepository)
                                                                           .SingleAsync(TestContext.CancellationToken)
                                                                           .ConfigureAwait(false);

                Assert.AreEqual(2,
                                await dbContext.ImageVersions.CountAsync().ConfigureAwait(false),
                                "Two image versions must be stored for the representative graph");
                Assert.AreEqual(1,
                                await dbContext.ContainerActionRuns.CountAsync().ConfigureAwait(false),
                                "One container action run must be stored for the representative graph");
                Assert.AreEqual("Example App",
                                persistedObservedImage.Name,
                                "The observed image name must survive the persistence round-trip");
                Assert.AreEqual("company/example-app",
                                persistedObservedImage.CurrentImageVersion.RegistryRepository.Repository,
                                "The observed image must keep its normalized repository");
                Assert.AreEqual("Base image update available",
                                persistedUpdateFinding.Summary,
                                "The update finding summary must survive the persistence round-trip");
                Assert.HasCount(1,
                                persistedUpdateFinding.TagCandidates,
                                "The update finding must keep its tag candidates");
            }
        }
    }

    /// <summary>
    /// Find an index by property names
    /// </summary>
    /// <param name="entityType">Entity type metadata</param>
    /// <param name="propertyNames">Property names</param>
    /// <returns>Matching index or null</returns>
    private static IIndex? FindIndex(IEntityType entityType, params string[] propertyNames)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(propertyNames);

        return entityType.GetIndexes()
                         .SingleOrDefault(index => index.Properties.Select(property => property.Name)
                                                                   .SequenceEqual(propertyNames));
    }

    #endregion // Methods
}