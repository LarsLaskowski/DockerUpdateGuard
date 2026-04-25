using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Runtime containers page
/// </summary>
public partial class RuntimeContainers
{
    #region Fields

    private IReadOnlyList<RuntimeContainerListItemData>? _containers;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Application view service
    /// </summary>
    [Inject]
    public IApplicationViewService ViewService { get; set; } = null!;

    #endregion // Properties

    #region Methods

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        _containers = await ViewService.GetRuntimeContainersAsync()
                                       .ConfigureAwait(true);
    }

    /// <summary>
    /// Resolve the chip color for an update state
    /// </summary>
    /// <param name="state">Update state</param>
    /// <returns>Chip color</returns>
    private static Color GetUpdateStateColor(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return Color.Default;
        }

        return state.ToUpperInvariant() switch
               {
                   "UPDATEAVAILABLE" => Color.Warning,
                   "UPTODATE" => Color.Success,
                   "UNKNOWN" => Color.Default,
                   _ => Color.Info,
               };
    }

    #endregion // Methods
}