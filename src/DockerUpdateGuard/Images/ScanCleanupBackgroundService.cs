using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Cleans historical scan data on a schedule
/// </summary>
public class ScanCleanupBackgroundService : ScheduledBackgroundService
{
    #region Constants

    /// <summary>
    /// Maximum number of scan runs to retain for history views
    /// </summary>
    private const int MaxRetainedScanRuns = 20;

    /// <summary>
    /// Shortest age a running scan run must reach before it is treated as abandoned
    /// </summary>
    private const int MinimumStaleRunThresholdHours = 24;

    /// <summary>
    /// Message stored on running scan runs that were repaired by the cleanup
    /// </summary>
    private const string StaleRunErrorMessage = "Marked as failed by cleanup: the run never completed (process restart or crash)";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Logger
    /// </summary>
    private readonly ILogger<ScanCleanupBackgroundService> _logger;

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

    #region Static methods

    /// <summary>
    /// Resolve the age a running scan run of the supplied type must reach before it is treated as abandoned
    /// </summary>
    /// <param name="scanRunType">Scan run type</param>
    /// <param name="scanningOptions">Scan scheduling options</param>
    /// <returns>Stale threshold</returns>
    private static TimeSpan GetStaleRunThreshold(ScanRunType scanRunType, ScanningOptions scanningOptions)
    {
        var intervalMinutes = scanRunType switch
                              {
                                  ScanRunType.ObservedImage => scanningOptions.OwnImageBaseScanIntervalMinutes,
                                  ScanRunType.RuntimeContainer => scanningOptions.RuntimeImageUpdateScanIntervalMinutes,
                                  ScanRunType.Vulnerability => scanningOptions.VulnerabilityRefreshIntervalMinutes,
                                  _ => 0,
                              };
        var intervalThreshold = TimeSpan.FromMinutes(2d * intervalMinutes);
        var minimumThreshold = TimeSpan.FromHours(MinimumStaleRunThresholdHours);

        return intervalThreshold < minimumThreshold ? minimumThreshold : intervalThreshold;
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Mark running scan runs that never completed because of a process restart or crash as failed
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="utcNow">Current point in time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task RepairStaleRunningScanRunsAsync(DockerUpdateGuardDbContext dbContext,
                                                       DateTimeOffset utcNow,
                                                       CancellationToken cancellationToken)
    {
        var runningScanRuns = await dbContext.ScanRuns.Where(entity => entity.Status == ScanRunStatus.Running)
                                                      .ToListAsync(cancellationToken)
                                                      .ConfigureAwait(false);

        var scanningOptions = _optionsMonitor.CurrentValue.Scanning;

        var staleScanRuns = runningScanRuns.Where(entity => entity.StartedAtUtc < utcNow - GetStaleRunThreshold(entity.Type, scanningOptions))
                                           .ToList();

        if (staleScanRuns.Count == 0)
        {
            return;
        }

        foreach (var staleScanRun in staleScanRuns)
        {
            staleScanRun.Status = ScanRunStatus.Failed;
            staleScanRun.CompletedAtUtc = utcNow;
            staleScanRun.ErrorMessage = StaleRunErrorMessage;
        }

        await dbContext.SaveChangesAsync(cancellationToken)
                       .ConfigureAwait(false);

        _logger.StaleScanRunsRepaired(staleScanRuns.Count);
    }

    #endregion // Methods

    #region ScheduledBackgroundService

    /// <inheritdoc/>
    protected override TimeSpan GetInterval()
    {
        return TimeSpan.FromMinutes(_optionsMonitor.CurrentValue.Scanning.CleanupIntervalMinutes);
    }

    /// <inheritdoc/>
    protected override bool ShouldExecuteImmediately()
    {
        return false;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteStartupAsync(CancellationToken stoppingToken)
    {
        var scope = _serviceScopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();

            await RepairStaleRunningScanRunsAsync(dbContext,
                                                  DateTimeOffset.UtcNow,
                                                  stoppingToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken stoppingToken)
    {
        var scope = _serviceScopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
            var applicationTelemetry = scope.ServiceProvider.GetRequiredService<ApplicationTelemetry>();
            var cleanupStartedAtUtc = DateTimeOffset.UtcNow;

            await RepairStaleRunningScanRunsAsync(dbContext,
                                                  cleanupStartedAtUtc,
                                                  stoppingToken).ConfigureAwait(false);

            var cutoff = cleanupStartedAtUtc.AddDays(-_optionsMonitor.CurrentValue.Scanning.RetainScanRunsDays);

            var completedUnreferencedScanRuns = dbContext.ScanRuns
                                                         .Where(entity => entity.CompletedAtUtc != null
                                                                          && entity.CompletedAtUtc <= cleanupStartedAtUtc
                                                                          && entity.UpdateFindings.Any() == false
                                                                          && entity.VulnerabilityFindings.Any() == false
                                                                          && entity.ContainerSnapshots.Any() == false
                                                                          && entity.ImageRelationships.Any() == false);

            var retainedScanRunIds = await completedUnreferencedScanRuns.OrderByDescending(entity => entity.CompletedAtUtc)
                                                                        .Take(MaxRetainedScanRuns)
                                                                        .Select(entity => entity.Id)
                                                                        .ToListAsync(stoppingToken)
                                                                        .ConfigureAwait(false);

            var oldTagCandidates = await dbContext.TagCandidates
                                                  .Where(entity => entity.UpdateFinding.ResolvedAtUtc != null
                                                                   && entity.UpdateFinding.ResolvedAtUtc < cutoff)
                                                  .ToListAsync(stoppingToken)
                                                  .ConfigureAwait(false);

            var invalidTagCandidates = await dbContext.TagCandidates
                                                      .Where(entity => entity.Digest == null || entity.Digest == string.Empty)
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

            var oldScanRuns = await completedUnreferencedScanRuns.Where(entity => entity.CompletedAtUtc < cutoff)
                                                                 .ToListAsync(stoppingToken)
                                                                 .ConfigureAwait(false);

            var excessScanRuns = await completedUnreferencedScanRuns.Where(entity => retainedScanRunIds.Contains(entity.Id) == false)
                                                                    .ToListAsync(stoppingToken)
                                                                    .ConfigureAwait(false);

            var oldRuntimeContainerSamples = await dbContext.RuntimeContainerResourceSamples
                                                            .Where(entity => entity.RecordedAtUtc < cutoff)
                                                            .ToListAsync(stoppingToken)
                                                            .ConfigureAwait(false);

            var oldDockerInstanceSamples = await dbContext.DockerInstanceResourceSamples
                                                          .Where(entity => entity.RecordedAtUtc < cutoff)
                                                          .ToListAsync(stoppingToken)
                                                          .ConfigureAwait(false);

            var removableTagCandidates = oldTagCandidates.Concat(invalidTagCandidates)
                                                         .DistinctBy(entity => entity.Id)
                                                         .ToList();

            var removableScanRuns = oldScanRuns.Concat(excessScanRuns)
                                               .DistinctBy(entity => entity.Id)
                                               .ToList();

            dbContext.TagCandidates.RemoveRange(removableTagCandidates);
            dbContext.UpdateFindings.RemoveRange(oldUpdateFindings);
            dbContext.VulnerabilityFindings.RemoveRange(oldVulnerabilityFindings);
            dbContext.ContainerSnapshots.RemoveRange(oldSnapshots);
            dbContext.ScanRuns.RemoveRange(removableScanRuns);
            dbContext.RuntimeContainerResourceSamples.RemoveRange(oldRuntimeContainerSamples);
            dbContext.DockerInstanceResourceSamples.RemoveRange(oldDockerInstanceSamples);

            await dbContext.SaveChangesAsync(stoppingToken)
                           .ConfigureAwait(false);

            await applicationTelemetry.RefreshInventoryMetricsAsync(dbContext, stoppingToken)
                                      .ConfigureAwait(false);

            _logger.ScanCleanupCompleted(_optionsMonitor.CurrentValue.Scanning.RetainScanRunsDays,
                                         removableTagCandidates.Count,
                                         oldUpdateFindings.Count,
                                         oldVulnerabilityFindings.Count,
                                         oldSnapshots.Count,
                                         removableScanRuns.Count);
        }
    }

    #endregion // ScheduledBackgroundService
}