using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
    /// Base images page
/// </summary>
public partial class SharedBaseImages
{
    #region Fields

    /// <summary>
    /// Base-image items
    /// </summary>
    private IReadOnlyList<SharedBaseImageListItemData>? _items;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Application view service
    /// </summary>
    [Inject]
    public IApplicationViewService ViewService { get; set; } = null!;

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Resolve the chip color for a vulnerability assessment status
    /// </summary>
    /// <param name="status">Vulnerability assessment status</param>
    /// <param name="severitySummary">Severity summary of the active findings</param>
    /// <returns>Chip color</returns>
    private static Color GetVulnerabilityStatusColor(string? status, VulnerabilitySeveritySummaryViewData? severitySummary)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return Color.Default;
        }

        return VulnerabilityDisplayFormatter.GetStatusColor(status, severitySummary);
    }

    #endregion // Static methods

    #region ComponentBase

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        var items = await ViewService.GetBaseImagesAsync()
                                     .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _items = items;
                          }).ConfigureAwait(false);
    }

    #endregion // ComponentBase
}