using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Observed image detail page
/// </summary>
public partial class ObservedImageDetail
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
    private static Color GetScanStatusColor(string status)
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
    /// Resolve the chip color for a vulnerability severity
    /// </summary>
    /// <param name="severity">Severity label</param>
    /// <returns>Chip color</returns>
    private static Color GetVulnerabilitySeverityColor(string severity)
    {
        return severity.ToUpperInvariant() switch
               {
                   "CRITICAL" => Color.Error,
                   "HIGH" => Color.Warning,
                   "MEDIUM" => Color.Info,
                   "LOW" => Color.Success,
                   _ => Color.Default,
               };
    }

    /// <summary>
    /// Resolve the chip color for a vulnerability assessment status
    /// </summary>
    /// <param name="status">Status label</param>
    /// <returns>Chip color</returns>
    private static Color GetVulnerabilityAssessmentColor(string status)
    {
        return status.ToUpperInvariant() switch
               {
                   "FINDINGS DETECTED" => Color.Warning,
                   "NO FINDINGS" => Color.Success,
                   "FAILED" => Color.Error,
                   "NOT CONFIGURED" => Color.Default,
                   "UNSUPPORTED" => Color.Default,
                   _ => Color.Info,
               };
    }

    /// <summary>
    /// Resolve the chip color for a base-runtime alert
    /// </summary>
    /// <param name="isOwnImage">Whether the image is owned</param>
    /// <returns>Chip color</returns>
    private static Color GetBaseRuntimeAlertColor(bool isOwnImage)
    {
        return isOwnImage ? Color.Warning : Color.Info;
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