using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.Images.Interfaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Collects time-based Docker-instance and container resource statistics
/// </summary>
public class ResourceStatisticsCollector : IResourceStatisticsCollector
{
    #region Fields

    /// <summary>
    /// Database context
    /// </summary>
    private readonly DockerUpdateGuardDbContext _dbContext;

    /// <summary>
    /// Docker-instance client
    /// </summary>
    private readonly IDockerInstanceClient _dockerInstanceClient;

    /// <summary>
    /// Instance-discovery service
    /// </summary>
    private readonly IInstanceDiscoveryService _instanceDiscoveryService;

    /// <summary>
    /// Logger
    /// </summary>
    private readonly ILogger<ResourceStatisticsCollector> _logger;

    /// <summary>
    /// Options monitor
    /// </summary>
    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _optionsMonitor;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="dockerInstanceClient">Docker instance client</param>
    /// <param name="instanceDiscoveryService">Instance discovery service</param>
    /// <param name="logger">Logger</param>
    /// <param name="optionsMonitor">Options monitor</param>
    public ResourceStatisticsCollector(DockerUpdateGuardDbContext dbContext,
                                       IDockerInstanceClient dockerInstanceClient,
                                       IInstanceDiscoveryService instanceDiscoveryService,
                                       ILogger<ResourceStatisticsCollector> logger,
                                       IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor)
    {
        _dbContext = dbContext;
        _dockerInstanceClient = dockerInstanceClient;
        _instanceDiscoveryService = instanceDiscoveryService;
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Calculate a network throughput value
    /// </summary>
    /// <param name="previousTotal">Previous total</param>
    /// <param name="currentTotal">Current total</param>
    /// <param name="previousRecordedAtUtc">Previous timestamp</param>
    /// <param name="currentRecordedAtUtc">Current timestamp</param>
    /// <returns>Bytes per second</returns>
    private static decimal CalculateBytesPerSecond(long? previousTotal,
                                                   long currentTotal,
                                                   DateTimeOffset? previousRecordedAtUtc,
                                                   DateTimeOffset currentRecordedAtUtc)
    {
        if (previousTotal is null || previousRecordedAtUtc is null)
        {
            return 0;
        }

        var elapsedSeconds = (decimal)(currentRecordedAtUtc - previousRecordedAtUtc.Value).TotalSeconds;

        if (elapsedSeconds <= 0)
        {
            return 0;
        }

        var delta = currentTotal - previousTotal.Value;

        if (delta <= 0)
        {
            return 0;
        }

        return Math.Round(delta / elapsedSeconds, 4);
    }

    #endregion // Static methods

    #region Methods

    /// <inheritdoc/>
    public async Task CollectAsync(CancellationToken cancellationToken = default)
    {
        await _instanceDiscoveryService.SynchronizeConfiguredInstancesAsync(cancellationToken)
                                       .ConfigureAwait(false);

        var optionsByName = _optionsMonitor.CurrentValue.DockerInstances.ToDictionary(entity => entity.Name, StringComparer.OrdinalIgnoreCase);
        var dockerInstances = await _dbContext.DockerInstances.Where(entity => entity.IsEnabled)
                                                              .ToListAsync(cancellationToken)
                                                              .ConfigureAwait(false);

        foreach (var dockerInstance in dockerInstances)
        {
            if (optionsByName.TryGetValue(dockerInstance.Name, out var configuredInstance) == false
                || configuredInstance.Enabled == false)
            {
                continue;
            }

            await CollectForInstanceAsync(dockerInstance, configuredInstance, cancellationToken).ConfigureAwait(false);
        }

        await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);
    }

