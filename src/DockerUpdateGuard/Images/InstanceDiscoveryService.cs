using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images.Interfaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Synchronizes configured Docker instances into persistence
/// </summary>
public class InstanceDiscoveryService : IInstanceDiscoveryService
{
    #region Fields

    /// <summary>
    /// Database context
    /// </summary>
    private readonly DockerUpdateGuardDbContext _dbContext;

    /// <summary>
    /// Logger
    /// </summary>
    private readonly ILogger<InstanceDiscoveryService> _logger;

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
    /// <param name="logger">Logger</param>
    /// <param name="optionsMonitor">Options monitor</param>
    public InstanceDiscoveryService(DockerUpdateGuardDbContext dbContext,
                                    ILogger<InstanceDiscoveryService> logger,
                                    IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor)
    {
        _dbContext = dbContext;
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Map configuration to the persisted connection kind enum
    /// </summary>
    /// <param name="configuredInstance">Configured instance</param>
    /// <returns>Connection kind</returns>
    private static DockerConnectionKind MapConnectionKind(DockerInstanceOptions configuredInstance)
    {
        if (Uri.TryCreate(configuredInstance.BaseUrl, UriKind.Absolute, out var uri) == false)
        {
            return DockerConnectionKind.NotSet;
        }

        return uri.Scheme switch
               {
                   "tcp" when configuredInstance.UseTls => DockerConnectionKind.Https,
                   "tcp" => DockerConnectionKind.Http,
                   "http" => DockerConnectionKind.Http,
                   "https" => DockerConnectionKind.Https,
                   "npipe" => DockerConnectionKind.NamedPipe,
                   "unix" => DockerConnectionKind.UnixSocket,
                   _ => DockerConnectionKind.NotSet,
               };
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Create or update the persisted Docker instance of a configured instance
    /// </summary>
    /// <param name="configuredInstance">Configured Docker instance options</param>
    /// <param name="existingInstances">Persisted Docker instances, extended when a new instance is created</param>
    private void SynchronizeConfiguredInstance(DockerInstanceOptions configuredInstance, List<DockerInstance> existingInstances)
    {
        var configuredInstanceName = configuredInstance.Name.Trim();
        var existingInstance = existingInstances.SingleOrDefault(entity => string.Equals(entity.Name,
                                                                                         configuredInstanceName,
                                                                                         StringComparison.OrdinalIgnoreCase));

        if (existingInstance is null)
        {
            existingInstance = new DockerInstance
                               {
                                   Name = configuredInstanceName,
                                   Source = RegistrationSource.ConfigurationFile,
                               };

            _dbContext.DockerInstances.Add(existingInstance);

            existingInstances.Add(existingInstance);
        }

        existingInstance.EndpointUri = configuredInstance.BaseUrl.Trim();
        existingInstance.ConnectionKind = MapConnectionKind(configuredInstance);
        existingInstance.IsEnabled = configuredInstance.Enabled;
        existingInstance.SkipCertificateValidation = configuredInstance.SkipCertificateValidation;
        existingInstance.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (configuredInstance.Enabled
            && existingInstance.ConnectionKind == DockerConnectionKind.NotSet)
        {
            _logger.DockerInstanceConfigurationUnsupported(configuredInstance.Name, configuredInstance.BaseUrl);
        }

        SynchronizePortainerEndpoint(existingInstance, configuredInstance);
    }

    /// <summary>
    /// Create, update or disable the Portainer endpoint of a persisted Docker instance
    /// </summary>
    /// <param name="existingInstance">Persisted Docker instance</param>
    /// <param name="configuredInstance">Configured Docker instance options</param>
    private void SynchronizePortainerEndpoint(DockerInstance existingInstance, DockerInstanceOptions configuredInstance)
    {
        if (configuredInstance.Portainer.Enabled == false)
        {
            if (existingInstance.PortainerEndpoint is not null)
            {
                existingInstance.PortainerEndpoint.IsEnabled = false;
                existingInstance.PortainerEndpoint.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            return;
        }

        if (existingInstance.PortainerEndpoint is null)
        {
            var portainerEndpoint = new PortainerEndpoint
                                    {
                                        DockerInstance = existingInstance,
                                    };

            _dbContext.PortainerEndpoints.Add(portainerEndpoint);

            existingInstance.PortainerEndpoint = portainerEndpoint;
        }

        existingInstance.PortainerEndpoint.Name = $"{existingInstance.Name} Portainer";
        existingInstance.PortainerEndpoint.BaseUrl = configuredInstance.Portainer.BaseUrl?.Trim() ?? string.Empty;
        existingInstance.PortainerEndpoint.ExternalEndpointId = configuredInstance.Portainer.EndpointId;
        existingInstance.PortainerEndpoint.IsEnabled = true;
        existingInstance.PortainerEndpoint.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    #endregion // Methods

    #region IInstanceDiscoveryService

    /// <inheritdoc/>
    public async Task SynchronizeConfiguredInstancesAsync(CancellationToken cancellationToken = default)
    {
        var configuredInstances = _optionsMonitor.CurrentValue.DockerInstances;
        var configuredNames = configuredInstances.Select(instance => instance.Name.Trim())
                                                 .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _logger.DockerInstanceSynchronizationStarted(configuredInstances.Count);

        var existingInstances = await _dbContext.DockerInstances
                                                .Include(entity => entity.PortainerEndpoint)
                                                .ToListAsync(cancellationToken)
                                                .ConfigureAwait(false);
        var obsoleteInstances = existingInstances.Where(entity => configuredNames.Contains(entity.Name) == false)
                                                 .ToList();
        var obsoleteInstanceIds = obsoleteInstances.Select(entity => entity.Id)
                                                   .ToList();
        IReadOnlyList<ScanRun> obsoleteScanRuns = obsoleteInstanceIds.Count == 0
                                                      ? []
                                                      : await _dbContext.ScanRuns.Where(entity => entity.DockerInstanceId != null
                                                                                                  && obsoleteInstanceIds.Contains(entity.DockerInstanceId.Value))
                                                                                 .ToListAsync(cancellationToken)
                                                                                 .ConfigureAwait(false);

        var enabledInstanceCount = configuredInstances.Count(instance => instance.Enabled);
        var disabledInstanceCount = configuredInstances.Count - enabledInstanceCount;
        var portainerEndpointCount = configuredInstances.Count(instance => instance.Portainer.Enabled);

        if (obsoleteScanRuns.Count > 0)
        {
            _dbContext.ScanRuns.RemoveRange(obsoleteScanRuns);
        }

        if (obsoleteInstances.Count > 0)
        {
            _dbContext.DockerInstances.RemoveRange(obsoleteInstances);

            existingInstances.RemoveAll(entity => obsoleteInstanceIds.Contains(entity.Id));
        }

        foreach (var configuredInstance in configuredInstances)
        {
            SynchronizeConfiguredInstance(configuredInstance, existingInstances);
        }

        await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);

        _logger.DockerInstanceSynchronizationCompleted(configuredInstances.Count,
                                                       enabledInstanceCount,
                                                       disabledInstanceCount,
                                                       portainerEndpointCount);
    }

    #endregion // IInstanceDiscoveryService
}