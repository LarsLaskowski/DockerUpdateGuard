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

    #endregion // Methods
}