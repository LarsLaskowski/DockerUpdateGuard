using DockerUpdateGuard.Data;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Tests.Data;

/// <summary>
/// SQLite-backed test database helper
/// </summary>
internal sealed class SqliteTestDatabase : IDisposable
{
    #region Fields

    private readonly SqliteConnection _connection;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    #endregion // Constructors

    #region Methods

    /// <summary>
    /// Create a database context
    /// </summary>
    /// <returns>Configured database context</returns>
    public DockerUpdateGuardDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseSqlite(_connection)
                                                                               .EnableSensitiveDataLogging()
                                                                               .Options;
        var dbContext = new DockerUpdateGuardDbContext(options);

        dbContext.Database.EnsureCreated();

        return dbContext;
    }

    /// <summary>
    /// Release resources
    /// </summary>
    public void Dispose()
    {
        _connection.Dispose();
    }

    #endregion // Methods
}