using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Queries;
using DockerUpdateGuard.Data.Tests.Data;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Data.Tests;

/// <summary>
/// Tests for <see cref="CurrentContainerSnapshotQuery"/>
/// </summary>
[TestClass]
public class CurrentContainerSnapshotQueryTests
{
    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Create a container snapshot recorded at the given time
    /// </summary>
    /// <param name="dockerInstance">Docker instance</param>
    /// <param name="imageVersion">Image version</param>
    /// <param name="scanRun">Scan run, or null when the snapshot belongs to no run</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="recordedAtUtc">Recording time</param>
    /// <returns>Container snapshot</returns>
    private static ContainerSnapshot CreateSnapshot(DockerInstance dockerInstance,
                                                    ImageVersion imageVersion,
                                                    ScanRun? scanRun,
                                                    string containerId,
                                                    DateTimeOffset recordedAtUtc)
    {
        return new ContainerSnapshot
               {
                   DockerInstance = dockerInstance,
                   ImageVersion = imageVersion,
                   ScanRun = scanRun,
                   ContainerId = containerId,
                   Name = containerId,
                   RecordedAtUtc = recordedAtUtc
               };
    }

    /// <summary>
    /// Create a docker instance
    /// </summary>
    /// <param name="name">Instance name</param>
    /// <returns>Docker instance</returns>
    private static DockerInstance CreateInstance(string name)
    {
        return new DockerInstance
               {
                   Name = name,
                   EndpointUri = $"https://{name}.example.test",
                   ConnectionKind = DockerConnectionKind.Https
               };
    }

    /// <summary>
    /// Create a runtime container scan run
    /// </summary>
    /// <param name="startedAtUtc">Start time</param>
    /// <returns>Scan run</returns>
    private static ScanRun CreateRuntimeRun(DateTimeOffset startedAtUtc)
    {
        return new ScanRun
               {
                   Type = ScanRunType.RuntimeContainer,
                   StartedAtUtc = startedAtUtc
               };
    }

    /// <summary>
    /// Create an image version
    /// </summary>
    /// <returns>Image version</returns>
    private static ImageVersion CreateImageVersion()
    {
        return new ImageVersion
               {
                   RegistryRepository = new RegistryRepository
                                        {
                                            Registry = "docker.io",
                                            Repository = "company/app"
                                        },
                   Tag = "1.0.0",
                   Digest = "sha256:app"
               };
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify a missing context is rejected
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task CurrentContainerSnapshotQueryNullContextThrowsAsync()
    {
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => CurrentContainerSnapshotQuery.GetCurrentContainerSnapshotsAsync(null!, TestContext.CancellationToken))
                    .ConfigureAwait(false);
    }

    /// <summary>
    /// Verify an instance with runtime scan runs yields the snapshots of its latest run and an instance without one
    /// yields the latest snapshot of each container
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task CurrentContainerSnapshotQueryMixedInstancesReturnsLatestRuntimeScanAndLatestSnapshotPerContainerAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var baseTime = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
                var imageVersion = CreateImageVersion();
                var instanceOne = CreateInstance("one");
                var instanceTwo = CreateInstance("two");
                var olderRun = CreateRuntimeRun(baseTime.AddHours(-2));
                var newerRun = CreateRuntimeRun(baseTime);
                var newestX = CreateSnapshot(instanceTwo, imageVersion, null, "X", baseTime.AddMinutes(-5));

                dbContext.ContainerSnapshots.AddRange(CreateSnapshot(instanceOne, imageVersion, olderRun, "A", baseTime.AddHours(-2)),
                                                      CreateSnapshot(instanceOne, imageVersion, olderRun, "B", baseTime.AddHours(-2)),
                                                      CreateSnapshot(instanceOne, imageVersion, olderRun, "C", baseTime.AddHours(-2)),
                                                      CreateSnapshot(instanceOne, imageVersion, newerRun, "A", baseTime),
                                                      CreateSnapshot(instanceOne, imageVersion, newerRun, "D", baseTime),
                                                      CreateSnapshot(instanceTwo, imageVersion, null, "X", baseTime.AddMinutes(-65)),
                                                      newestX,
                                                      CreateSnapshot(instanceTwo, imageVersion, null, "Y", baseTime.AddMinutes(-65)));

                await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                               .ConfigureAwait(false);

                var result = await CurrentContainerSnapshotQuery.GetCurrentContainerSnapshotsAsync(dbContext, TestContext.CancellationToken)
                                                                .ConfigureAwait(false);

                var keys = result.Select(entity => $"{entity.DockerInstanceId}/{entity.ContainerId}")
                                 .Order(StringComparer.Ordinal)
                                 .ToList();
                var expectedKeys = new[]
                                   {
                                       $"{instanceOne.Id}/A",
                                       $"{instanceOne.Id}/D",
                                       $"{instanceTwo.Id}/X",
                                       $"{instanceTwo.Id}/Y"
                                   }.Order(StringComparer.Ordinal)
                                    .ToList();
                var currentX = result.Single(entity => entity.DockerInstanceId == instanceTwo.Id && entity.ContainerId == "X");
                var currentA = result.Single(entity => entity.DockerInstanceId == instanceOne.Id && entity.ContainerId == "A");
                var newerRunA = await dbContext.ContainerSnapshots.Where(entity => entity.ScanRunId == newerRun.Id && entity.ContainerId == "A")
                                                                  .Select(entity => entity.Id)
                                                                  .SingleAsync(TestContext.CancellationToken)
                                                                  .ConfigureAwait(false);

                Assert.AreSequenceEqual(expectedKeys, keys, "Exactly the containers of the latest runtime scan run and the latest snapshot per container without a run are current.");
                Assert.AreEqual(newestX.Id, currentX.SnapshotId, "The newest snapshot of a container of an instance without a runtime scan is returned.");
                Assert.AreEqual(newerRunA, currentA.SnapshotId, "The snapshot of the latest run is returned for a container that is in both runs.");
                Assert.AreEqual(imageVersion.Id, currentX.ImageVersionId, "The image version of the snapshot is returned.");
            }
        }
    }

    #endregion // Methods
}