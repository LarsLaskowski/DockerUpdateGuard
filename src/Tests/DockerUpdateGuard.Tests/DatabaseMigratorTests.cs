using System.Data.Common;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DatabaseMigrator"/>
/// </summary>
[TestClass]
public class DatabaseMigratorTests
{
    #region Constants

    /// <summary>
    /// Distinctive migration command timeout in seconds, different from the provider default of 30
    /// </summary>
    private const int MigrationCommandTimeoutSeconds = 777;

    #endregion // Constants

    #region Properties

    /// <summary>
    /// Test context providing information about and functionality for the current test run
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Create a time provider whose current time a test moves forward by hand
    /// </summary>
    /// <param name="now">Current time of the returned provider</param>
    /// <returns>Time provider recording every wait</returns>
    private static WaitRecordingTimeProvider CreateLockClock(out StrongBox<DateTimeOffset> now)
    {
        var clock = new WaitRecordingTimeProvider();
        var current = new StrongBox<DateTimeOffset>(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        clock.TimeProvider.GetUtcNow().Returns(_ => current.Value);
        now = current;

        return clock;
    }

    /// <summary>
    /// Start the lock wait with a try-lock that fails a number of times and then succeeds
    /// </summary>
    /// <param name="attempts">Counter of the try-lock calls</param>
    /// <param name="failures">Number of calls that report the lock as held</param>
    /// <param name="databaseOptions">Database options</param>
    /// <param name="waitStartedAt">Time the wait started</param>
    /// <param name="logger">Logger</param>
    /// <param name="timeProvider">Time provider</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task of the wait</returns>
    private static Task StartLockWait(StrongBox<int> attempts,
                                      int failures,
                                      DatabaseOptions databaseOptions,
                                      DateTimeOffset waitStartedAt,
                                      ILogger logger,
                                      TimeProvider timeProvider,
                                      CancellationToken cancellationToken)
    {
        return Task.Run(() => DatabaseMigrator.WaitForMigrationLockAsync(_ => Task.FromResult(++attempts.Value > failures),
                                                                         databaseOptions,
                                                                         waitStartedAt,
                                                                         logger,
                                                                         timeProvider,
                                                                         cancellationToken),
                        CancellationToken.None);
    }

    /// <summary>
    /// Move the clock by the delay of every started wait and let that wait elapse until the lock wait ends
    /// </summary>
    /// <param name="clock">Time provider recording the waits</param>
    /// <param name="now">Current time of the clock</param>
    /// <param name="delay">Delay of each wait</param>
    /// <param name="waitTask">Task of the lock wait</param>
    /// <returns>Task</returns>
    private static async Task DriveWaitsAsync(WaitRecordingTimeProvider clock,
                                              StrongBox<DateTimeOffset> now,
                                              TimeSpan delay,
                                              Task waitTask)
    {
        while (await clock.WaitForNextWaitAsync(waitTask).ConfigureAwait(false))
        {
            now.Value += delay;
            clock.ElapseLastWait();
        }
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify migration completes for non-relational providers without attempting provider-specific coordination SQL
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DatabaseMigratorNonRelationalProviderCreatesStoreAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;
        var databaseOptions = new DatabaseOptions();

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            await DatabaseMigrator.MigrateAsync(dbContext, databaseOptions, NullLogger.Instance, TimeProvider.System, CancellationToken.None)
                                  .ConfigureAwait(false);

            Assert.IsFalse(dbContext.Database.IsRelational(),
                           "The in-memory provider must remain non-relational so the migrator skips advisory locking");
            Assert.IsTrue(await dbContext.Database.CanConnectAsync(TestContext.CancellationToken)
                                                  .ConfigureAwait(false),
                          "The migrator must ensure the non-relational store is created");
        }
    }

