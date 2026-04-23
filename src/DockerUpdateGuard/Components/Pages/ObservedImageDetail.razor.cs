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

    #region Methods

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        _detail = await ViewService.GetObservedImageDetailAsync(ObservedImageId)
                                   .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolve the chip color for a scan status
    /// </summary>
    /// <param name="status">Scan status</param>
    /// <returns>Chip color</returns>
    private static Color GetScanStatusColor(string status)
    {
        return status.ToUpperInvariant() switch
               {
                   "COMPLETED" => Color.Success,
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

    #endregion // Methods
}