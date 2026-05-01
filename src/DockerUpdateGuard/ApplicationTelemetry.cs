using System.Diagnostics.Metrics;

using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Telemetry;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DockerUpdateGuard;

/// <summary>
/// Host specific telemetry helpers
/// </summary>
public class ApplicationTelemetry
{
    #region Fields

    /// <summary>
    /// Logger
    /// </summary>
    private readonly ILogger<ApplicationTelemetry> _logger;

    /// <summary>
    /// Scan-runs counter
    /// </summary>
    private readonly Counter<long> _scanRunsCounter;

    /// <summary>
    /// Scan-failures counter
    /// </summary>
    private readonly Counter<long> _scanFailuresCounter;

    /// <summary>
    /// Scan-duration histogram
    /// </summary>
    private readonly Histogram<double> _scanDurationHistogram;

    /// <summary>
    /// Observed-image count
    /// </summary>
    private long _observedImages;

    /// <summary>
    /// Runtime-container count
    /// </summary>
    private long _runtimeContainers;

    /// <summary>
    /// Deduplicated base-image count
    /// </summary>
    private long _deduplicatedBaseImages;

    /// <summary>
    /// Active update-finding count
    /// </summary>
    private long _activeUpdateFindings;

    /// <summary>
    /// Active CVE-finding count
    /// </summary>
    private long _activeCveFindings;

    /// <summary>
    /// Needs-review finding count
    /// </summary>
    private long _needsReviewFindings;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    public ApplicationTelemetry()
        : this(NullLogger<ApplicationTelemetry>.Instance)
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="logger">Logger</param>
    public ApplicationTelemetry(ILogger<ApplicationTelemetry> logger)
    {
        _logger = logger;

        _scanRunsCounter = DockerUpdateGuardTelemetry.Meter.CreateCounter<long>(TelemetryMetricNames.ScanRuns);
        _scanFailuresCounter = DockerUpdateGuardTelemetry.Meter.CreateCounter<long>(TelemetryMetricNames.ScanFailures);
        _scanDurationHistogram = DockerUpdateGuardTelemetry.Meter.CreateHistogram<double>(TelemetryMetricNames.ScanDuration, unit: "s");

        DockerUpdateGuardTelemetry.Meter.CreateObservableGauge(TelemetryMetricNames.ObservedImages, () => new Measurement<long>(_observedImages));
        DockerUpdateGuardTelemetry.Meter.CreateObservableGauge(TelemetryMetricNames.RuntimeContainers, () => new Measurement<long>(_runtimeContainers));
        DockerUpdateGuardTelemetry.Meter.CreateObservableGauge(TelemetryMetricNames.DeduplicatedBaseImages, () => new Measurement<long>(_deduplicatedBaseImages));
        DockerUpdateGuardTelemetry.Meter.CreateObservableGauge(TelemetryMetricNames.ActiveUpdateFindings, () => new Measurement<long>(_activeUpdateFindings));
        DockerUpdateGuardTelemetry.Meter.CreateObservableGauge(TelemetryMetricNames.ActiveCveFindings, () => new Measurement<long>(_activeCveFindings));
        DockerUpdateGuardTelemetry.Meter.CreateObservableGauge(TelemetryMetricNames.NeedsReviewFindings, () => new Measurement<long>(_needsReviewFindings));
    }

    #endregion // Constructors

    #region Methods

    /// <summary>
    /// Record a finished scan run
    /// </summary>
    /// <param name="type">Scan type</param>
    /// <param name="status">Scan status</param>
    /// <param name="duration">Scan duration</param>
    public void RecordScanRun(ScanRunType type,
                              ScanRunStatus status,
                              TimeSpan duration)
    {
        KeyValuePair<string, object?>[] tags = [
                                                   new(TelemetryTagNames.ScanType, type.ToString()),
                                                   new(TelemetryTagNames.ResultStatus, status.ToString()),
                                               ];

        _scanRunsCounter.Add(1, tags);
        _scanDurationHistogram.Record(duration.TotalSeconds, tags);

        if (status == ScanRunStatus.Failed)
        {
            _scanFailuresCounter.Add(1, tags);
        }
    }

    /// <summary>
    /// Refresh observable inventory metrics from the database
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    public async Task RefreshInventoryMetricsAsync(DockerUpdateGuardDbContext dbContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _observedImages = await dbContext.ObservedImages
                                         .LongCountAsync(cancellationToken)
                                         .ConfigureAwait(false);
        _runtimeContainers = await dbContext.ContainerSnapshots
                                            .GroupBy(entity => new
                                                               {
                                                                   entity.DockerInstanceId,
                                                                   entity.ContainerId
                                                               })
                                            .LongCountAsync(cancellationToken)
                                            .ConfigureAwait(false);
        _deduplicatedBaseImages = await dbContext.ImageRelationships
                                                 .Where(entity => entity.RelationshipType == ImageRelationshipType.BaseImage)
                                                 .Select(entity => entity.BaseImageVersionId)
                                                 .Distinct()
                                                 .LongCountAsync(cancellationToken)
                                                 .ConfigureAwait(false);
        _activeUpdateFindings = await dbContext.UpdateFindings.Where(entity => entity.IsActive)
                                                              .LongCountAsync(cancellationToken)
                                                              .ConfigureAwait(false);
        _activeCveFindings = await dbContext.VulnerabilityFindings.Where(entity => entity.IsActive)
                                                                  .LongCountAsync(cancellationToken)
                                                                  .ConfigureAwait(false);
        _needsReviewFindings = await dbContext.UpdateFindings.Where(entity => entity.IsActive && entity.Type == UpdateFindingType.TagRecommendation)
                                                             .LongCountAsync(cancellationToken)
                                                             .ConfigureAwait(false);
        _logger.InventoryMetricsRefreshed(_observedImages,
                                          _runtimeContainers,
                                          _deduplicatedBaseImages,
                                          _activeUpdateFindings,
                                          _activeCveFindings,
                                          _needsReviewFindings);
    }

    #endregion // Methods
}