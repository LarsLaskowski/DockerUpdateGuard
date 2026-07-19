using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// My image detail page — dedicated drilldown for a discovery-owned image
/// </summary>
public partial class MyImageDetail
{
    #region Fields

    /// <summary>
    /// Observed-image detail view data
    /// </summary>
    private ObservedImageDetailViewData? _detail;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Observed image identifier from the route
    /// </summary>
    [Parameter]
    public Guid ObservedImageId { get; set; }

    /// <summary>
    /// Application view service
    /// </summary>
    [Inject]
    public IApplicationViewService ViewService { get; set; } = null!;

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Resolve the chip color for a scan status
    /// </summary>
    /// <param name="status">Scan status</param>
    /// <returns>Chip color</returns>
    internal static Color GetScanStatusColor(string status)
    {
        return status.ToUpperInvariant() switch
               {
                   "SUCCEEDED" => Color.Success,
                   "COMPLETED" => Color.Success,
                   "PARTIAL" => Color.Warning,
                   "FAILED" => Color.Error,
                   "RUNNING" => Color.Info,
                   "PENDING" => Color.Warning,
                   _ => Color.Default,
               };
    }

    /// <summary>
    /// Resolve the chip color for a vulnerability assessment status
    /// </summary>
    /// <param name="status">Status label</param>
    /// <param name="severitySummary">Severity summary of the active findings</param>
    /// <returns>Chip color</returns>
    internal static Color GetVulnerabilityAssessmentColor(string status, VulnerabilitySeveritySummaryViewData? severitySummary)
    {
        return VulnerabilityDisplayFormatter.GetStatusColor(status, severitySummary);
    }

    #endregion // Static methods

    #region ComponentBase

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        var detail = await ViewService.GetObservedImageDetailAsync(ObservedImageId)
                                      .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _detail = detail;
                          }).ConfigureAwait(false);
    }

    #endregion // ComponentBase
}