    /// <summary>
    /// Collect resource statistics for a single Docker instance
    /// </summary>
    /// <param name="dockerInstance">Docker instance</param>
    /// <param name="configuredInstance">Configured options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task CollectForInstanceAsync(DockerInstance dockerInstance,
                                               DockerInstanceOptions configuredInstance,
                                               CancellationToken cancellationToken)
    {
        var result = await _dockerInstanceClient.CollectContainerResourceUsageAsync(configuredInstance, cancellationToken)
                                                .ConfigureAwait(false);
        var hostMemoryTotalResult = await _dockerInstanceClient.GetHostMemoryTotalAsync(configuredInstance, cancellationToken)
                                                               .ConfigureAwait(false);

        if (result.Status != Infrastructure.ExternalOperationStatus.Succeeded || result.Data is null)
        {
            _logger.LogWarning("Resource statistics collection failed for Docker instance {DockerInstanceName}: {Message}",
                               dockerInstance.Name,
                               result.Message);

            return;
        }

        var resourceSamples = result.Data;
        var hostMemoryTotalBytes = hostMemoryTotalResult.Status == Infrastructure.ExternalOperationStatus.Succeeded && hostMemoryTotalResult.Data > 0
                                       ? hostMemoryTotalResult.Data
                                       : 0;
        var previousContainerSamples = await _dbContext.RuntimeContainerResourceSamples
                                                       .Where(entity => entity.DockerInstanceId == dockerInstance.Id)
                                                       .OrderByDescending(entity => entity.RecordedAtUtc)
                                                       .AsNoTracking()
                                                       .ToListAsync(cancellationToken)
                                                       .ConfigureAwait(false);
        var latestContainerSamples = previousContainerSamples.GroupBy(entity => entity.ContainerId, StringComparer.OrdinalIgnoreCase)
                                                             .ToDictionary(group => group.Key,
                                                                           group => group.First(),
                                                                           StringComparer.OrdinalIgnoreCase);
        var currentSamples = new List<RuntimeContainerResourceSample>();

        foreach (var resourceSample in resourceSamples)
        {
            latestContainerSamples.TryGetValue(resourceSample.ContainerId, out var previousSample);

            var currentSample = new RuntimeContainerResourceSample
                                {
                                    DockerInstanceId = dockerInstance.Id,
                                    ContainerId = resourceSample.ContainerId,
                                    ContainerName = resourceSample.ContainerName,
                                    CpuPercent = resourceSample.CpuPercent,
                                    MemoryUsageBytes = resourceSample.MemoryUsageBytes,
                                    MemoryLimitBytes = resourceSample.MemoryLimitBytes,
                                    NetworkRxBytesTotal = resourceSample.NetworkRxBytesTotal,
                                    NetworkTxBytesTotal = resourceSample.NetworkTxBytesTotal,
                                    NetworkRxBytesPerSecond = CalculateBytesPerSecond(previousSample?.NetworkRxBytesTotal,
                                                                                      resourceSample.NetworkRxBytesTotal,
                                                                                      previousSample?.RecordedAtUtc,
                                                                                      resourceSample.RecordedAtUtc),
                                    NetworkTxBytesPerSecond = CalculateBytesPerSecond(previousSample?.NetworkTxBytesTotal,
                                                                                      resourceSample.NetworkTxBytesTotal,
                                                                                      previousSample?.RecordedAtUtc,
                                                                                      resourceSample.RecordedAtUtc),
                                    RecordedAtUtc = resourceSample.RecordedAtUtc,
                                };

            currentSamples.Add(currentSample);
        }

        _dbContext.RuntimeContainerResourceSamples.AddRange(currentSamples);

        var latestInstanceSample = await _dbContext.DockerInstanceResourceSamples
                                                   .Where(entity => entity.DockerInstanceId == dockerInstance.Id)
                                                   .OrderByDescending(entity => entity.RecordedAtUtc)
                                                   .AsNoTracking()
                                                   .FirstOrDefaultAsync(cancellationToken)
                                                   .ConfigureAwait(false);
        var recordedAtUtc = currentSamples.Count == 0
                                ? DateTimeOffset.UtcNow
                                : currentSamples.Max(entity => entity.RecordedAtUtc);
        var instanceRxTotal = currentSamples.Sum(entity => entity.NetworkRxBytesTotal);
        var instanceTxTotal = currentSamples.Sum(entity => entity.NetworkTxBytesTotal);

        _dbContext.DockerInstanceResourceSamples.Add(new DockerInstanceResourceSample
                                                     {
                                                         DockerInstanceId = dockerInstance.Id,
                                                         ContainerCount = currentSamples.Count,
                                                         CpuPercent = currentSamples.Sum(entity => entity.CpuPercent),
                                                         MemoryUsageBytes = currentSamples.Sum(entity => entity.MemoryUsageBytes),
                                                         MemoryLimitBytes = hostMemoryTotalBytes,
                                                         NetworkRxBytesTotal = instanceRxTotal,
                                                         NetworkTxBytesTotal = instanceTxTotal,
                                                         NetworkRxBytesPerSecond = CalculateBytesPerSecond(latestInstanceSample?.NetworkRxBytesTotal,
                                                                                                           instanceRxTotal,
                                                                                                           latestInstanceSample?.RecordedAtUtc,
                                                                                                           recordedAtUtc),
                                                         NetworkTxBytesPerSecond = CalculateBytesPerSecond(latestInstanceSample?.NetworkTxBytesTotal,
                                                                                                           instanceTxTotal,
                                                                                                           latestInstanceSample?.RecordedAtUtc,
                                                                                                           recordedAtUtc),
                                                         RecordedAtUtc = recordedAtUtc,
                                                     });
    }

    #endregion // Methods
}