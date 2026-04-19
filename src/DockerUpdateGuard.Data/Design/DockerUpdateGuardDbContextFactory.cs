using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DockerUpdateGuard.Data.Design;

/// <summary>
/// Design-time factory for migrations
/// </summary>
public class DockerUpdateGuardDbContextFactory : IDesignTimeDbContextFactory<DockerUpdateGuardDbContext>
{
    #region Methods

    /// <inheritdoc/>
    public DockerUpdateGuardDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DockerUpdateGuardDbContext>();

        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=dockerupdateguard;Username=postgres;Password=postgres");

        return new DockerUpdateGuardDbContext(optionsBuilder.Options);
    }

    #endregion // Methods
}