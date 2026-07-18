using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DockerUpdateGuard.Data.Design;

/// <summary>
/// Design-time factory for migrations
/// </summary>
public class DockerUpdateGuardDbContextFactory : IDesignTimeDbContextFactory<DockerUpdateGuardDbContext>
{
    #region Constants

    /// <summary>
    /// Environment variable holding the design-time connection string
    /// </summary>
    private const string ConnectionStringVariable = "ConnectionStrings__DockerUpdateGuard";

    /// <summary>
    /// Non-secret local fallback connection string used when no environment variable is set
    /// </summary>
    private const string FallbackConnectionString = "Host=localhost;Port=5432;Database=dockerupdateguard;Username=postgres";

    #endregion // Constants

    #region IDesignTimeDbContextFactory

    /// <inheritdoc/>
    public DockerUpdateGuardDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = FallbackConnectionString;
        }

        var optionsBuilder = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>();

        optionsBuilder.UseNpgsql(connectionString);

        return new DockerUpdateGuardDbContext(optionsBuilder.Options);
    }

    #endregion // IDesignTimeDbContextFactory
}