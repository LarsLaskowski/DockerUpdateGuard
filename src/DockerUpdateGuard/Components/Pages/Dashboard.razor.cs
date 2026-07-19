using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Dashboard page
/// </summary>
public sealed partial class Dashboard : IDisposable
{
    #region Constants

    /// <summary>
    /// Persistent-state key for the prerendered dashboard data
    /// </summary>
    private const string DashboardStateKey = "Dashboard.State";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Persisting-state subscription used to hand the prerendered data to the interactive render
    /// </summary>
    private PersistingComponentStateSubscription _persistingSubscription;

    /// <summary>
    /// Dashboard view data
    /// </summary>
    private DashboardViewData? _dashboard;

    /// <summary>
    /// Whether the My Images tile should be shown
    /// </summary>
    private bool _showMyImages;

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

    /// <summary>
    /// Application options
    /// </summary>
    [Inject]
    public IOptions<DockerUpdateGuardOptions> AppOptions { get; set; } = null!;

    /// <summary>
    /// Persistent component state used to reuse the prerendered data on the interactive render
    /// </summary>
    [Inject]
    public PersistentComponentState PersistentState { get; set; } = null!;

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
                   "My Images" => "/my-images",
                   "Observed Images" => "/observed-images",
                   "Docker Instances" => "/docker-instances",
                   "Runtime Containers" => "/runtime-containers",
                   "Base Images" => "/base-images",
                   _ => throw new ArgumentOutOfRangeException(nameof(metricLabel), metricLabel, "Dashboard metric cards must map to known overview routes"),
               };
    }

    /// <summary>
    /// Determine whether Docker Hub account discovery is configured for the given options
    /// </summary>
    /// <param name="dockerHub">Docker Hub options to evaluate</param>
    /// <returns>True when both UserName and Pat are non-empty</returns>
    private static bool IsDockerHubAccountConfigured(DockerHubOptions dockerHub)
    {
        return string.IsNullOrWhiteSpace(dockerHub.UserName) == false
               && string.IsNullOrWhiteSpace(dockerHub.Pat) == false;
    }

    #endregion // Static methods

    #region Methods

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

    /// <summary>
    /// Persist the current dashboard data so the interactive render can reuse the prerendered data
    /// </summary>
    /// <returns>Task</returns>
    private Task PersistDashboard()
    {
        if (_dashboard is not null)
        {
            PersistentState.PersistAsJson(DashboardStateKey, _dashboard);
        }

        return Task.CompletedTask;
    }

    #endregion // Methods

    #region ComponentBase

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        _showMyImages = IsDockerHubAccountConfigured(AppOptions.Value.DockerHub);

        DashboardRefreshState.Changed += OnDashboardRefreshRequested;
        _persistingSubscription = PersistentState.RegisterOnPersisting(PersistDashboard);

        if (PersistentState.TryTakeFromJson<DashboardViewData>(DashboardStateKey, out var restoredDashboard)
            && restoredDashboard is not null)
        {
            _dashboard = restoredDashboard;

            return;
        }

        await LoadAsync().ConfigureAwait(false);
    }

    #endregion // ComponentBase

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        DashboardRefreshState.Changed -= OnDashboardRefreshRequested;
        _persistingSubscription.Dispose();
    }

    #endregion // IDisposable
}