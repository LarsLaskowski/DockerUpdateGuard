using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images.Interfaces;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Refreshes runtime container scans on a schedule
/// </summary>
public class RuntimeContainerRefreshBackgroundService : ScheduledBackgroundService
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
    public RuntimeContainerRefreshBackgroundService(ILogger<RuntimeContainerRefreshBackgroundService> logger,
                                                    IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor,
                                                    IServiceScopeFactory serviceScopeFactory)
        : base(logger)
    {
        _optionsMonitor = optionsMonitor;
        _serviceScopeFactory = serviceScopeFactory;
    }

    #endregion // Constructors

    #region ScheduledBackgroundService

    /// <inheritdoc/>
    protected override TimeSpan GetInterval()
    {
        return TimeSpan.FromMinutes(_optionsMonitor.CurrentValue.Scanning.RuntimeImageUpdateScanIntervalMinutes);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken stoppingToken)
    {
        var scope = _serviceScopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var orchestrator = scope.ServiceProvider.GetRequiredService<IRuntimeContainerScanOrchestrator>();

            await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, stoppingToken)
                              .ConfigureAwait(false);
        }
    }

    #endregion // ScheduledBackgroundService
}