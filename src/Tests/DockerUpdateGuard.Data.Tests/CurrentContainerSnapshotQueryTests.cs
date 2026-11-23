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

    /// <summary>
    /// Verify without any runtime scan run the latest snapshot of each container is returned
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task CurrentContainerSnapshotQueryNoRuntimeScanRunsReturnsLatestSnapshotPerContainerAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var baseTime = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
                var imageVersion = CreateImageVersion();
                var instance = CreateInstance("solo");
                var newestX = CreateSnapshot(instance, imageVersion, null, "X", baseTime);

                dbContext.ContainerSnapshots.AddRange(CreateSnapshot(instance, imageVersion, null, "X", baseTime.AddHours(-1)),
                                                      newestX);

                await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                               .ConfigureAwait(false);

                var result = await CurrentContainerSnapshotQuery.GetCurrentContainerSnapshotsAsync(dbContext, TestContext.CancellationToken)
                                                                .ConfigureAwait(false);

                Assert.HasCount(1, result, "Only the latest snapshot of the container is current.");
                Assert.AreEqual(newestX.Id, result[0].SnapshotId, "The newest snapshot is returned.");
            }
        }
    }

    /// <summary>
    /// Verify a snapshot of an instance that references the target run of another instance is not current
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task CurrentContainerSnapshotQuerySnapshotReferencingForeignTargetRunIsIgnoredAsync()
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
                var runOne = CreateRuntimeRun(baseTime.AddHours(-3));
                var runTwo = CreateRuntimeRun(baseTime);

                dbContext.ContainerSnapshots.AddRange(CreateSnapshot(instanceOne, imageVersion, runOne, "A", baseTime.AddHours(-3)),
                                                      CreateSnapshot(instanceTwo, imageVersion, runTwo, "B", baseTime),
                                                      CreateSnapshot(instanceTwo, imageVersion, runOne, "Foreign", baseTime.AddHours(-3)));

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
                                       $"{instanceTwo.Id}/B"
                                   }.Order(StringComparer.Ordinal)
                                    .ToList();

                Assert.AreSequenceEqual(expectedKeys, keys, "A snapshot whose run is not the target run of its own instance is not current.");
            }
        }
    }

    /// <summary>
    /// Verify runtime scan runs of one instance with the same start time are resolved by the higher run identifier
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task CurrentContainerSnapshotQueryRuntimeRunsWithSameStartTimeReturnsRunWithHigherIdAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var baseTime = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
                var imageVersion = CreateImageVersion();
                var instance = CreateInstance("tie");
                var lowerRun = CreateRuntimeRun(baseTime);
                var higherRun = CreateRuntimeRun(baseTime);

                lowerRun.Id = new Guid("00000000-0000-0000-0000-000000000001");
                higherRun.Id = new Guid("00000000-0000-0000-0000-000000000002");

                dbContext.ContainerSnapshots.AddRange(CreateSnapshot(instance, imageVersion, lowerRun, "Lower", baseTime),
                                                      CreateSnapshot(instance, imageVersion, higherRun, "Higher", baseTime));

                await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                               .ConfigureAwait(false);

                var result = await CurrentContainerSnapshotQuery.GetCurrentContainerSnapshotsAsync(dbContext, TestContext.CancellationToken)
                                                                .ConfigureAwait(false);

                Assert.HasCount(1, result, "Only the snapshots of one run are current.");
                Assert.AreEqual("Higher", result[0].ContainerId, "The run with the higher identifier wins a tie of start times.");
            }
        }
    }

    /// <summary>
    /// Verify snapshots of one container with the same recording time are resolved by the higher snapshot identifier
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task CurrentContainerSnapshotQuerySnapshotsWithSameRecordingTimeReturnsSnapshotWithHigherIdAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var baseTime = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
                var imageVersion = CreateImageVersion();
                var instance = CreateInstance("tie");
                var lowerSnapshot = CreateSnapshot(instance, imageVersion, null, "X", baseTime);
                var higherSnapshot = CreateSnapshot(instance, imageVersion, null, "X", baseTime);

                lowerSnapshot.Id = new Guid("00000000-0000-0000-0000-000000000001");
                higherSnapshot.Id = new Guid("00000000-0000-0000-0000-000000000002");

                dbContext.ContainerSnapshots.AddRange(lowerSnapshot, higherSnapshot);

                await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                               .ConfigureAwait(false);

                var result = await CurrentContainerSnapshotQuery.GetCurrentContainerSnapshotsAsync(dbContext, TestContext.CancellationToken)
                                                                .ConfigureAwait(false);

                Assert.HasCount(1, result, "Only one snapshot per container is current.");
                Assert.AreEqual(higherSnapshot.Id, result[0].SnapshotId, "The snapshot with the higher identifier wins a tie of recording times.");
            }
        }
    }

    #endregion // Methods
}