using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Refreshes observed image base scans on a schedule
/// </summary>
public class OwnImageBaseRefreshBackgroundService : ScheduledBackgroundService
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
        using (var scope = _serviceScopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
            var observedImageCount = dbContext.ObservedImages.Count(entity => entity.IsEnabled);

            return ObservedImageScanIntervalCalculator.CalculateInterval(_optionsMonitor.CurrentValue.Scanning, observedImageCount);
        }
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