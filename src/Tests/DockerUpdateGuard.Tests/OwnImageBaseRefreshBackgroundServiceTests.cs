using System.Runtime.CompilerServices;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="OwnImageBaseRefreshBackgroundService"/>
/// </summary>
[TestClass]
public class OwnImageBaseRefreshBackgroundServiceTests
{
    #region Constants

    /// <summary>
    /// Floor of the interval in minutes
    /// </summary>
    private const int FloorMinutes = 10;

    /// <summary>
    /// Number of enabled observed images that lifts the dynamic interval above the floor
    /// </summary>
    private const int EnabledImageCount = 5;

    /// <summary>
    /// Retry limit of the test execution strategy
    /// </summary>
    private const int RetryLimit = 5;

    #endregion // Constants

    #region Properties

    /// <summary>
    /// Test context providing information about and functionality for the current test run
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Create options whose dynamic interval for the enabled image count lies above the floor
    /// </summary>
    /// <returns>Application options</returns>
    private static DockerUpdateGuardOptions CreateOptions()
    {
        return new DockerUpdateGuardOptions
               {
                   Scanning = new ScanningOptions
                              {
                                  OwnImageBaseScanIntervalMinutes = FloorMinutes,
                                  DockerHubRequestLimitWindowHours = 6,
                                  DockerHubRequestLimitPerWindow = 100,
                                  DockerHubReservedManualRequestsPerWindow = 0,
                              },
               };
    }

    /// <summary>
    /// Create a service provider whose database runs on the supplied connection and whose scan orchestrator runs the supplied action
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="onScan">Action run when the job scans</param>
    /// <param name="configure">Optional additional configuration of the context options</param>
    /// <returns>Service provider</returns>
    private static ServiceProvider CreateServiceProvider(SqliteConnection connection, Action onScan, Action<DbContextOptionsBuilder<DockerUpdateGuardDbContext>>? configure = null)
    {
        var orchestrator = Substitute.For<IImageScanOrchestrator>();
        var services = new ServiceCollection();

        orchestrator.ScanAllAsync(Arg.Any<ScanTriggerSource>(), Arg.Any<CancellationToken>())
                    .Returns(_ =>
                             {
                                 onScan();

                                 return Task.CompletedTask;
                             });
        services.AddSingleton(orchestrator);
        services.AddScoped(_ =>
                           {
                               var optionsBuilder = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseSqlite(connection);

                               configure?.Invoke(optionsBuilder);

                               return new DockerUpdateGuardDbContext(optionsBuilder.Options);
                           });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Create the database schema and enabled observed images on the supplied connection
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <returns>Task</returns>
    private static async Task SeedEnabledObservedImagesAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseSqlite(connection)
                                                                               .Options;
        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            await dbContext.Database.EnsureCreatedAsync(CancellationToken.None).ConfigureAwait(false);

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

            for (var index = 0; index < EnabledImageCount; index++)
            {
                dbContext.ObservedImages.Add(new ObservedImage
                                             {
                                                 Name = $"Image {index}",
                                                 CurrentImageVersion = imageVersion,
                                             });
            }

            await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify a database error while counting the enabled images keeps the job waiting its last known dynamic interval
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task OwnImageBaseRefreshBackgroundServiceExecuteAsyncObservedImageCountFailsUsesLastKnownIntervalAsync()
    {
        var options = CreateOptions();
        var expectedInterval = ObservedImageScanIntervalCalculator.CalculateInterval(options.Scanning, EnabledImageCount);
        var logger = new TestLogger<OwnImageBaseRefreshBackgroundService>();
        var clock = new WaitRecordingTimeProvider();

        using (var connection = new SqliteConnection("Data Source=:memory:"))
            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                await connection.OpenAsync(TestContext.CancellationToken).ConfigureAwait(false);

                await SeedEnabledObservedImagesAsync(connection).ConfigureAwait(false);

                // Closing an in-memory database drops it, so the next count fails with a missing table
                var serviceProvider = CreateServiceProvider(connection, connection.Close);

                await using (serviceProvider.ConfigureAwait(false))
                {
                    var service = new TestOwnImageBaseRefreshBackgroundService(logger,
                                                                               new TestOptionsMonitor<DockerUpdateGuardOptions>(options),
                                                                               serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                                                                               clock.TimeProvider);
                    var runTask = service.RunAsync(cancellationTokenSource.Token);

                    var waitStarted = await clock.WaitForNextWaitAsync(runTask).ConfigureAwait(false);

                    Assert.IsTrue(waitStarted, "A database error while counting the enabled images must not end the job's task");

                    await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
                    await runTask.ConfigureAwait(false);

                    Assert.IsGreaterThan(TimeSpan.FromMinutes(FloorMinutes), expectedInterval, "The test data must lift the dynamic interval above the floor");
                    Assert.AreSequenceEqual(new[] { expectedInterval },
                                            clock.Waits,
                                            "The job must wait the last dynamic interval it read successfully");
                    Assert.Contains(entry => entry.EventId.Id == 2005
                                             && entry.LogLevel == LogLevel.Warning
                                             && entry.Exception is not null,
                                    logger.Entries,
                                    "The failed count must be logged as warning 2005 with the exception");
                }
            }
    }

