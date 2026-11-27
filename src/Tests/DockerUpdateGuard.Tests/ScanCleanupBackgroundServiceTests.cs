using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Queries;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ScanCleanupBackgroundService"/>
/// </summary>
[TestClass]
public partial class ScanCleanupBackgroundServiceTests
{
    #region Constants

    /// <summary>
    /// Registry of the image versions that reference scan runs
    /// </summary>
    private const string Registry = "docker.io";

    #endregion // Constants

    #region Properties

    /// <summary>
    /// Test context providing information about and functionality for the current test run
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify cleanup keeps every scan run that completed within the retention period, even beyond 20 runs
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task ScanCleanupBackgroundServiceExecuteCoreAsyncKeepsAllScanRunsWithinRetentionAsync()
    {
        var seeds = CreateSeeds(25, 0);

        var result = await RunCleanupAsync(seeds).ConfigureAwait(false);

        Assert.AreSequenceEqual(RankIds(Enumerable.Range(0, 25)),
                                result.RemainingCorrelationIds,
                                "Cleanup must keep all 25 scan runs that completed within the retention period");
        Assert.Contains(" and 0 scan runs",
                        result.CleanupMessage,
                        "The cleanup log entry must report that no scan run was removed");
    }

    /// <summary>
    /// Verify cleanup keeps the 20 most recently completed scan runs when all runs are past the retention period
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ScanCleanupBackgroundServiceExecuteCoreAsyncKeepsLatestTwentyScanRunsPastRetentionAsync()
    {
        var seeds = CreateSeeds(0, 25);

        var result = await RunCleanupAsync(seeds).ConfigureAwait(false);

        Assert.AreSequenceEqual(RankIds(Enumerable.Range(0, 20)),
                                result.RemainingCorrelationIds,
                                "Cleanup must keep the 20 most recently completed scan runs and remove the 5 oldest");
        Assert.Contains(" and 5 scan runs",
                        result.CleanupMessage,
                        "The cleanup log entry must report 5 removed scan runs");
    }

    /// <summary>
    /// Verify cleanup keeps the recent runs and tops them up with the newest old runs to a minimum of 20
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task ScanCleanupBackgroundServiceExecuteCoreAsyncKeepsMinimumOfTwentyScanRunsAsync()
    {
        var seeds = CreateSeeds(5, 30);

        var result = await RunCleanupAsync(seeds).ConfigureAwait(false);

        Assert.AreSequenceEqual(RankIds(Enumerable.Range(0, 20)),
                                result.RemainingCorrelationIds,
                                "Cleanup must keep the 5 recent runs and the 15 newest old runs");
        Assert.Contains(" and 15 scan runs",
                        result.CleanupMessage,
                        "The cleanup log entry must report 15 removed scan runs");
    }

    /// <summary>
    /// Verify cleanup keeps a referenced scan run that is past the retention period and outside the newest 20
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ScanCleanupBackgroundServiceExecuteCoreAsyncKeepsReferencedScanRunPastRetentionAsync()
    {
        var seeds = CreateSeeds(0, 21);

        var result = await RunCleanupAsync(seeds, 20).ConfigureAwait(false);

        Assert.AreSequenceEqual(RankIds(Enumerable.Range(0, 21)),
                                result.RemainingCorrelationIds,
                                "Cleanup must keep all 21 scan runs because the oldest one is referenced by an image relationship");
    }

    /// <summary>
    /// Verify the minimum of 20 counts referenced scan runs too
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task ScanCleanupBackgroundServiceExecuteCoreAsyncCountsReferencedScanRunsTowardMinimumAsync()
    {
        var seeds = CreateSeeds(0, 25);

        var result = await RunCleanupAsync(seeds, 0, 1).ConfigureAwait(false);

        Assert.AreSequenceEqual(RankIds(Enumerable.Range(0, 20)),
                                result.RemainingCorrelationIds,
                                "Cleanup must keep 20 scan runs: the 2 referenced ones count toward the minimum and the 5 oldest are removed");
    }

