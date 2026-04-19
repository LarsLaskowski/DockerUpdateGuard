using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Refreshes observed image base scans on a schedule
/// </summary>
public class OwnImageBaseRefreshBackgroundService : ScheduledBackgroundService
{
    #region Fields

    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _optionsMonitor;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    public OwnImageBaseRefreshBackgroundService(ILogger<OwnImageBaseRefreshBackgroundService> logger,
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
        return TimeSpan.FromMinutes(_optionsMonitor.CurrentValue.Scanning.OwnImageBaseScanIntervalMinutes);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken stoppingToken)
    {
        var scope = _serviceScopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var orchestrator = scope.ServiceProvider.GetRequiredService<IImageScanOrchestrator>();

            await orchestrator.ScanAllAsync(ScanTriggerSource.Scheduled, stoppingToken)
                              .ConfigureAwait(false);
        }
    }

    #endregion // Methods
}