using System.Data.Common;
using System.Net.Sockets;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard;

/// <summary>
/// Applies database migrations with startup resilience and cross-instance coordination
/// </summary>
public static class DatabaseMigrator
{
    #region Const fields

    /// <summary>
    /// Provider name reported by the Npgsql Entity Framework Core provider
    /// </summary>
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// PostgreSQL advisory lock key used to serialize migrations across competing instances
    /// </summary>
    private const long MigrationAdvisoryLockKey = 5_410_320_540_103_205;

    #endregion // Const fields

    #region Static methods

    /// <summary>
    /// Apply pending migrations, waiting for the database to become reachable and serializing competing instances
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="options">Database resilience options</param>
    /// <param name="logger">Logger</param>
    /// <param name="timeProvider">Time provider for all waits</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    public static async Task MigrateAsync(DockerUpdateGuardDbContext dbContext,
                                          DatabaseOptions options,
                                          ILogger logger,
                                          TimeProvider timeProvider,
                                          CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (dbContext.Database.IsRelational() == false)
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken)
                                    .ConfigureAwait(false);

            return;
        }

        await WaitForDatabaseAsync(dbContext, options, logger, timeProvider, cancellationToken).ConfigureAwait(false);

        var useAdvisoryLock = string.Equals(dbContext.Database.ProviderName, NpgsqlProviderName, StringComparison.Ordinal);
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        var waitStartedAt = timeProvider.GetUtcNow();

        try
        {
            // Run the advisory lock acquisition and the migration as one retriable unit. When the
            // Npgsql retrying execution strategy is active a transient fault re-runs the whole
            // delegate, so the session-scoped advisory lock is always re-acquired on the same
            // connection that the idempotent MigrateAsync then runs against instead of being
            // stranded on an abandoned connection
            await executionStrategy.ExecuteAsync(token => RunMigrationAsync(dbContext, useAdvisoryLock, options, waitStartedAt, logger, timeProvider, token), cancellationToken)
                                   .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.ApplicationDatabaseMigrationFailed(exception);

            throw;
        }
    }

    /// <summary>
    /// Poll the migration advisory lock until it is acquired or the configured lock timeout elapses
    /// </summary>
    /// <param name="tryAcquireLock">Attempt to take the lock without blocking; true when acquired</param>
    /// <param name="options">Database resilience options</param>
    /// <param name="waitStartedAt">Time the wait started, from which the deadline is measured</param>
    /// <param name="logger">Logger</param>
    /// <param name="timeProvider">Time provider for all waits</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    internal static async Task WaitForMigrationLockAsync(Func<CancellationToken, Task<bool>> tryAcquireLock,
                                                         DatabaseOptions options,
                                                         DateTimeOffset waitStartedAt,
                                                         ILogger logger,
                                                         TimeProvider timeProvider,
                                                         CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tryAcquireLock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var retryDelay = TimeSpan.FromSeconds(options.MigrationRetryDelaySeconds);
        var deadline = waitStartedAt + TimeSpan.FromSeconds(options.MigrationLockTimeoutSeconds);
        var attempt = 0;

        while (true)
        {
            attempt++;

            if (await tryAcquireLock(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var now = timeProvider.GetUtcNow();

            if (now >= deadline)
            {
                logger.ApplicationDatabaseMigrationLockTimedOut(options.MigrationLockTimeoutSeconds, attempt);

                throw new InvalidOperationException($"The migration advisory lock was not acquired within '{DockerUpdateGuardOptions.SectionName}:Database:MigrationLockTimeoutSeconds' ({options.MigrationLockTimeoutSeconds} seconds); another instance may still be applying migrations");
            }

            logger.ApplicationDatabaseMigrationLockWaiting(attempt, (now - waitStartedAt).TotalSeconds, retryDelay.TotalSeconds);

            await Task.Delay(retryDelay, timeProvider, cancellationToken)
                      .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Determine whether the exception represents a transient database connectivity failure
    /// </summary>
    /// <param name="exception">Exception to inspect</param>
    /// <returns>True when the failure is transient</returns>
    internal static bool IsTransientConnectionFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is DbException databaseException && databaseException.IsTransient)
        {
            return true;
        }

        if (exception is SocketException or TimeoutException)
        {
            return true;
        }

        return exception.InnerException is not null
               && IsTransientConnectionFailure(exception.InnerException);
    }

    /// <summary>
    /// Open the database connection, retrying transient failures until the configured startup timeout elapses
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="options">Database resilience options</param>
    /// <param name="logger">Logger</param>
    /// <param name="timeProvider">Time provider for all waits</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private static async Task WaitForDatabaseAsync(DockerUpdateGuardDbContext dbContext,
                                                   DatabaseOptions options,
                                                   ILogger logger,
                                                   TimeProvider timeProvider,
                                                   CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromSeconds(options.MigrationRetryDelaySeconds);
        var deadline = timeProvider.GetUtcNow() + TimeSpan.FromSeconds(options.MigrationStartupTimeoutSeconds);
        var attempt = 0;

        while (true)
        {
            attempt++;

            try
            {
                await dbContext.Database.OpenConnectionAsync(cancellationToken)
                                        .ConfigureAwait(false);
                await dbContext.Database.CloseConnectionAsync()
                                        .ConfigureAwait(false);

                return;
            }
            catch (Exception exception) when (IsTransientConnectionFailure(exception))
            {
                if (timeProvider.GetUtcNow() >= deadline)
                {
                    logger.ApplicationDatabaseUnavailable(attempt, exception);

                    throw;
                }

                logger.ApplicationDatabaseConnectionRetrying(attempt, retryDelay.TotalSeconds, exception);

                await Task.Delay(retryDelay, timeProvider, cancellationToken)
                          .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Acquire the advisory lock, apply pending migrations and release the lock on a single connection
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="useAdvisoryLock">True when the provider supports the PostgreSQL advisory lock</param>
    /// <param name="options">Database resilience options</param>
    /// <param name="waitStartedAt">Time the lock wait started, from which the deadline is measured</param>
    /// <param name="logger">Logger</param>
    /// <param name="timeProvider">Time provider for all waits</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private static async Task RunMigrationAsync(DockerUpdateGuardDbContext dbContext,
                                                bool useAdvisoryLock,
                                                DatabaseOptions options,
                                                DateTimeOffset waitStartedAt,
                                                ILogger logger,
                                                TimeProvider timeProvider,
                                                CancellationToken cancellationToken)
    {
        // Open the connection explicitly so that the advisory lock and the migration share one
        // session. Entity Framework Core reuses an explicitly opened connection and leaves it
        // open until it is closed again below.
        await dbContext.Database.OpenConnectionAsync(cancellationToken)
                                .ConfigureAwait(false);

        try
        {
            if (useAdvisoryLock)
            {
                await WaitForMigrationLockAsync(token => TryAcquireMigrationLockAsync(dbContext, token), options, waitStartedAt, logger, timeProvider, cancellationToken).ConfigureAwait(false);
            }

            // The migration statements run under their own command timeout; the lock queries before and
            // the unlock after keep the database command timeout, and the value the context had is restored
            // so that the later commands on this scoped context keep it.
            var previousCommandTimeout = dbContext.Database.GetCommandTimeout();

            dbContext.Database.SetCommandTimeout(options.MigrationCommandTimeoutSeconds);

            try
            {
                await dbContext.Database.MigrateAsync(cancellationToken)
                                        .ConfigureAwait(false);
            }
            finally
            {
                dbContext.Database.SetCommandTimeout(previousCommandTimeout);
            }
        }
        finally
        {
            await ReleaseAdvisoryLockAsync(dbContext, useAdvisoryLock, logger).ConfigureAwait(false);

            await dbContext.Database.CloseConnectionAsync()
                                    .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Try to take the migration advisory lock without blocking
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True when the lock was acquired</returns>
    private static async Task<bool> TryAcquireMigrationLockAsync(DockerUpdateGuardDbContext dbContext, CancellationToken cancellationToken)
    {
        var result = await dbContext.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({MigrationAdvisoryLockKey}) AS \"Value\"")
                                             .ToListAsync(cancellationToken)
                                             .ConfigureAwait(false);

        return result.Count > 0 && result[0];
    }

    /// <summary>
    /// Release the advisory lock without masking a migration failure
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="useAdvisoryLock">True when the provider supports the PostgreSQL advisory lock</param>
    /// <param name="logger">Logger</param>
    /// <returns>Task</returns>
    private static async Task ReleaseAdvisoryLockAsync(DockerUpdateGuardDbContext dbContext, bool useAdvisoryLock, ILogger logger)
    {
        if (useAdvisoryLock == false)
        {
            return;
        }

        try
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({MigrationAdvisoryLockKey})", CancellationToken.None)
                                    .ConfigureAwait(false);
        }
        catch (DbException exception)
        {
            // A failed release must never overwrite the migration outcome; the advisory lock is
            // also released automatically once the connection session ends on close
            logger.ApplicationDatabaseAdvisoryLockReleaseFailed(exception);
        }
        catch (InvalidOperationException exception)
        {
            // See above: the unlock is best-effort and is backstopped by the connection close
            logger.ApplicationDatabaseAdvisoryLockReleaseFailed(exception);
        }
    }

    #endregion // Static methods
}