    /// <summary>
    /// Verify cleanup does not delete running scan runs that are not part of retained history yet
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    public async Task ScanCleanupBackgroundServiceExecuteCoreAsyncKeepsRunningScanRunsAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddScoped(_ =>
                                    {
                                        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(databaseName)
                                                                                                               .Options;

                                        return new DockerUpdateGuardDbContext(options);
                                    });
        serviceCollection.AddScoped(_ => new ApplicationTelemetry());
        serviceCollection.AddScoped<ILiveImageInventoryQueryService, LiveImageInventoryQueryService>();

        var serviceProvider = serviceCollection.BuildServiceProvider();

        await using (serviceProvider.ConfigureAwait(false))
        {
            var scope = serviceProvider.CreateAsyncScope();

            await using (scope.ConfigureAwait(false))
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
                var now = DateTimeOffset.UtcNow;

                for (var index = 0; index < 20; index++)
                {
                    dbContext.ScanRuns.Add(new ScanRun
                                           {
                                               Type = ScanRunType.ObservedImage,
                                               Status = ScanRunStatus.Succeeded,
                                               TriggerSource = ScanTriggerSource.Manual,
                                               StartedAtUtc = now.AddMinutes(-(index + 1)),
                                               CompletedAtUtc = now.AddMinutes(-(index + 1)).AddSeconds(5),
                                               CorrelationId = $"completed-{index:D2}",
                                           });
                }

                dbContext.ScanRuns.Add(new ScanRun
                                       {
                                           Type = ScanRunType.RuntimeContainer,
                                           Status = ScanRunStatus.Running,
                                           TriggerSource = ScanTriggerSource.Scheduled,
                                           StartedAtUtc = now,
                                           CorrelationId = "running-scan",
                                       });

                await dbContext.SaveChangesAsync(CancellationToken.None)
                               .ConfigureAwait(false);
            }

            var options = new DockerUpdateGuardOptions
                          {
                              Scanning = new ScanningOptions
                                         {
                                             CleanupIntervalMinutes = 60,
                                             RetainScanRunsDays = 30,
                                         },
                          };

            var service = new TestScanCleanupBackgroundService(new TestLogger<ScanCleanupBackgroundService>(),
                                                               new TestOptionsMonitor<DockerUpdateGuardOptions>(options),
                                                               serviceProvider.GetRequiredService<IServiceScopeFactory>());

            await service.ExecuteOnceAsync(CancellationToken.None)
                         .ConfigureAwait(false);

            var verificationScope = serviceProvider.CreateAsyncScope();

