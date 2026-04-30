using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Dashboard page
/// </summary>
public sealed partial class Dashboard : IDisposable
{
    #region Fields

    /// <summary>
    /// Dashboard view data
    /// </summary>
    private DashboardViewData? _dashboard;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Application view service
    /// </summary>
    [Inject]
    public IApplicationViewService ViewService { get; set; } = null!;

    /// <summary>
    /// Dashboard refresh state
    /// </summary>
    [Inject]
    public DashboardRefreshState DashboardRefreshState { get; set; } = null!;

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
                   "COMPLETED" => Color.Success,
                   "FAILED" => Color.Error,
                   "RUNNING" => Color.Info,
                   "PENDING" => Color.Warning,
                   _ => Color.Default,
               };
    }

    /// <summary>
    /// Resolve the overview route for a dashboard metric card
    /// </summary>
    /// <param name="metricLabel">Metric card label</param>
    /// <returns>Navigation target</returns>
    private static string GetMetricNavigationTarget(string metricLabel)
    {
        return metricLabel switch
               {
                   "Observed Images" => "/observed-images",
                   "Docker Instances" => "/docker-instances",
                   "Runtime Containers" => "/runtime-containers",
                   "Shared Bases" => "/shared-base-images",
                   _ => throw new ArgumentOutOfRangeException(nameof(metricLabel), metricLabel, "Dashboard metric cards must map to known overview routes"),
               };
    }

    #endregion // Static methods

    #region Methods

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        DashboardRefreshState.Changed += OnDashboardRefreshRequested;

        await LoadAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Dispose component subscriptions
    /// </summary>
    public void Dispose()
    {
        DashboardRefreshState.Changed -= OnDashboardRefreshRequested;
    }

    /// <summary>
    /// Load the dashboard view model
    /// </summary>
    /// <returns>Task</returns>
    private async Task LoadAsync()
    {
        var dashboard = await ViewService.GetDashboardAsync()
                                         .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _dashboard = dashboard;

                              StateHasChanged();
                          }).ConfigureAwait(false);
    }

    /// <summary>
    /// React to explicit dashboard refresh notifications
    /// </summary>
    private void OnDashboardRefreshRequested()
    {
        _ = InvokeAsync(LoadAsync);
    }

    #endregion // Methods
}