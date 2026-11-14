using DockerUpdateGuard.Data.Entities;

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

    #endregion // Properties
}