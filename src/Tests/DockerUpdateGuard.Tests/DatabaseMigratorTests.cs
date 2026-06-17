using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;

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
    public async Task DatabaseMigratorNonRelationalProviderCompletesAsync()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                                                                               .Options;
        var databaseOptions = new DatabaseOptions();

        var dbContext = new DockerUpdateGuardDbContext(options);

        await using (dbContext.ConfigureAwait(false))
        {
            await DatabaseMigrator.MigrateAsync(dbContext, databaseOptions, NullLogger.Instance, CancellationToken.None)
                                  .ConfigureAwait(false);

            Assert.IsTrue(dbContext.Database.IsRelational() == false,
                          "The in-memory provider must remain non-relational so the migrator skips advisory locking");
            Assert.IsTrue(await dbContext.Database.CanConnectAsync()
                                                  .ConfigureAwait(false),
                          "The migrator must ensure the non-relational store is created");
        }
    }

    #endregion // Methods
}
