using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Common;

/// <summary>
/// Confirmation dialog for a container action
/// </summary>
public sealed partial class ContainerActionConfirmation
{
    #region Properties

    /// <summary>
    /// Dialog instance
    /// </summary>
    [CascadingParameter]
    public IMudDialogInstance MudDialog { get; set; } = null!;

    /// <summary>
    /// Action type to confirm
    /// </summary>
    [Parameter]
    public ContainerActionType ActionType { get; set; }

    /// <summary>
    /// Container name
    /// </summary>
    [Parameter]
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// Docker instance name
    /// </summary>
    [Parameter]
    public string DockerInstanceName { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the Portainer environment
    /// </summary>
    [Parameter]
    public string EndpointId { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the container is the application's own container
    /// </summary>
    [Parameter]
    public bool IsOwnContainer { get; set; }

    /// <summary>
    /// Display name of the action
    /// </summary>
    private string DisplayName => ContainerActionDisplayNames.GetDisplayName(ActionType);

    /// <summary>
    /// Consequence of the action; empty when it has none worth stating
    /// </summary>
    private string Consequence => ActionType switch
                                  {
                                      ContainerActionType.StopContainer => "The container stops and stays stopped until it is started again.",
                                      ContainerActionType.RestartContainer => "The container is unavailable while it restarts.",
                                      _ => string.Empty,
                                  };

    /// <summary>
    /// Verb of the own-container warning
    /// </summary>
    private string OwnContainerVerb => ActionType == ContainerActionType.RestartContainer ? "restarts" : "stops";

    /// <summary>
    /// Color of the confirmation button: destructive for stop and restart
    /// </summary>
    private Color ConfirmColor => ActionType is ContainerActionType.StopContainer or ContainerActionType.RestartContainer ? Color.Error : Color.Primary;

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Close the dialog without confirming
    /// </summary>
    private void Cancel()
    {
        MudDialog.Cancel();
    }

    /// <summary>
    /// Close the dialog and confirm the action
    /// </summary>
    private void Confirm()
    {
        MudDialog.Close(DialogResult.Ok(true));
    }

    #endregion // Methods
}