using DockerUpdateGuard.Configuration;

namespace DockerUpdateGuard.Portainer;

/// <summary>
/// Safe placeholder Portainer adapter
/// </summary>
public class PortainerClient : IPortainerClient
{
    #region Fields

    private readonly ILogger<PortainerClient> _logger;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="logger">Logger</param>
    public PortainerClient(ILogger<PortainerClient> logger)
    {
        _logger = logger;
    }

    #endregion // Constructors

    #region Methods

    /// <inheritdoc/>
    public Task<PortainerCapabilityData> GetCapabilityAsync(DockerInstanceOptions instanceOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instanceOptions);

        if (instanceOptions.Portainer.Enabled)
        {
            _logger.PortainerCapabilityActionsDisabled(instanceOptions.Name);
        }
        else
        {
            _logger.PortainerCapabilityNotConfigured(instanceOptions.Name);
        }

        var capability = new PortainerCapabilityData
                         {
                             IsConfigured = instanceOptions.Portainer.Enabled,
                             SupportsActions = false,
                             Message = instanceOptions.Portainer.Enabled
                                 ? "Portainer is configured, but actions are intentionally disabled in the first host iteration"
                                 : "Portainer is not configured for this Docker instance",
                         };

        return Task.FromResult(capability);
    }

    /// <inheritdoc/>
    public Task<PortainerActionResult> ExecuteActionAsync(DockerInstanceOptions instanceOptions,
                                                          PortainerActionRequest actionRequest,
                                                          CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instanceOptions);
        ArgumentNullException.ThrowIfNull(actionRequest);

        _logger.PortainerActionRejected(actionRequest.ActionName,
                                        actionRequest.ResourceName,
                                        instanceOptions.Name);

        return Task.FromResult(new PortainerActionResult
                               {
                                   Succeeded = false,
                                   Message = "Portainer actions are not implemented in the first host iteration",
                               });
    }

    #endregion // Methods
}