using System.Globalization;

using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Tests.Data;

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

    #region Static methods

    /// <summary>
    /// Describe every member of an enumeration with its stored number, ordered by number
    /// </summary>
    /// <typeparam name="TEnum">Enumeration type</typeparam>
    /// <returns>Members and numbers, for example <c>NotSet=0, Trivy=1</c></returns>
    private static string DescribeStoredNumbers<TEnum>()
        where TEnum : struct, Enum
    {
        var members = Enum.GetValues<TEnum>()
                          .OrderBy(value => Convert.ToInt32(value, CultureInfo.InvariantCulture))
                          .Select(value => $"{value}={Convert.ToInt32(value, CultureInfo.InvariantCulture)}");

        return string.Join(", ", members);
    }

    /// <summary>
    /// Create a vulnerability finding with a given stored source
    /// </summary>
    /// <param name="imageVersion">Image version of the finding</param>
    /// <param name="advisoryId">Advisory identifier</param>
    /// <param name="source">Stored source</param>
    /// <returns>Vulnerability finding</returns>
    private static VulnerabilityFinding CreateVulnerabilityFinding(ImageVersion imageVersion, string advisoryId, VulnerabilitySource source)
    {
        return new VulnerabilityFinding
               {
                   AdvisoryId = advisoryId,
                   DetectedAtUtc = DateTimeOffset.UtcNow,
                   ImageVersion = imageVersion,
                   IsActive = true,
                   Severity = VulnerabilitySeverity.High,
                   Source = source,
                   Title = "Retired source finding",
               };
    }

    #endregion // Static methods

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

                var vulnerabilityFindingEntity = dbContext.Model.FindEntityType(typeof(VulnerabilityFinding));

                Assert.IsNotNull(vulnerabilityFindingEntity, "Vulnerability finding entity must be part of the EF model");

                var resolvedByScanRunForeignKey = vulnerabilityFindingEntity.GetForeignKeys()
                                                                            .SingleOrDefault(foreignKey => foreignKey.Properties.Single().Name == nameof(VulnerabilityFinding.ResolvedByScanRunId));

                Assert.IsNotNull(resolvedByScanRunForeignKey, "Vulnerability finding must have a foreign key to the resolving scan run");
                Assert.AreEqual(DeleteBehavior.SetNull,
                                resolvedByScanRunForeignKey.DeleteBehavior,
                                "Deleting a scan run must clear the resolving scan run instead of deleting the finding");
                Assert.IsNull(resolvedByScanRunForeignKey.DependentToPrincipal,
                              "The resolving scan run must be mapped without a navigation property");

                Assert.IsNotNull(imageVersionEntity.FindProperty(nameof(ImageVersion.VulnerabilityAssessmentScanRunId)),
                                 "Image version must map the scan run of the latest vulnerability assessment");

                var fixStatusProperty = vulnerabilityFindingEntity.FindProperty(nameof(VulnerabilityFinding.FixStatus));
                var packageClassProperty = vulnerabilityFindingEntity.FindProperty(nameof(VulnerabilityFinding.PackageClass));
                var packageTypeProperty = vulnerabilityFindingEntity.FindProperty(nameof(VulnerabilityFinding.PackageType));

                Assert.IsNotNull(fixStatusProperty, "Vulnerability finding must map the reported fix status");
                Assert.IsFalse(fixStatusProperty.IsNullable, "The fix status must be non-nullable and default to NotSet");
                Assert.IsNotNull(packageClassProperty, "Vulnerability finding must map the reported package class");
                Assert.IsTrue(packageClassProperty.IsNullable, "The package class must stay optional for providers that do not report one");
                Assert.AreEqual(50,
                                packageClassProperty.GetMaxLength(),
                                "The package class must use the configured maximum length");
                Assert.IsNotNull(packageTypeProperty, "Vulnerability finding must map the reported package type");
                Assert.IsTrue(packageTypeProperty.IsNullable, "The package type must stay optional for providers that do not report one");
                Assert.AreEqual(50,
                                packageTypeProperty.GetMaxLength(),
                                "The package type must use the configured maximum length");

                var findingIdentityIndex = vulnerabilityFindingEntity.GetIndexes()
                                                                     .SingleOrDefault(index => index.IsUnique);

                Assert.IsNotNull(findingIdentityIndex, "Vulnerability findings must keep a unique identity index");
                Assert.AreSequenceEqual([
                                            nameof(VulnerabilityFinding.ImageVersionId),
                                            nameof(VulnerabilityFinding.AdvisoryId),
                                            nameof(VulnerabilityFinding.AffectedPackage),
                                        ],
                                        findingIdentityIndex.Properties.Select(property => property.Name)
                                                                       .ToList(),
                                        "The finding identity must stay the image version, advisory and affected package");
            }
        }
    }

    /// <summary>
    /// Verify the vulnerability value columns use the maximum lengths the entities declare
    /// </summary>
    [TestMethod]
    [TestCategory("AC5")]
    public void DockerUpdateGuardDbContextModelVulnerabilityValueColumnsUseEntityMaxLengths()
    {
        using (var database = new SqliteTestDatabase())
        {
            using (var dbContext = database.CreateDbContext())
            {
                var findingEntity = dbContext.Model.FindEntityType(typeof(VulnerabilityFinding));
                var imageVersionEntity = dbContext.Model.FindEntityType(typeof(ImageVersion));

                Assert.IsNotNull(findingEntity, "Vulnerability finding entity must be part of the EF model");
                Assert.IsNotNull(imageVersionEntity, "Image version entity must be part of the EF model");

                AssertMaxLength(findingEntity, nameof(VulnerabilityFinding.AdvisoryId), VulnerabilityFinding.MaxAdvisoryIdLength, 100);
                AssertMaxLength(findingEntity, nameof(VulnerabilityFinding.Title), VulnerabilityFinding.MaxTitleLength, 300);
                AssertMaxLength(findingEntity, nameof(VulnerabilityFinding.AffectedPackage), VulnerabilityFinding.MaxAffectedPackageLength, 200);
                AssertMaxLength(findingEntity, nameof(VulnerabilityFinding.InstalledVersion), VulnerabilityFinding.MaxInstalledVersionLength, 100);
                AssertMaxLength(findingEntity, nameof(VulnerabilityFinding.FixedVersion), VulnerabilityFinding.MaxFixedVersionLength, 100);
                AssertMaxLength(findingEntity, nameof(VulnerabilityFinding.PackageClass), VulnerabilityFinding.MaxPackageClassLength, 50);
                AssertMaxLength(findingEntity, nameof(VulnerabilityFinding.PackageType), VulnerabilityFinding.MaxPackageTypeLength, 50);
                AssertMaxLength(imageVersionEntity,
                                nameof(ImageVersion.VulnerabilityAssessmentMessage),
                                ImageVersion.MaxVulnerabilityAssessmentMessageLength,
                                2000);
            }
        }
    }

    /// <summary>
    /// Verify the stored enumerations keep exactly the members and numbers the database holds
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void DockerUpdateGuardDbContextEnumColumnsKeepStoredNumbers()
    {
        Assert.AreEqual("NotSet=0, ObservedImage=1, RuntimeContainer=2, BaseImageResolution=3",
                        DescribeStoredNumbers<ImageVersionSource>(),
                        "Image version sources must not list the retired registry metadata number 4");
        Assert.AreEqual("NotSet=0, BaseImage=1",
                        DescribeStoredNumbers<ImageRelationshipType>(),
                        "Relationship types must not list the retired build stage number 2");
        Assert.AreEqual("NotSet=0, RuntimeImageUpdate=2, TagRecommendation=3, DerivedBaseRuntimeUpdate=4",
                        DescribeStoredNumbers<UpdateFindingType>(),
                        "Update finding types must not list the retired base image update number 1");
        Assert.AreEqual("NotSet=0, Trivy=1, DockerScout=2",
                        DescribeStoredNumbers<VulnerabilitySource>(),
                        "Vulnerability sources must not list the retired registry advisory and manual numbers 3 and 4");
    }

    /// <summary>
    /// Verify rows holding retired enumeration numbers still load with those numbers
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task DockerUpdateGuardDbContextRetiredEnumNumbersLoadAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var baseImageVersion = new ImageVersion
                                       {
                                           Digest = "sha256:base",
                                           RegistryRepository = new RegistryRepository
                                                                {
                                                                    Registry = "docker.io",
                                                                    Repository = "library/debian"
                                                                },
                                           Tag = "12-slim",
                                           Source = (ImageVersionSource)4,
                                           VulnerabilityAssessmentSource = (VulnerabilitySource)3,
                                       };
                var currentImageVersion = new ImageVersion
                                          {
                                              Digest = "sha256:app",
                                              RegistryRepository = new RegistryRepository
                                                                   {
                                                                       Registry = "docker.io",
                                                                       Repository = "company/example-app"
                                                                   },
                                              Tag = "1.0.0",
                                          };
                var observedImage = new ObservedImage
                                    {
                                        CurrentImageVersion = currentImageVersion,
                                        Name = "Example App"
                                    };
                var scanRun = new ScanRun
                              {
                                  ObservedImage = observedImage,
                                  Status = ScanRunStatus.Succeeded,
                                  Type = ScanRunType.ObservedImage,
                              };

                dbContext.AddRange(baseImageVersion,
                                   currentImageVersion,
                                   observedImage,
                                   scanRun,
                                   new ImageRelationship
                                   {
                                       BaseImageVersion = baseImageVersion,
                                       ChildImageVersion = currentImageVersion,
                                       Depth = 1,
                                       RelationshipType = (ImageRelationshipType)2,
                                       ScanRun = scanRun,
                                   },
                                   new UpdateFinding
                                   {
                                       ObservedImage = observedImage,
                                       ScanRun = scanRun,
                                       SubjectImageVersion = baseImageVersion,
                                       Summary = "Retired finding type",
                                       Type = (UpdateFindingType)1,
                                   },
                                   CreateVulnerabilityFinding(baseImageVersion, "CVE-2026-0003", (VulnerabilitySource)3),
                                   CreateVulnerabilityFinding(baseImageVersion, "CVE-2026-0004", (VulnerabilitySource)4));

                await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);
                dbContext.ChangeTracker.Clear();

                var storedVersion = await dbContext.ImageVersions.SingleAsync(entity => entity.Digest == "sha256:base", TestContext.CancellationToken).ConfigureAwait(false);
                var storedRelationship = await dbContext.ImageRelationships.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var storedUpdateFinding = await dbContext.UpdateFindings.SingleAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var storedSources = await dbContext.VulnerabilityFindings.OrderBy(entity => entity.AdvisoryId)
                                                                         .Select(entity => (int)entity.Source)
                                                                         .ToListAsync(TestContext.CancellationToken)
                                                                         .ConfigureAwait(false);

                Assert.AreEqual(4, (int)storedVersion.Source, "A retired image version source must load with its stored number");
                Assert.AreEqual(3, (int)storedVersion.VulnerabilityAssessmentSource, "A retired assessment source must load with its stored number");
                Assert.AreEqual(2, (int)storedRelationship.RelationshipType, "A retired relationship type must load with its stored number");
                Assert.AreEqual(1, (int)storedUpdateFinding.Type, "A retired update finding type must load with its stored number");
                Assert.AreSequenceEqual<int>([3, 4], storedSources, "Retired vulnerability finding sources must load with their stored numbers");
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
                                        Type = UpdateFindingType.RuntimeImageUpdate
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
                                               FixStatus = VulnerabilityFixStatus.WillNotFix,
                                               IsActive = true,
                                               PackageClass = "os-pkgs",
                                               PackageType = "debian",
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

                await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);
                dbContext.ChangeTracker.Clear();

                var persistedObservedImage = await dbContext.ObservedImages.Include(entity => entity.CurrentImageVersion.RegistryRepository)
                                                                           .SingleAsync(TestContext.CancellationToken)
                                                                           .ConfigureAwait(false);

                var persistedUpdateFinding = await dbContext.UpdateFindings.Include(entity => entity.TagCandidates)
                                                                           .Include(entity => entity.SubjectImageVersion.RegistryRepository)
                                                                           .SingleAsync(TestContext.CancellationToken)
                                                                           .ConfigureAwait(false);

                Assert.AreEqual(2,
                                await dbContext.ImageVersions.CountAsync(TestContext.CancellationToken).ConfigureAwait(false),
                                "Two image versions must be stored for the representative graph");
                Assert.AreEqual(1,
                                await dbContext.ContainerActionRuns.CountAsync(TestContext.CancellationToken).ConfigureAwait(false),
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

                var persistedVulnerabilityFinding = await dbContext.VulnerabilityFindings.SingleAsync(TestContext.CancellationToken)
                                                                                         .ConfigureAwait(false);

                Assert.AreEqual(VulnerabilityFixStatus.WillNotFix,
                                persistedVulnerabilityFinding.FixStatus,
                                "The reported fix status must survive the persistence round-trip");
                Assert.AreEqual("os-pkgs",
                                persistedVulnerabilityFinding.PackageClass,
                                "The reported package class must survive the persistence round-trip");
                Assert.AreEqual("debian",
                                persistedVulnerabilityFinding.PackageType,
                                "The reported package type must survive the persistence round-trip");
            }
        }
    }

    /// <summary>
    /// Assert a property's mapped maximum length equals the entity constant and the unchanged column length
    /// </summary>
    /// <param name="entityType">Entity type metadata</param>
    /// <param name="propertyName">Property name</param>
    /// <param name="constantLength">Maximum length constant declared on the entity</param>
    /// <param name="expectedLength">Column length that must stay unchanged</param>
    private static void AssertMaxLength(IEntityType entityType, string propertyName, int constantLength, int expectedLength)
    {
        var property = entityType.FindProperty(propertyName);

        Assert.IsNotNull(property, $"{propertyName} must be mapped");
        Assert.AreEqual(constantLength, property.GetMaxLength(), $"{propertyName} must use the entity's maximum length constant");
        Assert.AreEqual(expectedLength, constantLength, $"{propertyName} must keep its column length");
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