    /// <summary>
    /// Verify the relational migration path runs through the execution strategy and surfaces a migration failure
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DatabaseMigratorRelationalProviderSurfacesMigrationFailureAsync()
    {
        // The migrations are generated for Npgsql, so Entity Framework Core builds a divergent
        // model for SQLite and MigrateAsync reports pending model changes. That deterministic
        // failure exercises the relational orchestration end to end: the connection wait, the
        // execution-strategy wrapped migration run and the diagnostic catch path
        var connection = new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync(TestContext.CancellationToken)
                        .ConfigureAwait(false);

        using (connection)
        {
            var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseSqlite(connection)
                                                                                   .Options;
            var databaseOptions = new DatabaseOptions();
            var dbContext = new DockerUpdateGuardDbContext(options);
            Exception? caught = null;

            await using (dbContext.ConfigureAwait(false))
            {
                try
                {
                    await DatabaseMigrator.MigrateAsync(dbContext, databaseOptions, NullLogger.Instance, TimeProvider.System, CancellationToken.None)
                                          .ConfigureAwait(false);
                }
                catch (InvalidOperationException exception)
                {
                    caught = exception;
                }
            }

            Assert.IsNotNull(caught,
                             "The relational migration path must surface a migration failure rather than swallow it");
        }
    }

    /// <summary>
    /// Verify every command of the migration runs under the migration command timeout
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task DatabaseMigratorMigrationCommandsUseMigrationCommandTimeoutAsync()
    {
        var interceptor = new CommandTimeoutRecordingInterceptor();
        var databaseOptions = new DatabaseOptions
                              {
                                  MigrationCommandTimeoutSeconds = MigrationCommandTimeoutSeconds
                              };

        var run = await RunMigrationWithInterceptorAsync(interceptor, databaseOptions, null).ConfigureAwait(false);

        Assert.AreSame(interceptor.Failure, run.Caught, "The migration must fail with the exception of the interceptor");
        Assert.IsNotEmpty(interceptor.CommandTimeouts, "The migration must execute at least one command");
        Assert.IsTrue(interceptor.CommandTimeouts.All(timeout => timeout == MigrationCommandTimeoutSeconds),
                      $"Every migration command must run under {MigrationCommandTimeoutSeconds} seconds but ran under [{string.Join(", ", interceptor.CommandTimeouts)}]");
    }

    /// <summary>
    /// Verify the previous command timeout of the context is restored after a failed migration
    /// </summary>
    /// <param name="previousTimeout">Command timeout set on the context before the migration, null for none</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    [DataRow(null)]
    [DataRow(45)]
    public async Task DatabaseMigratorMigrationRestoresPreviousCommandTimeoutAsync(int? previousTimeout)
    {
        var interceptor = new CommandTimeoutRecordingInterceptor();
        var databaseOptions = new DatabaseOptions
                              {
                                  MigrationCommandTimeoutSeconds = MigrationCommandTimeoutSeconds
                              };

        var run = await RunMigrationWithInterceptorAsync(interceptor, databaseOptions, previousTimeout).ConfigureAwait(false);

        Assert.AreSame(interceptor.Failure, run.Caught, "The migration must fail with the exception of the interceptor");
        Assert.IsNotEmpty(interceptor.CommandTimeouts, "The migration must execute at least one command");
        Assert.AreEqual(MigrationCommandTimeoutSeconds, interceptor.CommandTimeouts[0], "The migration command must run under the migration command timeout");
        Assert.AreEqual(previousTimeout, run.TimeoutAfter, "The command timeout of the context must be the value it had before the migration");
    }

    /// <summary>
    /// Verify an unreachable database is retried and ultimately surfaces a connection failure
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DatabaseMigratorUnreachableDatabaseThrowsAfterTimeoutAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=dug;Timeout=1;Command Timeout=1")
                                                                               .Options;
        var databaseOptions = new DatabaseOptions
                              {
                                  MigrationStartupTimeoutSeconds = 1,
                                  MigrationRetryDelaySeconds = 0,
                              };
        var dbContext = new DockerUpdateGuardDbContext(options);
        var startedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = Substitute.For<TimeProvider>();
        Exception? caught = null;

        // The first read fixes the deadline, every later read is past it, so no real second has to elapse
        timeProvider.GetUtcNow().Returns(startedAt, startedAt.AddHours(1));

        await using (dbContext.ConfigureAwait(false))
        {
            try
            {
                await DatabaseMigrator.MigrateAsync(dbContext, databaseOptions, NullLogger.Instance, timeProvider, CancellationToken.None)
                                      .ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                caught = exception;
            }
        }

