using DockerUpdateGuard.Data.Queries;
using DockerUpdateGuard.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DockerUpdateGuard.Data;

/// <summary>
/// Service registration helpers for the data layer
/// </summary>
public static class ServiceCollectionExtensions
{
    #region Methods

    /// <summary>
    /// Add DockerUpdateGuard data services
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureDbContext">Database configuration callback</param>
    /// <returns>Returns the service collection</returns>
    public static IServiceCollection AddDockerUpdateGuardData(this IServiceCollection services, Action<DbContextOptionsBuilder> configureDbContext)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureDbContext);

        services.AddDbContext<DockerUpdateGuardDbContext>(configureDbContext);
        services.AddScoped<IImageCatalogRepository, ImageCatalogRepository>();
        services.AddScoped<ISharedBaseImageQueryService, SharedBaseImageQueryService>();

        return services;
    }

    #endregion // Methods
}