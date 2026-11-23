using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Queries;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.UI;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ApplicationTelemetry"/>
/// </summary>
[TestClass]
public class ApplicationTelemetryTests
{
    #region Fields

    /// <summary>
    /// Fixed base time of the seeded data
    /// </summary>
    private static readonly DateTimeOffset _baseTime = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Test context providing information about and functionality for the current test run
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Create an in-memory database context with a fresh database
    /// </summary>
    /// <returns>Database context</returns>
    private static DockerUpdateGuardDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;

        return new DockerUpdateGuardDbContext(options);
    }

    /// <summary>
    /// Create a container snapshot
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

    /// <summary>
    /// Add the snapshots of an instance with two runtime scan runs: A, B and C in the older, A and D in the newer
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="imageVersion">Image version</param>
    private static void AddInstanceWithRuntimeScans(DockerUpdateGuardDbContext dbContext, ImageVersion imageVersion)
    {
        var instance = CreateInstance("one");
        var olderRun = CreateRuntimeRun(_baseTime.AddHours(-2));
        var newerRun = CreateRuntimeRun(_baseTime);

        dbContext.ContainerSnapshots.AddRange(CreateSnapshot(instance, imageVersion, olderRun, "A", _baseTime.AddHours(-2)),
                                              CreateSnapshot(instance, imageVersion, olderRun, "B", _baseTime.AddHours(-2)),
                                              CreateSnapshot(instance, imageVersion, olderRun, "C", _baseTime.AddHours(-2)),
                                              CreateSnapshot(instance, imageVersion, newerRun, "A", _baseTime),
                                              CreateSnapshot(instance, imageVersion, newerRun, "D", _baseTime));
    }

    /// <summary>
    /// Add the snapshots of an instance without a runtime scan run: X twice and Y once
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="imageVersion">Image version</param>
    private static void AddInstanceWithoutRuntimeScan(DockerUpdateGuardDbContext dbContext, ImageVersion imageVersion)
    {
        var instance = CreateInstance("two");

        dbContext.ContainerSnapshots.AddRange(CreateSnapshot(instance, imageVersion, null, "X", _baseTime.AddMinutes(-65)),
                                              CreateSnapshot(instance, imageVersion, null, "X", _baseTime.AddMinutes(-5)),
                                              CreateSnapshot(instance, imageVersion, null, "Y", _baseTime.AddMinutes(-65)));
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify the gauge counts only the containers of the latest runtime scan run
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task ApplicationTelemetryRefreshInventoryMetricsCountsOnlyLatestRuntimeScanContainersAsync()
    {
        var dbContext = CreateDbContext();

        await using (dbContext.ConfigureAwait(false))
        {
            AddInstanceWithRuntimeScans(dbContext, CreateImageVersion());

            await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                           .ConfigureAwait(false);

            var telemetry = new ApplicationTelemetry();

            await telemetry.RefreshInventoryMetricsAsync(dbContext, TestContext.CancellationToken)
                           .ConfigureAwait(false);

            Assert.AreEqual(2L, telemetry.RuntimeContainerCount, "Only containers A and D of the newer runtime scan run are currently known.");
        }
    }

    /// <summary>
    /// Verify an instance without a runtime scan run counts the latest snapshot of each container
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ApplicationTelemetryRefreshInventoryMetricsInstanceWithoutRuntimeScanUsesLatestSnapshotPerContainerAsync()
    {
        var dbContext = CreateDbContext();

        await using (dbContext.ConfigureAwait(false))
        {
            var imageVersion = CreateImageVersion();

            AddInstanceWithRuntimeScans(dbContext, imageVersion);
            AddInstanceWithoutRuntimeScan(dbContext, imageVersion);

            await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                           .ConfigureAwait(false);

            var telemetry = new ApplicationTelemetry();

            await telemetry.RefreshInventoryMetricsAsync(dbContext, TestContext.CancellationToken)
                           .ConfigureAwait(false);

            Assert.AreEqual(4L, telemetry.RuntimeContainerCount, "A, D, X and Y are currently known; X counts once.");
        }
    }

    /// <summary>
    /// Verify the gauge reports zero without any snapshot
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task ApplicationTelemetryRefreshInventoryMetricsNoSnapshotsReportsZeroAsync()
    {
        var dbContext = CreateDbContext();

        await using (dbContext.ConfigureAwait(false))
        {
            var telemetry = new ApplicationTelemetry();

            await telemetry.RefreshInventoryMetricsAsync(dbContext, TestContext.CancellationToken)
                           .ConfigureAwait(false);

            Assert.AreEqual(0L, telemetry.RuntimeContainerCount, "Without snapshots no container is known.");
        }
    }

    /// <summary>
    /// Verify the gauge equals the dashboard count and the number of listed runtime containers
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ApplicationTelemetryRefreshInventoryMetricsMatchesDashboardRuntimeContainerCountAsync()
    {
        var dbContext = CreateDbContext();

        await using (dbContext.ConfigureAwait(false))
        {
            var imageVersion = CreateImageVersion();

            AddInstanceWithRuntimeScans(dbContext, imageVersion);
            AddInstanceWithoutRuntimeScan(dbContext, imageVersion);

            await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                           .ConfigureAwait(false);

            var telemetry = new ApplicationTelemetry();
            var service = new ApplicationViewService(dbContext,
                                                     new ImageReferenceParser(),
                                                     new TestOptionsMonitor<DockerUpdateGuardOptions>(new DockerUpdateGuardOptions()),
                                                     new SharedBaseImageQueryService(dbContext));

            await telemetry.RefreshInventoryMetricsAsync(dbContext, TestContext.CancellationToken)
                           .ConfigureAwait(false);

            var summary = await service.GetDashboardSummaryAsync(TestContext.CancellationToken)
                                       .ConfigureAwait(false);
            var containers = await service.GetRuntimeContainersAsync(TestContext.CancellationToken)
                                          .ConfigureAwait(false);

            Assert.AreEqual(4L, telemetry.RuntimeContainerCount, "The gauge reports the four currently known containers.");
            Assert.AreEqual(summary.RuntimeContainerCount, telemetry.RuntimeContainerCount, "The gauge equals the dashboard's runtime container count.");
            Assert.AreEqual(containers.Count, telemetry.RuntimeContainerCount, "The gauge equals the number of listed runtime containers.");
        }
    }

    #endregion // Methods
}