    /// <summary>
    /// Verify the job waits the configured own-image interval when its first interval lookups fail
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task OwnImageBaseRefreshBackgroundServiceExecuteAsyncFirstIntervalLookupFailsUsesOwnImageBaseScanIntervalAsync()
    {
        var options = CreateOptions();
        var logger = new TestLogger<OwnImageBaseRefreshBackgroundService>();
        var clock = new WaitRecordingTimeProvider();

        using (var connection = new SqliteConnection("Data Source=:memory:"))
            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                // No schema is created, so every count fails with a missing table
                await connection.OpenAsync(TestContext.CancellationToken).ConfigureAwait(false);

                var serviceProvider = CreateServiceProvider(connection,
                                                            () =>
                                                            {
                                                            });

                await using (serviceProvider.ConfigureAwait(false))
                {
                    var service = new TestOwnImageBaseRefreshBackgroundService(logger,
                                                                               new TestOptionsMonitor<DockerUpdateGuardOptions>(options),
                                                                               serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                                                                               clock.TimeProvider);
                    var runTask = service.RunAsync(cancellationTokenSource.Token);

                    var waitStarted = await clock.WaitForNextWaitAsync(runTask).ConfigureAwait(false);

                    Assert.IsTrue(waitStarted, "A failing first interval lookup must not end the job's task");

                    await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
                    await runTask.ConfigureAwait(false);

                    Assert.AreSequenceEqual(new[] { TimeSpan.FromMinutes(FloorMinutes) },
                                            clock.Waits,
                                            "Before its first successful lookup the job must wait OwnImageBaseScanIntervalMinutes");
                    Assert.Contains(entry => entry.EventId.Id == 2005
                                             && entry.LogLevel == LogLevel.Warning
                                             && entry.Exception is not null,
                                    logger.Entries,
                                    "The failed lookup must be logged as warning 2005 with the exception");
                }
            }
    }

    /// <summary>
    /// Verify the interval lookup counts the enabled images through the asynchronous query path with the stopping token
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task OwnImageBaseRefreshBackgroundServiceExecuteAsyncIntervalLookupCountsWithStoppingTokenAsync()
    {
        var options = CreateOptions();
        var expectedInterval = ObservedImageScanIntervalCalculator.CalculateInterval(options.Scanning, EnabledImageCount);
        var logger = new TestLogger<OwnImageBaseRefreshBackgroundService>();
        var clock = new WaitRecordingTimeProvider();
        var interceptor = new RecordingCommandInterceptor();

        using (var connection = new SqliteConnection("Data Source=:memory:"))
            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                await connection.OpenAsync(TestContext.CancellationToken).ConfigureAwait(false);

                await SeedEnabledObservedImagesAsync(connection).ConfigureAwait(false);

                var serviceProvider = CreateServiceProvider(connection,
                                                            () =>
                                                            {
                                                            },
                                                            optionsBuilder => optionsBuilder.AddInterceptors(interceptor));

                await using (serviceProvider.ConfigureAwait(false))
                {
                    var service = new TestOwnImageBaseRefreshBackgroundService(logger,
                                                                               new TestOptionsMonitor<DockerUpdateGuardOptions>(options),
                                                                               serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                                                                               clock.TimeProvider);
                    var runTask = service.RunAsync(cancellationTokenSource.Token);

                    var waitStarted = await clock.WaitForNextWaitAsync(runTask).ConfigureAwait(false);

                    Assert.IsTrue(waitStarted, "The job must start its first wait");

                    await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
                    await runTask.ConfigureAwait(false);

                    Assert.AreEqual(0, interceptor.SynchronousExecutions, "The interval lookup must not execute a synchronous query");
                    Assert.IsNotEmpty(interceptor.AsynchronousTokens, "The interval lookup must execute an asynchronous query");
                    Assert.IsTrue(interceptor.AsynchronousTokens.All(token => token == cancellationTokenSource.Token),
                                  "The interval lookup must hand the job's stopping token to its query");
                    Assert.AreSequenceEqual(new[] { expectedInterval },
                                            clock.Waits,
                                            "The job must wait the dynamic interval of the seeded enabled image count");
                }
            }
    }

    /// <summary>
    /// Verify a shutdown while the retrying execution strategy repeats a failing count ends the lookup without a further attempt
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task OwnImageBaseRefreshBackgroundServiceExecuteAsyncShutdownDuringCountRetriesStopsRetryingAsync()
    {
        var options = CreateOptions();
        var logger = new TestLogger<OwnImageBaseRefreshBackgroundService>();
        var clock = new WaitRecordingTimeProvider();
        var retryDecisions = new StrongBox<int>();

        using (var connection = new SqliteConnection("Data Source=:memory:"))
            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                // No schema is created, so every count fails with a missing table
                await connection.OpenAsync(TestContext.CancellationToken).ConfigureAwait(false);

                var serviceProvider = CreateServiceProvider(connection,
                                                            () =>
                                                            {
                                                            },
                                                            optionsBuilder => optionsBuilder.UseSqlite(connection,
                                                                                                       sqlite => sqlite.ExecutionStrategy(dependencies => new CancellingRetryExecutionStrategy(dependencies,
                                                                                                                                                                                               RetryLimit,
                                                                                                                                                                                               cancellationTokenSource,
                                                                                                                                                                                               retryDecisions))));

                await using (serviceProvider.ConfigureAwait(false))
                {
                    var service = new TestOwnImageBaseRefreshBackgroundService(logger,
                                                                               new TestOptionsMonitor<DockerUpdateGuardOptions>(options),
                                                                               serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                                                                               clock.TimeProvider);

                    await service.RunAsync(cancellationTokenSource.Token).WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken).ConfigureAwait(false);

                    Assert.AreEqual(1, retryDecisions.Value, "A cancelled lookup must not retry the failing count again");
                    Assert.DoesNotContain(entry => entry.EventId.Id == 2005 || entry.EventId.Id == 2004,
                                          logger.Entries,
                                          "A shutdown during the retries must log neither a lookup warning nor a run failure");
                }
            }
    }

    #endregion // Methods
}