        Assert.IsNotNull(caught,
                         "An unreachable database must surface a connection failure once the startup timeout elapses");
    }

    /// <summary>
    /// Verify a lock held longer than the command timeout is polled until it is released
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    [TestCategory("AC6")]
    public async Task DatabaseMigratorLockHeldLongerThanCommandTimeoutWaitsAndAcquiresAsync()
    {
        var databaseOptions = new DatabaseOptions
                              {
                                  MigrationLockTimeoutSeconds = 900,
                                  MigrationRetryDelaySeconds = 5,
                              };
        var logger = new TestLogger<DatabaseMigratorTests>();
        var clock = CreateLockClock(out var now);
        var attempts = new StrongBox<int>();

        var waitTask = StartLockWait(attempts, 10, databaseOptions, now.Value, logger, clock.TimeProvider, CancellationToken.None);

        await DriveWaitsAsync(clock, now, TimeSpan.FromSeconds(5), waitTask).ConfigureAwait(false);
        await waitTask.ConfigureAwait(false);

        Assert.AreEqual(11, attempts.Value, "The lock must be tried ten times while held and once more when released");
        Assert.HasCount(10,
                        clock.Waits,
                        "Every failed attempt must be followed by one wait");
        Assert.IsTrue(clock.Waits.All(wait => wait == TimeSpan.FromSeconds(5)),
                      "Every wait must last the configured retry delay");
        Assert.AreSequenceEqual(Enumerable.Range(1, 10)
                                          .Select(attempt => $"DockerUpdateGuard migration advisory lock is held by another instance; attempt {attempt} after {(attempt - 1) * 5} seconds, retrying in 5 seconds")
                                          .ToArray(),
                                logger.Entries.Where(entry => entry.EventId.Id == 1007)
                                              .Select(entry => entry.Message)
                                              .ToArray(),
                                "Each failed attempt must log the attempt number and the seconds waited so far");
        Assert.DoesNotContain(entry => entry.EventId.Id == 1008 || entry.EventId.Id == 1005,
                              logger.Entries,
                              "A lock that is acquired in time must log neither the timeout nor a migration failure");
    }

    /// <summary>
    /// Verify a lock that is not acquired before the deadline fails with a logged error
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    [TestCategory("AC6")]
    public async Task DatabaseMigratorLockNotAcquiredBeforeDeadlineFailsWithLoggedErrorAsync()
    {
        var databaseOptions = new DatabaseOptions
                              {
                                  MigrationLockTimeoutSeconds = 10,
                                  MigrationRetryDelaySeconds = 5,
                              };
        var logger = new TestLogger<DatabaseMigratorTests>();
        var clock = CreateLockClock(out var now);
        var attempts = new StrongBox<int>();

        var waitTask = StartLockWait(attempts, int.MaxValue, databaseOptions, now.Value, logger, clock.TimeProvider, CancellationToken.None);

        await DriveWaitsAsync(clock, now, TimeSpan.FromSeconds(5), waitTask).ConfigureAwait(false);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => waitTask)
                                    .ConfigureAwait(false);

        Assert.AreEqual("The migration advisory lock was not acquired within 'DockerUpdateGuard:Database:MigrationLockTimeoutSeconds' (10 seconds); another instance may still be applying migrations",
                        exception.Message,
                        "The failure must name the configuration key and the configured timeout");
        Assert.AreEqual(3, attempts.Value, "The lock must be tried at 0, 5 and 10 seconds");
        Assert.HasCount(2,
                        clock.Waits,
                        "No wait may follow the attempt that reached the deadline");
        Assert.HasCount(1,
                        logger.Entries.Where(entry => entry.EventId.Id == 1008 && entry.LogLevel == LogLevel.Error),
                        "The timeout must be logged once as an error");
        Assert.Contains(entry => entry.EventId.Id == 1008
                                 && entry.Message.Contains("within 10 seconds after 3 attempts", StringComparison.Ordinal),
                        logger.Entries,
                        "The timeout entry must carry the timeout and the number of attempts");
    }

    /// <summary>
    /// Verify cancelling the token during a wait ends the wait without a further attempt
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [TestCategory("AC6")]
    public async Task DatabaseMigratorLockWaitCancelledStopsWaitingAsync()
    {
        var databaseOptions = new DatabaseOptions
                              {
                                  MigrationLockTimeoutSeconds = 900,
                                  MigrationRetryDelaySeconds = 5,
                              };
        var logger = new TestLogger<DatabaseMigratorTests>();
        var clock = CreateLockClock(out var now);
        var attempts = new StrongBox<int>();

        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            var waitTask = StartLockWait(attempts, int.MaxValue, databaseOptions, now.Value, logger, clock.TimeProvider, cancellationTokenSource.Token);

            var waitStarted = await clock.WaitForNextWaitAsync(waitTask).ConfigureAwait(false);

            Assert.IsTrue(waitStarted, "The first failed attempt must start a wait");

            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<OperationCanceledException>(() => waitTask).ConfigureAwait(false);
        }

        Assert.AreEqual(1, attempts.Value, "No attempt may follow the cancellation");
        Assert.DoesNotContain(entry => entry.EventId.Id == 1008,
                              logger.Entries,
                              "A cancelled wait must not be reported as a timeout");
    }

    /// <summary>
    /// Verify exhausted execution strategy retries are logged and rethrown unchanged
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    [TestCategory("AC6")]
    public async Task DatabaseMigratorRetryLimitExceededIsLoggedAsync()
    {
        var retryLimitExceeded = new RetryLimitExceededException("retries exhausted", new InvalidOperationException("lock wait"));
        var logger = new TestLogger<DatabaseMigratorTests>();
        var startedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = Substitute.For<TimeProvider>();

        timeProvider.GetUtcNow().Returns(startedAt);

        var connection = new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync(TestContext.CancellationToken)
                        .ConfigureAwait(false);

        using (connection)
        {
            var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseSqlite(connection,
                                                                                              sqlite => sqlite.ExecutionStrategy(_ => new ThrowingExecutionStrategy(retryLimitExceeded, 1)))
                                                                                   .Options;
            var dbContext = new DockerUpdateGuardDbContext(options);

            await using (dbContext.ConfigureAwait(false))
            {
                var thrown = await Assert.ThrowsExactlyAsync<RetryLimitExceededException>(() => DatabaseMigrator.MigrateAsync(dbContext, new DatabaseOptions(), logger, timeProvider, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken))
                                         .ConfigureAwait(false);

                Assert.AreSame(retryLimitExceeded, thrown, "The exhausted retries must be rethrown as the same instance");
            }
        }

        Assert.Contains(entry => entry.EventId.Id == 1005
                                 && ReferenceEquals(entry.Exception, retryLimitExceeded),
                        logger.Entries,
                        "The exhausted retries must be logged as a migration failure with the exception");
    }

    /// <summary>
    /// Verify a cancellation from the execution strategy is rethrown without logging a migration failure
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [TestCategory("AC6")]
    public async Task DatabaseMigratorMigrateCancelledDoesNotLogFailureAsync()
    {
        var cancellation = new OperationCanceledException("cancelled");
        var logger = new TestLogger<DatabaseMigratorTests>();
        var startedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = Substitute.For<TimeProvider>();

        timeProvider.GetUtcNow().Returns(startedAt);

        var connection = new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync(TestContext.CancellationToken)
                        .ConfigureAwait(false);

        using (connection)
        {
            var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseSqlite(connection,
                                                                                              sqlite => sqlite.ExecutionStrategy(_ => new ThrowingExecutionStrategy(cancellation, 1)))
                                                                                   .Options;
            var dbContext = new DockerUpdateGuardDbContext(options);

            await using (dbContext.ConfigureAwait(false))
            {
                var thrown = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => DatabaseMigrator.MigrateAsync(dbContext, new DatabaseOptions(), logger, timeProvider, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken))
                                         .ConfigureAwait(false);

                Assert.AreSame(cancellation, thrown, "The cancellation must be rethrown as the same instance");
            }
        }

        Assert.DoesNotContain(entry => entry.EventId.Id == 1005,
                              logger.Entries,
                              "A cancellation must not be logged as a migration failure");
    }

    /// <summary>
    /// Verify a transient connection failure before the startup deadline is retried after the retry delay
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DatabaseMigratorUnreachableDatabaseRetriesBeforeDeadlineAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=dug;Timeout=1;Command Timeout=1")
                                                                               .Options;
        var databaseOptions = new DatabaseOptions
                              {
                                  MigrationStartupTimeoutSeconds = 1,
                                  MigrationRetryDelaySeconds = 0,
                              };
        var logger = new TestLogger<DatabaseMigratorTests>();
        var dbContext = new DockerUpdateGuardDbContext(options);
        var startedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = Substitute.For<TimeProvider>();
        Exception? caught = null;

        // The deadline read and the first failure are before the deadline, the second failure is past it
        timeProvider.GetUtcNow().Returns(startedAt, startedAt, startedAt.AddHours(1));

        await using (dbContext.ConfigureAwait(false))
        {
            try
            {
                await DatabaseMigrator.MigrateAsync(dbContext, databaseOptions, logger, timeProvider, CancellationToken.None)
                                      .ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                caught = exception;
            }
        }

        Assert.IsNotNull(caught, "The connection failure must surface once the deadline has passed");
        Assert.HasCount(2,
                        logger.Entries.Where(entry => entry.Exception is not null),
                        "The first failure must be logged as a retry and the second as the final failure");
    }

    /// <summary>
    /// Verify transient connectivity failures are classified as retryable
    /// </summary>
    [TestMethod]
    public void DatabaseMigratorIsTransientConnectionFailureClassifiesTransientFaults()
    {
        var transientDatabaseException = Substitute.For<DbException>();

        transientDatabaseException.IsTransient.Returns(true);

        Assert.IsTrue(DatabaseMigrator.IsTransientConnectionFailure(new SocketException()),
                      "Socket failures must be treated as transient");
        Assert.IsTrue(DatabaseMigrator.IsTransientConnectionFailure(new TimeoutException()),
                      "Timeouts must be treated as transient");
        Assert.IsTrue(DatabaseMigrator.IsTransientConnectionFailure(transientDatabaseException),
                      "Database exceptions flagged as transient must be treated as transient");
        Assert.IsTrue(DatabaseMigrator.IsTransientConnectionFailure(new InvalidOperationException("wrapper", new SocketException())),
                      "A wrapped socket failure must be treated as transient");
    }

    /// <summary>
    /// Verify non-transient failures are classified as fail-fast
    /// </summary>
    [TestMethod]
    public void DatabaseMigratorIsTransientConnectionFailureRejectsPermanentFaults()
    {
        var permanentDatabaseException = Substitute.For<DbException>();

        permanentDatabaseException.IsTransient.Returns(false);

        Assert.IsFalse(DatabaseMigrator.IsTransientConnectionFailure(permanentDatabaseException),
                       "Database exceptions not flagged as transient must fail fast");
        Assert.IsFalse(DatabaseMigrator.IsTransientConnectionFailure(new InvalidOperationException("permanent")),
                       "Unrelated failures must fail fast");
    }

    /// <summary>
    /// Run the migration on SQLite with the interceptor and report the failure and the final command timeout of the context
    /// </summary>
    /// <param name="interceptor">Interceptor that fails the first command</param>
    /// <param name="databaseOptions">Database options</param>
    /// <param name="previousTimeout">Command timeout set on the context before the migration, null for none</param>
    /// <returns>Thrown exception and the command timeout of the context after the call</returns>
    private async Task<(Exception? Caught, int? TimeoutAfter)> RunMigrationWithInterceptorAsync(CommandTimeoutRecordingInterceptor interceptor,
                                                                                                DatabaseOptions databaseOptions,
                                                                                                int? previousTimeout)
    {
        var connection = new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync(TestContext.CancellationToken)
                        .ConfigureAwait(false);

        using (connection)
        {
            var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseSqlite(connection)
                                                                                   .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
                                                                                   .AddInterceptors(interceptor)
                                                                                   .Options;
            var dbContext = new DockerUpdateGuardDbContext(options);

            await using (dbContext.ConfigureAwait(false))
            {
                Exception? caught = null;

                dbContext.Database.SetCommandTimeout(previousTimeout);

                try
                {
                    await DatabaseMigrator.MigrateAsync(dbContext, databaseOptions, NullLogger.Instance, TimeProvider.System, CancellationToken.None)
                                          .ConfigureAwait(false);
                }
                catch (InvalidOperationException exception)
                {
                    caught = exception;
                }

                return (caught, dbContext.Database.GetCommandTimeout());
            }
        }
    }

    #endregion // Methods
}