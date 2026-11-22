using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DockerUpdateGuard.Data.Tests.Data;

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
        return CreateDbContext([]);
    }

    /// <summary>
    /// Create a database context with interceptors
    /// </summary>
    /// <param name="interceptors">Interceptors to register</param>
    /// <returns>Configured database context</returns>
    public DockerUpdateGuardDbContext CreateDbContext(IEnumerable<IInterceptor> interceptors)
    {
        var options = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseSqlite(_connection)
                                                                               .AddInterceptors(interceptors.ToArray())
                                                                               .EnableSensitiveDataLogging()
                                                                               .Options;
        var dbContext = new DockerUpdateGuardDbContext(options);

        dbContext.Database.EnsureCreated();

        return dbContext;
    }

    #endregion // Methods

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        _connection.Dispose();
    }

    #endregion // IDisposable
}