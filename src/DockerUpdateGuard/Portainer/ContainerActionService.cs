using System.Collections.Concurrent;
using System.Diagnostics;

using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Portainer.Data;
using DockerUpdateGuard.Portainer.Interfaces;
using DockerUpdateGuard.Telemetry;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Portainer;

/// <summary>
/// Container action service
/// </summary>
public sealed class ContainerActionService : IContainerActionService
{
    #region Constants

    /// <summary>
    /// Value of RequestedBy of every run: the application has no sign-in
    /// </summary>
    private const string RequestedByUi = "UI";

    /// <summary>
    /// Length of the host name Docker gives a container by default
    /// </summary>
    private const int DockerHostNameLength = 12;

    /// <summary>
    /// Message of a container that is not in the latest runtime scan of its instance, as shown on confirmation
    /// </summary>
    private const string NotCurrentRunMessage = "Container is not in the latest runtime scan; run a runtime scan and try again";

    /// <summary>
    /// Message of a container with another action in progress, as shown on confirmation
    /// </summary>
    private const string InProgressRunMessage = "Another action is in progress for this container";

    /// <summary>
    /// Message of a container with an action in progress, as shown on page load
    /// </summary>
    private const string InProgressPageMessage = "An action is in progress for this container.";

    /// <summary>
    /// Message of an action cancelled by an application stop before the request was sent
    /// </summary>
    private const string CancelledMessage = "Cancelled: the application stopped before the action was sent";

    #endregion // Constants

    #region Fields

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

    /// <summary>
    /// Containers an action of this process is executing for, by Docker instance and container identifier
    /// </summary>
    private readonly ConcurrentDictionary<(Guid DockerInstanceId, string ContainerId), byte> _actionsInProgress = new();

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

    #region Static methods

    /// <summary>
    /// Get the actions offered for a runtime status
    /// </summary>
    /// <param name="status">Runtime status of the snapshot</param>
    /// <returns>Offered actions</returns>
    private static IReadOnlyList<ContainerActionType> GetOfferedActions(ContainerRuntimeStatus status)
    {
        return status switch
               {
                   ContainerRuntimeStatus.Running or ContainerRuntimeStatus.Paused or ContainerRuntimeStatus.Restarting => [ContainerActionType.StopContainer, ContainerActionType.RestartContainer],
                   ContainerRuntimeStatus.Dead => [],
                   _ => [ContainerActionType.StartContainer],
               };
    }

