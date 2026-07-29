using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ScanCleanupBackgroundService"/>
/// </summary>
[TestClass]
public partial class ScanCleanupBackgroundServiceTests
{
    #region Methods

    /// <summary>
    /// Verify cleanup keeps only the latest 20 unreferenced scan runs
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ScanCleanupBackgroundServiceExecuteCoreAsyncRemovesUnreferencedScanRunsBeyondLatestTwentyAsync()
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

        var serviceProvider = serviceCollection.BuildServiceProvider();

        await using (serviceProvider.ConfigureAwait(false))
        {
            var scope = serviceProvider.CreateAsyncScope();

            await using (scope.ConfigureAwait(false))
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
                var now = DateTimeOffset.UtcNow;

                for (var index = 0; index < 25; index++)
                {
                    var startedAtUtc = now.AddMinutes(-(index + 1));

                    dbContext.ScanRuns.Add(new ScanRun
                                           {
                                               Type = ScanRunType.ObservedImage,
                                               Status = ScanRunStatus.Succeeded,
                                               TriggerSource = ScanTriggerSource.Manual,
                                               StartedAtUtc = startedAtUtc,
                                               CompletedAtUtc = startedAtUtc.AddSeconds(5),
                                               CorrelationId = $"scan-{index:D2}",
                                           });
                }

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
                                                             .OrderByDescending(entity => entity.StartedAtUtc)
                                                             .Select(entity => entity.CorrelationId)
                                                             .ToListAsync(CancellationToken.None)
                                                             .ConfigureAwait(false);

                Assert.HasCount(20,
                                remainingCorrelationIds,
                                "Cleanup must keep only the latest 20 unreferenced scan runs");
                Assert.AreSequenceEqual(Enumerable.Range(0, 20)
                                                  .Select(index => $"scan-{index:D2}")
                                                  .ToList(),
                                        remainingCorrelationIds,
                                        "Cleanup must retain the newest 20 scan runs and delete older history entries");
            }
        }
    }

    /// <summary>
    /// Verify cleanup does not delete running scan runs that are not part of retained history yet
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
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
                Assert.Contains("running-scan", remainingCorrelationIds, "Cleanup must not delete a running scan run that has not produced related entities yet");
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

    #endregion // Methods
}