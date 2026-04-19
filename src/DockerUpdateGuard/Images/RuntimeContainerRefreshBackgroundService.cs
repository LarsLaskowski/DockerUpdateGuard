using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Refreshes runtime container scans on a schedule
/// </summary>
public class RuntimeContainerRefreshBackgroundService : ScheduledBackgroundService
{
    #region Fields

    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _optionsMonitor;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    public RuntimeContainerRefreshBackgroundService(ILogger<RuntimeContainerRefreshBackgroundService> logger,
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

    #endregion // Methods
}