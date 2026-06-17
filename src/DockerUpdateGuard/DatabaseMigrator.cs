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
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    public static async Task MigrateAsync(DockerUpdateGuardDbContext dbContext,
                                          DatabaseOptions options,
                                          ILogger logger,
                                          CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (dbContext.Database.IsRelational() == false)
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken)
                                    .ConfigureAwait(false);

            return;
        }

        await WaitForDatabaseAsync(dbContext, options, logger, cancellationToken)
            .ConfigureAwait(false);

        var useAdvisoryLock = string.Equals(dbContext.Database.ProviderName, NpgsqlProviderName, StringComparison.Ordinal);
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        try
        {
            // Run the advisory lock acquisition and the migration as one retriable unit. When the
            // Npgsql retrying execution strategy is active a transient fault re-runs the whole
            // delegate, so the session-scoped advisory lock is always re-acquired on the same
            // connection that the idempotent MigrateAsync then runs against instead of being
            // stranded on an abandoned connection
            await executionStrategy.ExecuteAsync(token => RunMigrationAsync(dbContext, useAdvisoryLock, logger, token), cancellationToken)
                                   .ConfigureAwait(false);
        }
        catch (DbException exception)
        {
            logger.ApplicationDatabaseMigrationFailed(exception);

            throw;
        }
        catch (InvalidOperationException exception)
        {
            logger.ApplicationDatabaseMigrationFailed(exception);

            throw;
        }
    }

    /// <summary>
    /// Open the database connection, retrying transient failures until the configured startup timeout elapses
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="options">Database resilience options</param>
    /// <param name="logger">Logger</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private static async Task WaitForDatabaseAsync(DockerUpdateGuardDbContext dbContext,
                                                   DatabaseOptions options,
                                                   ILogger logger,
                                                   CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromSeconds(options.MigrationRetryDelaySeconds);
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(options.MigrationStartupTimeoutSeconds);
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
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    logger.ApplicationDatabaseUnavailable(attempt, exception);

                    throw;
                }

                logger.ApplicationDatabaseConnectionRetrying(attempt, retryDelay.TotalSeconds, exception);

                await Task.Delay(retryDelay, cancellationToken)
                          .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Acquire the advisory lock, apply pending migrations and release the lock on a single connection
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="useAdvisoryLock">True when the provider supports the PostgreSQL advisory lock</param>
    /// <param name="logger">Logger</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private static async Task RunMigrationAsync(DockerUpdateGuardDbContext dbContext,
                                                bool useAdvisoryLock,
                                                ILogger logger,
                                                CancellationToken cancellationToken)
    {
        // Open the connection explicitly so the advisory lock and the migration share one session;
        // Entity Framework Core reuses an explicitly opened connection and leaves it open until it
        // is closed again below
        await dbContext.Database.OpenConnectionAsync(cancellationToken)
                                .ConfigureAwait(false);

        try
        {
            if (useAdvisoryLock)
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({MigrationAdvisoryLockKey})", cancellationToken)
                                        .ConfigureAwait(false);
            }

            await dbContext.Database.MigrateAsync(cancellationToken)
                                    .ConfigureAwait(false);
        }
        finally
        {
            await ReleaseAdvisoryLockAsync(dbContext, useAdvisoryLock, logger)
                .ConfigureAwait(false);

            await dbContext.Database.CloseConnectionAsync()
                                    .ConfigureAwait(false);
        }
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

    #endregion // Static methods
}
