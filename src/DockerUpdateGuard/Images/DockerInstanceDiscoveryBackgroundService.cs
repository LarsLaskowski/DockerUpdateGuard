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
    /// <param name="timeProvider">Time provider</param>
    public DockerInstanceDiscoveryBackgroundService(ILogger<DockerInstanceDiscoveryBackgroundService> logger,
                                                    IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor,
                                                    IServiceScopeFactory serviceScopeFactory,
                                                    TimeProvider timeProvider)
        : base(logger,
               timeProvider,
               GetConfiguredInterval(optionsMonitor.CurrentValue))
    {
        _optionsMonitor = optionsMonitor;
        _serviceScopeFactory = serviceScopeFactory;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Resolve the execution interval of the supplied options
    /// </summary>
    /// <param name="options">Application options</param>
    /// <returns>Execution interval</returns>
    private static TimeSpan GetConfiguredInterval(DockerUpdateGuardOptions options)
    {
        return TimeSpan.FromMinutes(options.Scanning.DiscoveryIntervalMinutes);
    }

    #endregion // Static methods

    #region ScheduledBackgroundService

    /// <inheritdoc/>
    protected override TimeSpan GetInterval()
    {
        return GetConfiguredInterval(_optionsMonitor.CurrentValue);
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

    #endregion // ScheduledBackgroundService
}