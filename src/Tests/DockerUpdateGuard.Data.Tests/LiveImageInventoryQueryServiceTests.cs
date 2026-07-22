using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Queries;
using DockerUpdateGuard.Data.Tests.Data;

namespace DockerUpdateGuard.Data.Tests;

/// <summary>
/// Tests for <see cref="LiveImageInventoryQueryService"/>
/// </summary>
[TestClass]
public class LiveImageInventoryQueryServiceTests
{
    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify the live inventory contains observed, running and base image versions while excluding orphaned versions
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task LiveImageInventoryQueryServiceReturnsObservedRunningAndBaseVersionsAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var appRepository = new RegistryRepository
                                    {
                                        Registry = "docker.io",
                                        Repository = "company/app"
                                    };
                var appVersion = new ImageVersion
                                 {
                                     RegistryRepository = appRepository,
                                     Tag = "1.0.0",
                                     Digest = "sha256:app"
                                 };
                var baseRepository = new RegistryRepository
                                     {
                                         Registry = "docker.io",
                                         Repository = "library/debian"
                                     };
                var baseVersion = new ImageVersion
                                  {
                                      RegistryRepository = baseRepository,
                                      Tag = "12-slim",
                                      Digest = "sha256:base"
                                  };
                var runningRepository = new RegistryRepository
                                        {
                                            Registry = "docker.io",
                                            Repository = "company/worker"
                                        };
                var runningVersion = new ImageVersion
                                     {
                                         RegistryRepository = runningRepository,
                                         Tag = "2.0.0",
                                         Digest = "sha256:running"
                                     };
                var staleRepository = new RegistryRepository
                                      {
                                          Registry = "docker.io",
                                          Repository = "company/legacy"
                                      };
                var staleVersion = new ImageVersion
                                   {
                                       RegistryRepository = staleRepository,
                                       Tag = "latest",
                                       Digest = "sha256:stale"
                                   };
                var observedImage = new ObservedImage
                                    {
                                        Name = "Company App",
                                        CurrentImageVersion = appVersion
                                    };
                var dockerInstance = new DockerInstance
                                     {
                                         Name = "Production",
                                         EndpointUri = "https://docker.example.test",
                                         ConnectionKind = DockerConnectionKind.Https
                                     };
                var previousScanRun = new ScanRun
                                      {
                                          Type = ScanRunType.RuntimeContainer,
                                          StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-2)
                                      };
                var latestScanRun = new ScanRun
                                    {
                                        Type = ScanRunType.RuntimeContainer,
                                        StartedAtUtc = DateTimeOffset.UtcNow
                                    };
                var staleSnapshot = new ContainerSnapshot
                                    {
                                        DockerInstance = dockerInstance,
                                        ImageVersion = staleVersion,
                                        ScanRun = previousScanRun,
                                        ContainerId = "container-1",
                                        Name = "worker-old",
                                        RecordedAtUtc = DateTimeOffset.UtcNow.AddHours(-2)
                                    };
                var runningSnapshot = new ContainerSnapshot
                                      {
                                          DockerInstance = dockerInstance,
                                          ImageVersion = runningVersion,
                                          ScanRun = latestScanRun,
                                          ContainerId = "container-1",
                                          Name = "worker-new",
                                          RecordedAtUtc = DateTimeOffset.UtcNow
                                      };
                var relationship = new ImageRelationship
                                   {
                                       ChildImageVersion = appVersion,
                                       BaseImageVersion = baseVersion,
                                       RelationshipType = ImageRelationshipType.BaseImage,
                                       Depth = 1
                                   };

                dbContext.AddRange(appVersion,
                                   baseVersion,
                                   runningVersion,
                                   staleVersion,
                                   observedImage,
                                   dockerInstance,
                                   previousScanRun,
                                   latestScanRun,
                                   staleSnapshot,
                                   runningSnapshot,
                                   relationship);

                await dbContext.SaveChangesAsync(TestContext.CancellationToken).ConfigureAwait(false);

                var queryService = new LiveImageInventoryQueryService(dbContext);

                var liveImageVersionIds = await queryService.GetLiveImageVersionIdsAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.Contains(id => id == appVersion.Id, liveImageVersionIds, "The current version of an observed image must be live");
                Assert.Contains(id => id == baseVersion.Id, liveImageVersionIds, "The base image of an observed image must be live");
                Assert.Contains(id => id == runningVersion.Id, liveImageVersionIds, "The image version from the latest runtime scan must be live");
                Assert.DoesNotContain(id => id == staleVersion.Id, liveImageVersionIds, "An image version only present in an older runtime scan must not be live");
            }
        }
    }

    #endregion // Methods
}