    /// <summary>
    /// Find the configuration of a Docker instance
    /// </summary>
    /// <param name="options">Application options</param>
    /// <param name="instanceName">Docker instance name</param>
    /// <returns>Instance options; null when the instance is not configured</returns>
    private static DockerInstanceOptions? FindInstanceOptions(DockerUpdateGuardOptions options, string instanceName)
    {
        return options.DockerInstances.FirstOrDefault(entity => string.Equals(entity.Name, instanceName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Load a Docker instance, the latest snapshot of a container and whether that snapshot belongs to the latest runtime scan that recorded snapshots
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="dockerInstanceId">Docker instance identifier</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Instance, snapshot and currency; instance or snapshot are null when unknown</returns>
    private static async Task<(DockerInstance? Instance, ContainerSnapshot? Snapshot, bool IsCurrent)> LoadTargetAsync(DockerUpdateGuardDbContext dbContext,
                                                                                                                       Guid dockerInstanceId,
                                                                                                                       string containerId,
                                                                                                                       CancellationToken cancellationToken)
    {
        var instance = await dbContext.DockerInstances.Include(entity => entity.PortainerEndpoint)
                                                      .AsNoTracking()
                                                      .FirstOrDefaultAsync(entity => entity.Id == dockerInstanceId, cancellationToken)
                                                      .ConfigureAwait(false);

        if (instance is null)
        {
            return (null, null, false);
        }

        var snapshot = await dbContext.ContainerSnapshots.AsNoTracking()
                                                         .Where(entity => entity.DockerInstanceId == dockerInstanceId && entity.ContainerId == containerId)
                                                         .OrderByDescending(entity => entity.RecordedAtUtc)
                                                         .FirstOrDefaultAsync(cancellationToken)
                                                         .ConfigureAwait(false);

        if (snapshot is null)
        {
            return (instance, null, false);
        }

        var latestScanRunId = await dbContext.ScanRuns.AsNoTracking()
                                                      .Where(entity => entity.DockerInstanceId == dockerInstanceId
                                                                       && entity.Type == ScanRunType.RuntimeContainer
                                                                       && entity.ContainerSnapshots.Any())
                                                      .OrderByDescending(entity => entity.StartedAtUtc)
                                                      .Select(entity => (Guid?)entity.Id)
                                                      .FirstOrDefaultAsync(cancellationToken)
                                                      .ConfigureAwait(false);

        return (instance, snapshot, latestScanRunId is not null && snapshot.ScanRunId == latestScanRunId);
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Check whether a container is the application's own container
    /// </summary>
    /// <param name="containerId">Container identifier</param>
    /// <returns>True when HOSTNAME is a default Docker host name that starts the container identifier</returns>
    private bool IsOwnContainer(string containerId)
    {
        var hostName = _configuration["HOSTNAME"];

        return hostName is not null
               && hostName.Length == DockerHostNameLength
               && hostName.All(c => (c is >= '0' and <= '9') || (c is >= 'a' and <= 'f'))
               && containerId.StartsWith(hostName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Write a run
    /// </summary>
    /// <param name="run">Run to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task AddRunAsync(ContainerActionRun run, CancellationToken cancellationToken)
    {
        var scope = _serviceScopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();

            dbContext.ContainerActionRuns.Add(run);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Write the status of an existing run
    /// </summary>
    /// <param name="runId">Run identifier</param>
    /// <param name="status">New status</param>
    /// <param name="message">New message; null keeps the stored one</param>
    /// <param name="completedAtUtc">Completion time; null for a run that is not finished</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    private async Task UpdateRunAsync(Guid runId,
                                      ContainerActionStatus status,
                                      string? message,
                                      DateTimeOffset? completedAtUtc,
                                      CancellationToken cancellationToken)
    {
        var scope = _serviceScopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>();
            var run = await dbContext.ContainerActionRuns.FirstAsync(entity => entity.Id == runId, cancellationToken).ConfigureAwait(false);

            run.Status = status;
            run.ErrorMessage = message ?? run.ErrorMessage;
            run.CompletedAtUtc = completedAtUtc;

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Create a run of an action on the container of a snapshot
    /// </summary>
    /// <param name="instance">Docker instance</param>
    /// <param name="snapshot">Snapshot of the container</param>
    /// <param name="actionType">Action type</param>
    /// <param name="status">Initial status</param>
    /// <returns>Run</returns>
    private ContainerActionRun CreateRun(DockerInstance instance, ContainerSnapshot snapshot, ContainerActionType actionType, ContainerActionStatus status)
    {
        return new ContainerActionRun
               {
                   DockerInstanceId = instance.Id,
                   PortainerEndpointId = instance.PortainerEndpoint?.Id,
                   ContainerSnapshotId = snapshot.Id,
                   ActionType = actionType,
                   ResourceType = PortainerResourceType.Container,
                   Status = status,
                   ResourceName = snapshot.Name,
                   RequestedBy = RequestedByUi,
                   RequestedAtUtc = _timeProvider.GetUtcNow(),
               };
    }

    /// <summary>
    /// Record a confirmation that a check rejected as a failed run
    /// </summary>
    /// <param name="instance">Docker instance</param>
    /// <param name="snapshot">Snapshot of the container</param>
    /// <param name="actionType">Action type</param>
    /// <param name="check">Name of the check that rejected the action</param>
    /// <param name="message">Message of the run</param>
    /// <returns>Failed result</returns>
    private async Task<ContainerActionRunResultData> RecordRejectionAsync(DockerInstance instance,
                                                                          ContainerSnapshot snapshot,
                                                                          ContainerActionType actionType,
                                                                          string check,
                                                                          string message)
    {
        _logger.ContainerActionRejected(actionType, snapshot.Name, instance.Name, check);

        var run = CreateRun(instance, snapshot, actionType, ContainerActionStatus.Failed);

        run.ErrorMessage = message;
        run.CompletedAtUtc = run.RequestedAtUtc;

        await AddRunAsync(run, CancellationToken.None).ConfigureAwait(false);

        return new ContainerActionRunResultData
               {
                   Status = ContainerActionStatus.Failed,
                   Message = message,
               };
    }

    /// <summary>
    /// Check the target of an action against the current configuration and snapshots, and record the confirmation as a failed run when a check rejects it
    /// </summary>
    /// <param name="instance">Docker instance</param>
    /// <param name="snapshot">Snapshot of the container</param>
    /// <param name="instanceOptions">Configuration of the instance; null when it is not configured</param>
    /// <param name="isCurrent">Whether the snapshot is in the latest runtime scan</param>
    /// <param name="actionType">Action type</param>
    /// <returns>Failed result of the rejection; null when every check passed</returns>
    private async Task<ContainerActionRunResultData?> CheckTargetAsync(DockerInstance instance,
                                                                       ContainerSnapshot snapshot,
                                                                       DockerInstanceOptions? instanceOptions,
                                                                       bool isCurrent,
                                                                       ContainerActionType actionType)
    {
        if (instanceOptions is null || instanceOptions.Enabled == false)
        {
            return await RecordRejectionAsync(instance, snapshot, actionType, "instance disabled", $"Docker instance '{instance.Name}' is disabled").ConfigureAwait(false);
        }

        if (instanceOptions.Portainer.Enabled == false)
        {
            return await RecordRejectionAsync(instance, snapshot, actionType, "portainer disabled", $"Portainer is not enabled for Docker instance '{instance.Name}'").ConfigureAwait(false);
        }

        if (isCurrent == false)
        {
            return await RecordRejectionAsync(instance, snapshot, actionType, "not current", NotCurrentRunMessage).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Run an action whose container is reserved for it: write the run, call Portainer and record the outcome
    /// </summary>
    /// <param name="instance">Docker instance</param>
    /// <param name="snapshot">Snapshot of the container</param>
    /// <param name="instanceOptions">Configuration of the instance</param>
    /// <param name="actionType">Action type</param>
    /// <returns>Run result</returns>
    private async Task<ContainerActionRunResultData> RunActionAsync(DockerInstance instance,
                                                                    ContainerSnapshot snapshot,
                                                                    DockerInstanceOptions instanceOptions,
                                                                    ContainerActionType actionType)
    {
        var run = CreateRun(instance, snapshot, actionType, ContainerActionStatus.Requested);

        await AddRunAsync(run, CancellationToken.None).ConfigureAwait(false);
        await UpdateRunAsync(run.Id, ContainerActionStatus.Running, null, null, CancellationToken.None).ConfigureAwait(false);

        _logger.ContainerActionRequested(actionType, snapshot.Name, instance.Name);

        var stoppingToken = _applicationLifetime.ApplicationStopping;
        var request = new PortainerContainerActionRequest
                      {
                          ActionType = actionType,
                          ContainerId = snapshot.ContainerId,
                          ContainerName = snapshot.Name,
                      };
        var outcome = await _portainerClient.ExecuteContainerActionAsync(instanceOptions, request, stoppingToken).ConfigureAwait(false);
        var stopping = stoppingToken.IsCancellationRequested;
        ContainerActionStatus status;
        string message;

        if (outcome.Status == ExternalOperationStatus.Succeeded && outcome.Data is not null)
        {
            status = ContainerActionStatus.Succeeded;
            message = outcome.Data.Message;
        }
        else if (stopping && outcome.Status == ExternalOperationStatus.Unknown)
        {
            // The action may have been applied: the run stays Running and startup reconciles it
            return new ContainerActionRunResultData
                   {
                       Status = ContainerActionStatus.Running,
                       Message = outcome.Message ?? string.Empty,
                   };
        }
        else if (stopping)
        {
            status = ContainerActionStatus.Cancelled;
            message = CancelledMessage;
        }
        else
        {
            status = ContainerActionStatus.Failed;
            message = outcome.Message ?? "Action failed";
        }

        try
        {
            await UpdateRunAsync(run.Id, status, message, _timeProvider.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.ContainerActionRunNotFinalized(actionType, snapshot.Name, instance.Name, status, ex);
        }

        _logger.ContainerActionFinished(actionType, snapshot.Name, instance.Name, status);

        return new ContainerActionRunResultData
               {
                   Status = status,
                   Message = message,
               };
    }

    /// <summary>
    /// Execute an action: check the target, reserve the container and run the action
    /// </summary>
    /// <param name="dockerInstanceId">Docker instance identifier</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="actionType">Action type</param>
    /// <param name="activity">Activity of the action</param>
    /// <returns>Run result</returns>
    private async Task<ContainerActionRunResultData> ExecuteCoreAsync(Guid dockerInstanceId, string containerId, ContainerActionType actionType, Activity? activity)
    {
        var options = _optionsMonitor.CurrentValue;
        (DockerInstance? Instance, ContainerSnapshot? Snapshot, bool IsCurrent) target;
        var scope = _serviceScopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            target = await LoadTargetAsync(scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>(), dockerInstanceId, containerId, CancellationToken.None).ConfigureAwait(false);
        }

        if (target.Instance is null || target.Snapshot is null)
        {
            _logger.ContainerActionTargetUnknown(actionType, dockerInstanceId);

            return new ContainerActionRunResultData
                   {
                       Status = ContainerActionStatus.Failed,
                       Message = NotCurrentRunMessage,
                   };
        }

        var instance = target.Instance;
        var snapshot = target.Snapshot;
        var instanceOptions = FindInstanceOptions(options, instance.Name);

        activity?.SetTag(TelemetryTagNames.DockerInstanceName, instance.Name);
        activity?.SetTag(TelemetryTagNames.ActionType, actionType.ToString());
        activity?.SetTag(TelemetryTagNames.ContainerName, snapshot.Name);

        var rejection = await CheckTargetAsync(instance, snapshot, instanceOptions, target.IsCurrent, actionType).ConfigureAwait(false);

        if (rejection is not null)
        {
            return rejection;
        }

        var key = (dockerInstanceId, snapshot.ContainerId);

        if (_actionsInProgress.TryAdd(key, 0) == false)
        {
            return await RecordRejectionAsync(instance, snapshot, actionType, "in progress", InProgressRunMessage).ConfigureAwait(false);
        }

        try
        {
            return await RunActionAsync(instance, snapshot, instanceOptions!, actionType).ConfigureAwait(false);
        }
        finally
        {
            _actionsInProgress.TryRemove(key, out _);
        }
    }

    #endregion // Methods

    #region IContainerActionService

    /// <inheritdoc/>
    public async Task<ContainerActionAvailabilityData> GetAvailabilityAsync(Guid dockerInstanceId, string containerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(containerId);

        (DockerInstance? Instance, ContainerSnapshot? Snapshot, bool IsCurrent) target;
        var scope = _serviceScopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            target = await LoadTargetAsync(scope.ServiceProvider.GetRequiredService<DockerUpdateGuardDbContext>(), dockerInstanceId, containerId, cancellationToken).ConfigureAwait(false);
        }

        if (target.Instance is null || target.Snapshot is null)
        {
            return new ContainerActionAvailabilityData();
        }

        var instanceOptions = FindInstanceOptions(_optionsMonitor.CurrentValue, target.Instance.Name);

        if (instanceOptions is null || instanceOptions.Enabled == false || instanceOptions.Portainer.Enabled == false)
        {
            return new ContainerActionAvailabilityData();
        }

        var availability = new ContainerActionAvailabilityData
                           {
                               IsShown = true,
                               IsCurrent = target.IsCurrent,
                               IsOwnContainer = IsOwnContainer(target.Snapshot.ContainerId),
                           };

        if (target.IsCurrent == false)
        {
            availability.Message = $"This container is not in the latest runtime scan of '{target.Instance.Name}'; no actions are available.";

            return availability;
        }

        var capability = await _portainerClient.CheckCapabilityAsync(instanceOptions, cancellationToken).ConfigureAwait(false);

        if (capability.Status != ExternalOperationStatus.Succeeded || capability.Data is null)
        {
            availability.Message = capability.Message;

            return availability;
        }

        availability.IsSupported = true;
        availability.EndpointId = capability.Data.EndpointId;
        availability.OfferedActions = GetOfferedActions(target.Snapshot.Status);
        availability.IsInProgress = _actionsInProgress.ContainsKey((dockerInstanceId, target.Snapshot.ContainerId));

        if (availability.IsInProgress)
        {
            availability.Message = InProgressPageMessage;
        }

        return availability;
    }

    /// <inheritdoc/>
    public async Task<ContainerActionRunResultData> ExecuteAsync(Guid dockerInstanceId, string containerId, ContainerActionType actionType)
    {
        ArgumentNullException.ThrowIfNull(containerId);

        using var activity = DockerUpdateGuardTelemetry.ActivitySource.StartActivity(TelemetryActivityNames.PortainerAction, ActivityKind.Internal);
        var result = await ExecuteCoreAsync(dockerInstanceId, containerId, actionType, activity).ConfigureAwait(false);

        activity?.SetTag(TelemetryTagNames.ResultStatus, result.Status.ToString());

        return result;
    }

    #endregion // IContainerActionService
}