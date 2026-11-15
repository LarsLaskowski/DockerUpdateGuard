using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.UI;

/// <summary>
/// Display names of container action types
/// </summary>
public static class ContainerActionDisplayNames
{
    #region Static methods

    /// <summary>
    /// Get the display name of an action type
    /// </summary>
    /// <param name="actionType">Action type</param>
    /// <returns>Display name</returns>
    public static string GetDisplayName(ContainerActionType actionType)
    {
        return actionType switch
               {
                   ContainerActionType.StartContainer => "Start",
                   ContainerActionType.StopContainer => "Stop",
                   ContainerActionType.RestartContainer => "Restart",
                   ContainerActionType.RedeployStack => "Redeploy stack",
                   ContainerActionType.UpdateService => "Update service",
                   _ => actionType.ToString(),
               };
    }

    #endregion // Static methods
}