            await using (verificationScope.ConfigureAwait(false))
            {
                var dbContext = verificationScope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();

                var remainingCorrelationIds = await dbContext.ScanRuns
                                                             .OrderBy(entity => entity.CorrelationId)
                                                             .Select(entity => entity.CorrelationId)
                                                             .ToListAsync(CancellationToken.None)
                                                             .ConfigureAwait(false);

                Assert.HasCount(21,
                                remainingCorrelationIds,
                                "Cleanup must preserve running scan runs in addition to retained completed history");
                Assert.Contains(entity => entity == "running-scan",
                                remainingCorrelationIds,
                                "Cleanup must not delete a running scan run that has not produced related entities yet");
            }
        }
    }

    /// <summary>
    /// Verify cleanup marks abandoned running scan runs as failed while fresh runs stay untouched
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ScanCleanupBackgroundServiceExecuteCoreAsyncMarksStaleRunningScanRunsAsFailedAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddScoped(_ =>
                                    {
                                        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(databaseName)
                                                                                                               .Options;

                                        return new DockerUpdateGuardDbContext(options);
                                    });
        serviceCollection.AddScoped(_ => new ApplicationTelemetry());
        serviceCollection.AddScoped<ILiveImageInventoryQueryService, LiveImageInventoryQueryService>();

        var serviceProvider = serviceCollection.BuildServiceProvider();

        await using (serviceProvider.ConfigureAwait(false))
        {
            var scope = serviceProvider.CreateAsyncScope();

            await using (scope.ConfigureAwait(false))
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
                var now = DateTimeOffset.UtcNow;

                dbContext.ScanRuns.Add(new ScanRun
                                       {
                                           Type = ScanRunType.Vulnerability,
                                           Status = ScanRunStatus.Running,
                                           TriggerSource = ScanTriggerSource.Scheduled,
                                           StartedAtUtc = now.AddDays(-3),
                                           CorrelationId = "stale-run",
                                       });
                dbContext.ScanRuns.Add(new ScanRun
                                       {
                                           Type = ScanRunType.ObservedImage,
                                           Status = ScanRunStatus.Running,
                                           TriggerSource = ScanTriggerSource.Scheduled,
                                           StartedAtUtc = now.AddDays(-3),
                                           CorrelationId = "stale-observed-run",
                                       });
                dbContext.ScanRuns.Add(new ScanRun
                                       {
                                           Type = ScanRunType.NotSet,
                                           Status = ScanRunStatus.Running,
                                           TriggerSource = ScanTriggerSource.Scheduled,
                                           StartedAtUtc = now.AddDays(-3),
                                           CorrelationId = "stale-untyped-run",
                                       });
                dbContext.ScanRuns.Add(new ScanRun
                                       {
                                           Type = ScanRunType.Vulnerability,
                                           Status = ScanRunStatus.Running,
                                           TriggerSource = ScanTriggerSource.Scheduled,
                                           StartedAtUtc = now.AddMinutes(-5),
                                           CorrelationId = "fresh-run",
                                       });

                await dbContext.SaveChangesAsync(CancellationToken.None)
                               .ConfigureAwait(false);
            }

            var options = new DockerUpdateGuardOptions
                          {
                              Scanning = new ScanningOptions
                                         {
                                             CleanupIntervalMinutes = 60,
                                             RetainScanRunsDays = 30,
                                             VulnerabilityRefreshIntervalMinutes = 180,
                                         },
                          };
            var logger = new TestLogger<ScanCleanupBackgroundService>();

            var service = new TestScanCleanupBackgroundService(logger,
                                                               new TestOptionsMonitor<DockerUpdateGuardOptions>(options),
                                                               serviceProvider.GetRequiredService<IServiceScopeFactory>());

            await service.ExecuteOnceAsync(CancellationToken.None)
                         .ConfigureAwait(false);

            var verificationScope = serviceProvider.CreateAsyncScope();

            await using (verificationScope.ConfigureAwait(false))
            {
                var dbContext = verificationScope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
                var staleScanRun = await dbContext.ScanRuns.SingleAsync(entity => entity.CorrelationId == "stale-run", CancellationToken.None)
                                                           .ConfigureAwait(false);
                var staleObservedScanRun = await dbContext.ScanRuns.SingleAsync(entity => entity.CorrelationId == "stale-observed-run", CancellationToken.None)
                                                                   .ConfigureAwait(false);
                var staleUntypedScanRun = await dbContext.ScanRuns.SingleAsync(entity => entity.CorrelationId == "stale-untyped-run", CancellationToken.None)
                                                                  .ConfigureAwait(false);
                var freshScanRun = await dbContext.ScanRuns.SingleAsync(entity => entity.CorrelationId == "fresh-run", CancellationToken.None)
                                                           .ConfigureAwait(false);

                Assert.AreEqual(ScanRunStatus.Failed,
                                staleScanRun.Status,
                                "Cleanup must mark running scan runs that never completed as failed");
                Assert.AreEqual(ScanRunStatus.Failed,
                                staleObservedScanRun.Status,
                                "Cleanup must repair abandoned observed image scan runs as well");
                Assert.AreEqual(ScanRunStatus.Failed,
                                staleUntypedScanRun.Status,
                                "Cleanup must repair abandoned scan runs that carry no run type");
                Assert.IsNotNull(staleScanRun.CompletedAtUtc, "A repaired scan run must record a completion timestamp");
                Assert.AreEqual("Marked as failed by cleanup: the run never completed (process restart or crash)",
                                staleScanRun.ErrorMessage,
                                "A repaired scan run must explain why it was marked as failed");
                Assert.AreEqual(ScanRunStatus.Running,
                                freshScanRun.Status,
                                "Cleanup must not touch running scan runs that are still within their expected runtime");
                Assert.IsNull(freshScanRun.CompletedAtUtc, "Cleanup must not complete running scan runs that are still within their expected runtime");
                Assert.Contains(entry => entry.EventId.Id == 2021, logger.Entries, "Repairing abandoned scan runs must be logged");
            }
        }
    }

    /// <summary>
    /// Verify a long refresh interval stretches the stale threshold beyond the minimum of 24 hours
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ScanCleanupBackgroundServiceExecuteCoreAsyncLongIntervalStretchesStaleThresholdAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddScoped(_ =>
                                    {
                                        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(databaseName)
                                                                                                               .Options;

                                        return new DockerUpdateGuardDbContext(options);
                                    });
        serviceCollection.AddScoped(_ => new ApplicationTelemetry());
        serviceCollection.AddScoped<ILiveImageInventoryQueryService, LiveImageInventoryQueryService>();

        var serviceProvider = serviceCollection.BuildServiceProvider();

        await using (serviceProvider.ConfigureAwait(false))
        {
            var scope = serviceProvider.CreateAsyncScope();

            await using (scope.ConfigureAwait(false))
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
                var now = DateTimeOffset.UtcNow;

                dbContext.ScanRuns.Add(new ScanRun
                                       {
                                           Type = ScanRunType.Vulnerability,
                                           Status = ScanRunStatus.Running,
                                           TriggerSource = ScanTriggerSource.Scheduled,
                                           StartedAtUtc = now.AddHours(-40),
                                           CorrelationId = "beyond-threshold-run",
                                       });
                dbContext.ScanRuns.Add(new ScanRun
                                       {
                                           Type = ScanRunType.Vulnerability,
                                           Status = ScanRunStatus.Running,
                                           TriggerSource = ScanTriggerSource.Scheduled,
                                           StartedAtUtc = now.AddHours(-26),
                                           CorrelationId = "within-threshold-run",
                                       });

                await dbContext.SaveChangesAsync(CancellationToken.None)
                               .ConfigureAwait(false);
            }

            // Twice the refresh interval of 15 hours is 30 hours and therefore longer than the minimum threshold
            var options = new DockerUpdateGuardOptions
                          {
                              Scanning = new ScanningOptions
                                         {
                                             CleanupIntervalMinutes = 60,
                                             RetainScanRunsDays = 30,
                                             VulnerabilityRefreshIntervalMinutes = 900,
                                         },
                          };

            var service = new TestScanCleanupBackgroundService(new TestLogger<ScanCleanupBackgroundService>(),
                                                               new TestOptionsMonitor<DockerUpdateGuardOptions>(options),
                                                               serviceProvider.GetRequiredService<IServiceScopeFactory>());

            await service.ExecuteOnceAsync(CancellationToken.None)
                         .ConfigureAwait(false);

            var verificationScope = serviceProvider.CreateAsyncScope();

            await using (verificationScope.ConfigureAwait(false))
            {
                var dbContext = verificationScope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
                var beyondThresholdScanRun = await dbContext.ScanRuns.SingleAsync(entity => entity.CorrelationId == "beyond-threshold-run", CancellationToken.None)
                                                                     .ConfigureAwait(false);
                var withinThresholdScanRun = await dbContext.ScanRuns.SingleAsync(entity => entity.CorrelationId == "within-threshold-run", CancellationToken.None)
                                                                     .ConfigureAwait(false);

                Assert.AreEqual(ScanRunStatus.Failed,
                                beyondThresholdScanRun.Status,
                                "A running scan run older than twice the configured refresh interval must be repaired");
                Assert.AreEqual(ScanRunStatus.Running,
                                withinThresholdScanRun.Status,
                                "A long refresh interval must keep running scan runs untouched beyond the minimum threshold of 24 hours");
            }
        }
    }

    /// <summary>
    /// Verify the cleanup startup step repairs abandoned running scan runs before the first scheduled cycle
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ScanCleanupBackgroundServiceExecuteStartupAsyncMarksStaleRunningScanRunsAsFailedAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddScoped(_ =>
                                    {
                                        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(databaseName)
                                                                                                               .Options;

                                        return new DockerUpdateGuardDbContext(options);
                                    });
        serviceCollection.AddScoped(_ => new ApplicationTelemetry());
        serviceCollection.AddScoped<ILiveImageInventoryQueryService, LiveImageInventoryQueryService>();

        var serviceProvider = serviceCollection.BuildServiceProvider();

        await using (serviceProvider.ConfigureAwait(false))
        {
            var scope = serviceProvider.CreateAsyncScope();

            await using (scope.ConfigureAwait(false))
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();

                dbContext.ScanRuns.Add(new ScanRun
                                       {
                                           Type = ScanRunType.RuntimeContainer,
                                           Status = ScanRunStatus.Running,
                                           TriggerSource = ScanTriggerSource.Scheduled,
                                           StartedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
                                           CorrelationId = "crashed-run",
                                       });

                await dbContext.SaveChangesAsync(CancellationToken.None)
                               .ConfigureAwait(false);
            }

            var options = new DockerUpdateGuardOptions
                          {
                              Scanning = new ScanningOptions
                                         {
                                             CleanupIntervalMinutes = 60,
                                             RetainScanRunsDays = 30,
                                         },
                          };

            var service = new TestScanCleanupBackgroundService(new TestLogger<ScanCleanupBackgroundService>(),
                                                               new TestOptionsMonitor<DockerUpdateGuardOptions>(options),
                                                               serviceProvider.GetRequiredService<IServiceScopeFactory>());

            await service.ExecuteStartupOnceAsync(CancellationToken.None)
                         .ConfigureAwait(false);

            var verificationScope = serviceProvider.CreateAsyncScope();

            await using (verificationScope.ConfigureAwait(false))
            {
                var dbContext = verificationScope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
                var crashedScanRun = await dbContext.ScanRuns.SingleAsync(entity => entity.CorrelationId == "crashed-run", CancellationToken.None)
                                                             .ConfigureAwait(false);

                Assert.AreEqual(ScanRunStatus.Failed,
                                crashedScanRun.Status,
                                "The cleanup startup step must repair running scan runs left behind by a crashed process");
                Assert.IsNotNull(crashedScanRun.CompletedAtUtc, "A scan run repaired at startup must record a completion timestamp");
            }
        }
    }

    /// <summary>
    /// Verify deactivated vulnerability findings of image versions no longer part of the live fleet are purged immediately,
    /// while deactivated findings of still-live image versions keep the age-based retention
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ScanCleanupBackgroundServiceExecuteCoreAsyncPurgesFindingsOfNonLiveImageVersionsImmediatelyAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddScoped(_ =>
                                    {
                                        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(databaseName)
                                                                                                               .Options;

                                        return new DockerUpdateGuardDbContext(options);
                                    });
        serviceCollection.AddScoped(_ => new ApplicationTelemetry());
        serviceCollection.AddScoped<ILiveImageInventoryQueryService, LiveImageInventoryQueryService>();

        var serviceProvider = serviceCollection.BuildServiceProvider();

        await using (serviceProvider.ConfigureAwait(false))
        {
            Guid retiredFindingId;
            Guid liveImageFindingId;

            var scope = serviceProvider.CreateAsyncScope();

            await using (scope.ConfigureAwait(false))
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
                var imageCatalogRepository = new ImageCatalogRepository(dbContext);
                var retiredImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                    "company/retired",
                                                                                                    "1.0.0",
                                                                                                    "sha256:retired",
                                                                                                    cancellationToken: CancellationToken.None)
                                                                      .ConfigureAwait(false);
                var liveImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync("docker.io",
                                                                                                 "company/live",
                                                                                                 "1.0.0",
                                                                                                 "sha256:live",
                                                                                                 cancellationToken: CancellationToken.None)
                                                                   .ConfigureAwait(false);
                var observedImage = new ObservedImage
                                    {
                                        Name = "Company Live",
                                        CurrentImageVersionId = liveImageVersion.Id,
                                    };
                var retiredFinding = new VulnerabilityFinding
                                     {
                                         ImageVersionId = retiredImageVersion.Id,
                                         AdvisoryId = "CVE-2026-3000",
                                         Title = "Retired image finding",
                                         Severity = VulnerabilitySeverity.High,
                                         Source = VulnerabilitySource.Trivy,
                                         AffectedPackage = "openssl",
                                         IsActive = false,
                                         ResolvedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                                     };
                var liveImageFinding = new VulnerabilityFinding
                                       {
                                           ImageVersionId = liveImageVersion.Id,
                                           AdvisoryId = "CVE-2026-4000",
                                           Title = "Patched finding on a live image",
                                           Severity = VulnerabilitySeverity.Medium,
                                           Source = VulnerabilitySource.Trivy,
                                           AffectedPackage = "zlib",
                                           IsActive = false,
                                           ResolvedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                                       };

                dbContext.ObservedImages.Add(observedImage);
                dbContext.VulnerabilityFindings.AddRange(retiredFinding, liveImageFinding);
                await dbContext.SaveChangesAsync(CancellationToken.None)
                               .ConfigureAwait(false);

                retiredFindingId = retiredFinding.Id;
                liveImageFindingId = liveImageFinding.Id;
            }

            var options = new DockerUpdateGuardOptions
                          {
                              Scanning = new ScanningOptions
                                         {
                                             CleanupIntervalMinutes = 60,
                                             RetainScanRunsDays = 30,
                                         },
                          };
            var service = new TestScanCleanupBackgroundService(new TestLogger<ScanCleanupBackgroundService>(),
                                                               new TestOptionsMonitor<DockerUpdateGuardOptions>(options),
                                                               serviceProvider.GetRequiredService<IServiceScopeFactory>());

            await service.ExecuteOnceAsync(CancellationToken.None)
                         .ConfigureAwait(false);

            var verificationScope = serviceProvider.CreateAsyncScope();

            await using (verificationScope.ConfigureAwait(false))
            {
                var dbContext = verificationScope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
                var remainingFindingIds = await dbContext.VulnerabilityFindings.Select(entity => entity.Id)
                                                                               .ToListAsync(CancellationToken.None)
                                                                               .ConfigureAwait(false);

                Assert.DoesNotContain(retiredFindingId,
                                      remainingFindingIds,
                                      "A deactivated finding of an image version no longer part of the live fleet must be purged immediately, regardless of its age");
                Assert.Contains(liveImageFindingId,
                                remainingFindingIds,
                                "A deactivated finding of a still-live image version must keep the age-based retention window");
            }
        }
    }

    /// <summary>
    /// Verify a reloaded configuration that fails validation keeps the job waiting its last valid interval
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ScanCleanupBackgroundServiceExecuteAsyncReloadFailsValidationKeepsLastValidIntervalAsync()
    {
        var harness = new ScanCleanupReloadHarness(30);

        await using (harness.ConfigureAwait(false))
        {
            harness.Reload(45);
            harness.Start();

            var firstWaitStarted = await harness.Clock.WaitForNextWaitAsync(harness.RunTask).ConfigureAwait(false);

            Assert.IsTrue(firstWaitStarted, "The job must start its first wait with a valid configuration");

            harness.Reload(0);
            harness.Clock.ElapseLastWait();

            var secondWaitStarted = await harness.Clock.WaitForNextWaitAsync(harness.RunTask).ConfigureAwait(false);

            Assert.IsTrue(secondWaitStarted, "A reload that fails validation must not end the job's task");

            await harness.StopAsync().ConfigureAwait(false);

            Assert.AreSequenceEqual(new[] { TimeSpan.FromMinutes(45), TimeSpan.FromMinutes(45) },
                                    harness.Clock.Waits,
                                    "The job must keep waiting the last valid interval while the configuration is invalid");
            Assert.Contains(entry => entry.EventId.Id == 2005
                                     && entry.LogLevel == LogLevel.Warning
                                     && entry.Exception is OptionsValidationException,
                            harness.Logger.Entries,
                            "The failed lookup must be logged as warning 2005 with the validation exception");
        }
    }

    /// <summary>
    /// Verify the next wait uses the new interval once a valid configuration is loaded again
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ScanCleanupBackgroundServiceExecuteAsyncReloadValidAgainUsesNewIntervalAsync()
    {
        var harness = new ScanCleanupReloadHarness(30);

        await using (harness.ConfigureAwait(false))
        {
            harness.Start();

            var firstWaitStarted = await harness.Clock.WaitForNextWaitAsync(harness.RunTask).ConfigureAwait(false);

            Assert.IsTrue(firstWaitStarted, "The job must start its first wait with a valid configuration");

            harness.Reload(0);
            harness.Clock.ElapseLastWait();

            var secondWaitStarted = await harness.Clock.WaitForNextWaitAsync(harness.RunTask).ConfigureAwait(false);

            Assert.IsTrue(secondWaitStarted, "A reload that fails validation must not end the job's task");

            harness.Reload(60);
            harness.Clock.ElapseLastWait();

            var thirdWaitStarted = await harness.Clock.WaitForNextWaitAsync(harness.RunTask).ConfigureAwait(false);

            await harness.StopAsync().ConfigureAwait(false);

            Assert.IsTrue(thirdWaitStarted, "The job must keep running after the configuration is valid again");
            Assert.AreSequenceEqual(new[] { TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60) },
                                    harness.Clock.Waits,
                                    "The first wait after a valid reload must use the new interval");
        }
    }

    /// <summary>
    /// Create the names and ages of the scan runs ordered from the newest to the oldest completed run;
    /// a recent run completed within retention, an old run 31 days and more before the pass start
    /// </summary>
    /// <param name="recentCount">Number of runs completed within the retention period</param>
    /// <param name="oldCount">Number of runs completed past the retention period</param>
    /// <returns>Names with the completion offset before the pass start</returns>
    private static List<(string CorrelationId, TimeSpan CompletedBefore)> CreateSeeds(int recentCount, int oldCount)
    {
        var seeds = new List<(string CorrelationId, TimeSpan CompletedBefore)>();

        for (var index = 0; index < recentCount + oldCount; index++)
        {
            var baseAge = index < recentCount ? TimeSpan.FromMinutes(index + 1) : TimeSpan.FromDays(31) + TimeSpan.FromMinutes(index);

            seeds.Add(($"scan-{index:D2}", baseAge));
        }

        return seeds;
    }

    /// <summary>
    /// Create the correlation ids of the runs with the given ranks
    /// </summary>
    /// <param name="ranks">Ranks, 0 being the most recently completed run</param>
    /// <returns>Correlation ids</returns>
    private static List<string> RankIds(IEnumerable<int> ranks)
    {
        return ranks.Select(rank => $"scan-{rank:D2}")
                    .ToList();
    }

    /// <summary>
    /// Seed completed scan runs, let the referenced ones be referenced by an image relationship, run one cleanup pass
    /// at a fixed instant and read what remains
    /// </summary>
    /// <param name="seeds">Names and ages of the runs</param>
    /// <param name="referencedRanks">Ranks of the runs an image relationship refers to</param>
    /// <returns>Remaining correlation ids from the newest to the oldest completed run and the cleanup log message</returns>
    private async Task<(List<string?> RemainingCorrelationIds, string CleanupMessage)> RunCleanupAsync(List<(string CorrelationId, TimeSpan CompletedBefore)> seeds, params int[] referencedRanks)
    {
        var passStartedAtUtc = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var databaseName = Guid.NewGuid().ToString();
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddScoped(_ =>
                                    {
                                        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(databaseName)
                                                                                                               .Options;

                                        return new DockerUpdateGuardDbContext(options);
                                    });
        serviceCollection.AddScoped(_ => new ApplicationTelemetry());
        serviceCollection.AddScoped<ILiveImageInventoryQueryService, LiveImageInventoryQueryService>();

        var serviceProvider = serviceCollection.BuildServiceProvider();

        await using (serviceProvider.ConfigureAwait(false))
        {
            var scope = serviceProvider.CreateAsyncScope();

            await using (scope.ConfigureAwait(false))
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();

                await SeedAsync(dbContext, seeds, passStartedAtUtc, referencedRanks).ConfigureAwait(false);
            }

            var timeProvider = Substitute.For<TimeProvider>();

            timeProvider.GetUtcNow()
                        .Returns(passStartedAtUtc);

            var logger = new TestLogger<ScanCleanupBackgroundService>();
            var options = new DockerUpdateGuardOptions
                          {
                              Scanning = new ScanningOptions
                                         {
                                             CleanupIntervalMinutes = 60,
                                             RetainScanRunsDays = 30,
                                         },
                          };
            var service = new TestScanCleanupBackgroundService(logger,
                                                               new TestOptionsMonitor<DockerUpdateGuardOptions>(options),
                                                               serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                                                               timeProvider);

            await service.ExecuteOnceAsync(TestContext.CancellationToken)
                         .ConfigureAwait(false);

            var verificationScope = serviceProvider.CreateAsyncScope();

            await using (verificationScope.ConfigureAwait(false))
            {
                var dbContext = verificationScope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
                var remainingCorrelationIds = await dbContext.ScanRuns
                                                             .OrderByDescending(entity => entity.CompletedAtUtc)
                                                             .Select(entity => entity.CorrelationId)
                                                             .ToListAsync(TestContext.CancellationToken)
                                                             .ConfigureAwait(false);
                var cleanupMessage = logger.Entries.Where(entry => entry.EventId.Id == 2020)
                                                   .Select(entry => entry.Message)
                                                   .SingleOrDefault() ?? string.Empty;

                return (remainingCorrelationIds, cleanupMessage);
            }
        }
    }

    /// <summary>
    /// Seed the scan runs and the image relationships that refer to the runs of the given ranks
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="seeds">Names and ages of the runs</param>
    /// <param name="passStartedAtUtc">Instant the clock returns</param>
    /// <param name="referencedRanks">Ranks of the referenced runs</param>
    /// <returns>Task</returns>
    private async Task SeedAsync(DockerUpdateGuardDbContext dbContext,
                                 List<(string CorrelationId, TimeSpan CompletedBefore)> seeds,
                                 DateTimeOffset passStartedAtUtc,
                                 int[] referencedRanks)
    {
        var scanRuns = seeds.Select(seed => new ScanRun
                                            {
                                                Type = ScanRunType.ObservedImage,
                                                Status = ScanRunStatus.Succeeded,
                                                TriggerSource = ScanTriggerSource.Manual,
                                                StartedAtUtc = passStartedAtUtc - seed.CompletedBefore - TimeSpan.FromSeconds(5),
                                                CompletedAtUtc = passStartedAtUtc - seed.CompletedBefore,
                                                CorrelationId = seed.CorrelationId,
                                            })
                            .ToList();

        dbContext.ScanRuns.AddRange(scanRuns);

        var referenced = referencedRanks.ToList();

        if (referenced.Count > 0)
        {
            var imageCatalogRepository = new ImageCatalogRepository(dbContext);
            var childImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync(Registry,
                                                                                              "company/child",
                                                                                              "1.0.0",
                                                                                              "sha256:child",
                                                                                              cancellationToken: TestContext.CancellationToken)
                                                                .ConfigureAwait(false);
            var baseImageVersion = await imageCatalogRepository.GetOrCreateImageVersionAsync(Registry,
                                                                                             "company/base",
                                                                                             "1.0.0",
                                                                                             "sha256:base",
                                                                                             cancellationToken: TestContext.CancellationToken)
                                                               .ConfigureAwait(false);

            dbContext.ImageRelationships.AddRange(referenced.Select(rank => new ImageRelationship
                                                                            {
                                                                                ChildImageVersionId = childImageVersion.Id,
                                                                                BaseImageVersionId = baseImageVersion.Id,
                                                                                ScanRunId = scanRuns[rank].Id,
                                                                                RelationshipType = ImageRelationshipType.BaseImage,
                                                                            }));
        }

        await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                       .ConfigureAwait(false);
    }

    #endregion // Methods
}