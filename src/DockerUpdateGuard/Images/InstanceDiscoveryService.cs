using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Synchronizes configured Docker instances into persistence
/// </summary>
public class InstanceDiscoveryService : IInstanceDiscoveryService
{
    #region Fields

    private readonly DockerUpdateGuardDbContext _dbContext;
    private readonly ILogger<InstanceDiscoveryService> _logger;
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

    #region Methods

    /// <inheritdoc/>
    public async Task SynchronizeConfiguredInstancesAsync(CancellationToken cancellationToken = default)
    {
        var configuredInstances = _optionsMonitor.CurrentValue.DockerInstances;
        var configuredNames = configuredInstances
                              .Select(instance => instance.Name.Trim())
                              .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _logger.DockerInstanceSynchronizationStarted(configuredInstances.Count);
        var existingInstances = await _dbContext.DockerInstances
                                                .Include(entity => entity.PortainerEndpoint)
                                                .Where(entity => entity.Source == RegistrationSource.ConfigurationFile)
                                                .ToListAsync(cancellationToken)
                                                .ConfigureAwait(false);
        var disabledInstanceCount = existingInstances.Count(entity => configuredNames.Contains(entity.Name) == false);
        var enabledInstanceCount = 0;
        var portainerEndpointCount = 0;

        foreach (var existingInstance in existingInstances.Where(entity => configuredNames.Contains(entity.Name) == false))
        {
            existingInstance.IsEnabled = false;
            existingInstance.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        foreach (var configuredInstance in configuredInstances)
        {
            if (configuredInstance.Enabled)
            {
                enabledInstanceCount++;
            }
            else
            {
                disabledInstanceCount++;
            }

            var existingInstance = existingInstances.SingleOrDefault(entity => string.Equals(entity.Name,
                                                                                             configuredInstance.Name,
                                                                                             StringComparison.OrdinalIgnoreCase));

            if (existingInstance is null)
            {
                existingInstance = new DockerInstance
                                   {
                                       Name = configuredInstance.Name.Trim(),
                                       Source = RegistrationSource.ConfigurationFile,
                                   };

                _dbContext.DockerInstances.Add(existingInstance);
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

            if (configuredInstance.Portainer.Enabled)
            {
                portainerEndpointCount++;

                if (existingInstance.PortainerEndpoint is null)
                {
                    var portainerEndpoint = new PortainerEndpoint
                                            {
                                                DockerInstanceId = existingInstance.Id,
                                            };

                    existingInstance.PortainerEndpoint = portainerEndpoint;
                }

                existingInstance.PortainerEndpoint.Name = $"{existingInstance.Name} Portainer";
                existingInstance.PortainerEndpoint.BaseUrl = configuredInstance.Portainer.BaseUrl?.Trim() ?? string.Empty;
                existingInstance.PortainerEndpoint.ExternalEndpointId = configuredInstance.Portainer.EndpointId;
                existingInstance.PortainerEndpoint.IsEnabled = true;
                existingInstance.PortainerEndpoint.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            else if (existingInstance.PortainerEndpoint is not null)
            {
                existingInstance.PortainerEndpoint.IsEnabled = false;
                existingInstance.PortainerEndpoint.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);
        _logger.DockerInstanceSynchronizationCompleted(configuredInstances.Count,
                                                       enabledInstanceCount,
                                                       disabledInstanceCount,
                                                       portainerEndpointCount);
    }

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
            _ => DockerConnectionKind.NotSet,
        };
    }

    #endregion // Methods
}