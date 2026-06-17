using System.Data.Common;
using System.Net.Sockets;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DatabaseMigrator"/>
/// </summary>
[TestClass]
public class DatabaseMigratorTests
{
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
            await DatabaseMigrator.MigrateAsync(dbContext, databaseOptions, NullLogger.Instance, CancellationToken.None)
                                  .ConfigureAwait(false);

            Assert.IsFalse(dbContext.Database.IsRelational(),
                           "The in-memory provider must remain non-relational so the migrator skips advisory locking");
            Assert.IsTrue(await dbContext.Database.CanConnectAsync()
                                                  .ConfigureAwait(false),
                          "The migrator must ensure the non-relational store is created");
        }
    }

    /// <summary>
    /// Verify the migrator applies the schema migrations on a relational provider
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DatabaseMigratorRelationalProviderAppliesMigrationsAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync()
                        .ConfigureAwait(false);

        using (connection)
        {
            var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseSqlite(connection)
                                                                                   .Options;
            var databaseOptions = new DatabaseOptions();
            var dbContext = new DockerUpdateGuardDbContext(options);

            await using (dbContext.ConfigureAwait(false))
            {
                await DatabaseMigrator.MigrateAsync(dbContext, databaseOptions, NullLogger.Instance, CancellationToken.None)
                                      .ConfigureAwait(false);

                var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync()
                                                                .ConfigureAwait(false);

                Assert.IsTrue(appliedMigrations.Any(),
                              "The migrator must apply the schema migrations on a relational provider");
            }
        }
    }

    /// <summary>
    /// Verify a migration failure is surfaced after the diagnostic log
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DatabaseMigratorMigrationFailureIsSurfacedAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync()
                        .ConfigureAwait(false);

        using (connection)
        {
            using (var seedCommand = connection.CreateCommand())
            {
                seedCommand.CommandText = "CREATE TABLE \"DockerInstances\" (\"Id\" integer);";

                await seedCommand.ExecuteNonQueryAsync()
                                 .ConfigureAwait(false);
            }

            var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseSqlite(connection)
                                                                                   .Options;
            var databaseOptions = new DatabaseOptions();
            var dbContext = new DockerUpdateGuardDbContext(options);
            Exception? caught = null;

            await using (dbContext.ConfigureAwait(false))
            {
                try
                {
                    await DatabaseMigrator.MigrateAsync(dbContext, databaseOptions, NullLogger.Instance, CancellationToken.None)
                                          .ConfigureAwait(false);
                }
                catch (DbException exception)
                {
                    caught = exception;
                }
            }

            Assert.IsNotNull(caught,
                             "A migration that conflicts with an existing schema object must surface the failure");
        }
    }

    /// <summary>
    /// Verify an unreachable database is retried and ultimately surfaces a connection failure
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DatabaseMigratorUnreachableDatabaseThrowsAfterTimeoutAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=dug;Username=u;Password=p;Timeout=1;Command Timeout=1")
                                                                               .Options;
        var databaseOptions = new DatabaseOptions
                              {
                                  MigrationStartupTimeoutSeconds = 1,
                                  MigrationRetryDelaySeconds = 0,
                              };
        var dbContext = new DockerUpdateGuardDbContext(options);
        Exception? caught = null;

        await using (dbContext.ConfigureAwait(false))
        {
            try
            {
                await DatabaseMigrator.MigrateAsync(dbContext, databaseOptions, NullLogger.Instance, CancellationToken.None)
                                      .ConfigureAwait(false);
            }
            catch (DbException exception)
            {
                caught = exception;
            }
        }

        Assert.IsNotNull(caught,
                         "An unreachable database must surface a connection failure once the startup timeout elapses");
    }

    /// <summary>
    /// Verify transient connectivity failures are classified as retryable
    /// </summary>
    [TestMethod]
    public void DatabaseMigratorIsTransientConnectionFailureClassifiesTransientFaults()
    {
        Assert.IsTrue(DatabaseMigrator.IsTransientConnectionFailure(new SocketException()),
                      "Socket failures must be treated as transient");
        Assert.IsTrue(DatabaseMigrator.IsTransientConnectionFailure(new TimeoutException()),
                      "Timeouts must be treated as transient");
        Assert.IsTrue(DatabaseMigrator.IsTransientConnectionFailure(new TransientDbException()),
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
        Assert.IsFalse(DatabaseMigrator.IsTransientConnectionFailure(new PermanentDbException()),
                       "Database exceptions not flagged as transient must fail fast");
        Assert.IsFalse(DatabaseMigrator.IsTransientConnectionFailure(new InvalidOperationException("permanent")),
                       "Unrelated failures must fail fast");
    }

    #endregion // Methods

    #region Nested types

    /// <summary>
    /// Database exception test double that reports a transient failure
    /// </summary>
    private sealed class TransientDbException : DbException
    {
        #region Properties

        /// <inheritdoc/>
        public override bool IsTransient => true;

        #endregion // Properties
    }

    /// <summary>
    /// Database exception test double that reports a permanent failure
    /// </summary>
    private sealed class PermanentDbException : DbException
    {
        #region Properties

        /// <inheritdoc/>
        public override bool IsTransient => false;

        #endregion // Properties
    }

    #endregion // Nested types
}
