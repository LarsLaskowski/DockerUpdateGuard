using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Images.Interfaces;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Synchronizes configured Docker instances on a schedule
/// </summary>
public class DockerInstanceDiscoveryBackgroundService : ScheduledBackgroundService
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
    public DockerInstanceDiscoveryBackgroundService(ILogger<DockerInstanceDiscoveryBackgroundService> logger,
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
        return TimeSpan.FromMinutes(_optionsMonitor.CurrentValue.Scanning.DiscoveryIntervalMinutes);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken stoppingToken)
    {
        var scope = _serviceScopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var discoveryService = scope.ServiceProvider.GetRequiredService<IInstanceDiscoveryService>();

            await discoveryService.SynchronizeConfiguredInstancesAsync(stoppingToken)
                                  .ConfigureAwait(false);
        }
    }

    #endregion // Methods
}