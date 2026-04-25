namespace DockerUpdateGuard.UI;

/// <summary>
/// Runtime container manual tag selection command service
/// </summary>
public interface IRuntimeContainerTagSelectionService
{
    #region Methods

    /// <summary>
    /// Persist a manual tag selection for a runtime container
    /// </summary>
    /// <param name="dockerInstanceId">Docker instance identifier</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="tag">Selected tag</param>
    /// <param name="digest">Selected digest</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    Task SaveSelectionAsync(Guid dockerInstanceId,
                            string containerId,
                            string tag,
                            string? digest,
                            CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear a manual tag selection for a runtime container
    /// </summary>
    /// <param name="dockerInstanceId">Docker instance identifier</param>
    /// <param name="containerId">Container identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    Task ClearSelectionAsync(Guid dockerInstanceId, string containerId, CancellationToken cancellationToken = default);

    #endregion // Methods
}