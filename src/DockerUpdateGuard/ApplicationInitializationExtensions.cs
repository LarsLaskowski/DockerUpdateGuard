using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Images.Interfaces;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard;

/// <summary>
/// Application startup helpers
/// </summary>
public static class ApplicationInitializationExtensions
{
    #region Methods

    /// <summary>
    /// Initialize the application database and configuration backed state
    /// </summary>
    /// <param name="application">Web application</param>
    /// <returns>Task</returns>
    public static async Task InitializeDockerUpdateGuardAsync(this WebApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        var scope = application.Services.CreateAsyncScope();
        var loggerFactory = application.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(typeof(ApplicationInitializationExtensions).FullName ?? nameof(ApplicationInitializationExtensions));

        logger.ApplicationInitializationStarted();

        await using (scope.ConfigureAwait(false))
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuard.Data.DockerUpdateGuardDbContext>();
            var dockerHubAccountDiscoveryService = scope.ServiceProvider.GetRequiredService<IDockerHubAccountImageDiscoveryService>();
            var instanceDiscoveryService = scope.ServiceProvider.GetRequiredService<IInstanceDiscoveryService>();
            var applicationTelemetry = scope.ServiceProvider.GetRequiredService<ApplicationTelemetry>();
            var applicationOptions = scope.ServiceProvider.GetRequiredService<IOptions<DockerUpdateGuardOptions>>().Value;
            var applicationLifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();

            await DatabaseMigrator.MigrateAsync(dbContext, applicationOptions.Database, logger, applicationLifetime.ApplicationStopping)
                                  .ConfigureAwait(false);
            logger.ApplicationDatabaseMigrated();
            await instanceDiscoveryService.SynchronizeConfiguredInstancesAsync(CancellationToken.None)
                                          .ConfigureAwait(false);
            await dockerHubAccountDiscoveryService.SynchronizeAccountImagesAsync(CancellationToken.None)
                                                  .ConfigureAwait(false);
            await applicationTelemetry.RefreshInventoryMetricsAsync(dbContext, CancellationToken.None)
                                      .ConfigureAwait(false);
        }

        logger.ApplicationInitializationCompleted();
    }

    #endregion // Methods
}