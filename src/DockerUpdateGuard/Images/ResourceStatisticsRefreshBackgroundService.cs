using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Images.Interfaces;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Refreshes Docker-instance and runtime-container resource statistics on a schedule
/// </summary>
public class ResourceStatisticsRefreshBackgroundService : ScheduledBackgroundService
{
    #region Fields

    /// <summary>
    /// Options monitor
    /// </summary>
    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _optionsMonitor;

    /// <summary>
    /// Service-scope factory
    /// </summary>
    private readonly IServiceScopeFactory _serviceScopeFactory;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="optionsMonitor">Application options monitor</param>
    /// <param name="serviceScopeFactory">Service scope factory</param>
    public ResourceStatisticsRefreshBackgroundService(ILogger<ResourceStatisticsRefreshBackgroundService> logger,
                                                      IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor,
                                                      IServiceScopeFactory serviceScopeFactory)
        : base(logger)
    {
        _optionsMonitor = optionsMonitor;
        _serviceScopeFactory = serviceScopeFactory;
    }

    #endregion // Constructors

    #region Methods

    /// <inheritdoc/>
    protected override TimeSpan GetInterval()
    {
        return TimeSpan.FromMinutes(_optionsMonitor.CurrentValue.Scanning.ResourceStatisticsIntervalMinutes);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken stoppingToken)
    {
        var scope = _serviceScopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var collector = scope.ServiceProvider.GetRequiredService<IResourceStatisticsCollector>();

            await collector.CollectAsync(stoppingToken)
                           .ConfigureAwait(false);
        }
    }

    #endregion // Methods
}