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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.ApplicationDatabaseMigrationFailed(exception);

            throw;
        }
        finally
        {
            if (useAdvisoryLock)
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({MigrationAdvisoryLockKey})", CancellationToken.None)
                                        .ConfigureAwait(false);
            }

            await dbContext.Database.CloseConnectionAsync()
                                    .ConfigureAwait(false);
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
    /// Determine whether the exception represents a transient database connectivity failure
    /// </summary>
    /// <param name="exception">Exception to inspect</param>
    /// <returns>True when the failure is transient</returns>
    private static bool IsTransientConnectionFailure(Exception exception)
    {
        return exception is DbException
                            or SocketException
                            or TimeoutException
               || exception.InnerException is SocketException
                                               or TimeoutException;
    }

    #endregion // Static methods
}
