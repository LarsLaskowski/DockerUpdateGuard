using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Cleans historical scan data on a schedule
/// </summary>
public class ScanCleanupBackgroundService : ScheduledBackgroundService
{
    #region Fields

    private readonly ILogger<ScanCleanupBackgroundService> _logger;
    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _optionsMonitor;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    public ScanCleanupBackgroundService(ILogger<ScanCleanupBackgroundService> logger,
                                        IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor,
                                        IServiceScopeFactory serviceScopeFactory)
        : base(logger)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _serviceScopeFactory = serviceScopeFactory;
    }

    #endregion // Constructors

    #region Methods

    /// <inheritdoc/>
    protected override TimeSpan GetInterval()
    {
        return TimeSpan.FromMinutes(_optionsMonitor.CurrentValue.Scanning.CleanupIntervalMinutes);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken stoppingToken)
    {
        var scope = _serviceScopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
            var applicationTelemetry = scope.ServiceProvider.GetRequiredService<ApplicationTelemetry>();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-_optionsMonitor.CurrentValue.Scanning.RetainScanRunsDays);
            var oldTagCandidates = await dbContext.TagCandidates
                                                  .Where(entity => entity.UpdateFinding.ResolvedAtUtc != null
                                                                   && entity.UpdateFinding.ResolvedAtUtc < cutoff)
                                                  .ToListAsync(stoppingToken)
                                                  .ConfigureAwait(false);
            var oldUpdateFindings = await dbContext.UpdateFindings
                                                   .Where(entity => entity.IsActive == false
                                                                    && entity.ResolvedAtUtc != null
                                                                    && entity.ResolvedAtUtc < cutoff)
                                                   .ToListAsync(stoppingToken)
                                                   .ConfigureAwait(false);
            var oldVulnerabilityFindings = await dbContext.VulnerabilityFindings
                                                          .Where(entity => entity.IsActive == false
                                                                           && entity.ResolvedAtUtc != null
                                                                           && entity.ResolvedAtUtc < cutoff)
                                                          .ToListAsync(stoppingToken)
                                                          .ConfigureAwait(false);
            var oldSnapshots = await dbContext.ContainerSnapshots
                                              .Where(entity => entity.RecordedAtUtc < cutoff
                                                               && entity.UpdateFindings.Any(finding => finding.IsActive) == false
                                                               && entity.ContainerActionRuns.Any() == false)
                                              .ToListAsync(stoppingToken)
                                              .ConfigureAwait(false);
            var oldScanRuns = await dbContext.ScanRuns
                                             .Where(entity => entity.CompletedAtUtc != null
                                                              && entity.CompletedAtUtc < cutoff
                                                              && entity.UpdateFindings.Any() == false
                                                              && entity.VulnerabilityFindings.Any() == false
                                                              && entity.ContainerSnapshots.Any() == false
                                                              && entity.ImageRelationships.Any() == false)
                                             .ToListAsync(stoppingToken)
                                             .ConfigureAwait(false);

            dbContext.TagCandidates.RemoveRange(oldTagCandidates);
            dbContext.UpdateFindings.RemoveRange(oldUpdateFindings);
            dbContext.VulnerabilityFindings.RemoveRange(oldVulnerabilityFindings);
            dbContext.ContainerSnapshots.RemoveRange(oldSnapshots);
            dbContext.ScanRuns.RemoveRange(oldScanRuns);
            await dbContext.SaveChangesAsync(stoppingToken)
                           .ConfigureAwait(false);
            await applicationTelemetry.RefreshInventoryMetricsAsync(dbContext, stoppingToken)
                                      .ConfigureAwait(false);
            _logger.ScanCleanupCompleted(_optionsMonitor.CurrentValue.Scanning.RetainScanRunsDays,
                                         oldTagCandidates.Count,
                                         oldUpdateFindings.Count,
                                         oldVulnerabilityFindings.Count,
                                         oldSnapshots.Count,
                                         oldScanRuns.Count);
        }
    }

    #endregion // Methods
}