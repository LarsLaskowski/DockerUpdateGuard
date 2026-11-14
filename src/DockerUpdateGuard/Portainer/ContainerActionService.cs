using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Portainer.Data;
using DockerUpdateGuard.Portainer.Interfaces;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Portainer;

/// <summary>
/// Container action service
/// </summary>
public sealed class ContainerActionService : IContainerActionService
{
    #region Fields

#pragma warning disable S4487 // Skeleton: removed with the implementation
    /// <summary>
    /// Service scope factory
    /// </summary>
    private readonly IServiceScopeFactory _serviceScopeFactory;

    /// <summary>
    /// Portainer client
    /// </summary>
    private readonly IPortainerClient _portainerClient;

    /// <summary>
    /// Options monitor
    /// </summary>
    private readonly IOptionsMonitor<DockerUpdateGuardOptions> _optionsMonitor;

    /// <summary>
    /// Application lifetime
    /// </summary>
    private readonly IHostApplicationLifetime _applicationLifetime;

    /// <summary>
    /// Configuration
    /// </summary>
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Time provider
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Logger
    /// </summary>
    private readonly ILogger<ContainerActionService> _logger;
#pragma warning restore S4487 // Skeleton: removed with the implementation

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="serviceScopeFactory">Service scope factory</param>
    /// <param name="portainerClient">Portainer client</param>
    /// <param name="optionsMonitor">Options monitor</param>
    /// <param name="applicationLifetime">Application lifetime</param>
    /// <param name="configuration">Configuration</param>
    /// <param name="timeProvider">Time provider</param>
    /// <param name="logger">Logger</param>
    public ContainerActionService(IServiceScopeFactory serviceScopeFactory,
                                  IPortainerClient portainerClient,
                                  IOptionsMonitor<DockerUpdateGuardOptions> optionsMonitor,
                                  IHostApplicationLifetime applicationLifetime,
                                  IConfiguration configuration,
                                  TimeProvider timeProvider,
                                  ILogger<ContainerActionService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _portainerClient = portainerClient;
        _optionsMonitor = optionsMonitor;
        _applicationLifetime = applicationLifetime;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    #endregion // Constructors

    #region IContainerActionService

    /// <inheritdoc/>
    public Task<ContainerActionAvailabilityData> GetAvailabilityAsync(Guid dockerInstanceId, string containerId, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    /// <inheritdoc/>
    public Task<ContainerActionRunResultData> ExecuteAsync(Guid dockerInstanceId, string containerId, ContainerActionType actionType)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // IContainerActionService
}