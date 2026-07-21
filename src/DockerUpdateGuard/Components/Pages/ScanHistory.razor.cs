using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Scan history page
/// </summary>
public sealed partial class ScanHistory : IDisposable
{
    #region Constants

    /// <summary>
    /// Persistent-state key for the prerendered scan-history list
    /// </summary>
    private const string ScanHistoryStateKey = "ScanHistory.List";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Persisting-state subscription used to hand the prerendered data to the interactive render
    /// </summary>
    private PersistingComponentStateSubscription _persistingSubscription;

    /// <summary>
    /// Scan-history items
    /// </summary>
    private IReadOnlyList<ScanHistoryItemData>? _scans;

    /// <summary>
    /// Current error message
    /// </summary>
    private string? _errorMessage;

    /// <summary>
    /// Busy-state flag for the manual vulnerability refresh
    /// </summary>
    private bool _isRefreshingVulnerabilities;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Application view service
    /// </summary>
    [Inject]
    public IApplicationViewService ViewService { get; set; } = null!;

    /// <summary>
    /// Service-scope factory
    /// </summary>
    [Inject]
    public IServiceScopeFactory ServiceScopeFactory { get; set; } = null!;

    /// <summary>
    /// Application options monitor
    /// </summary>
    [Inject]
    public IOptionsMonitor<DockerUpdateGuardOptions> OptionsMonitor { get; set; } = null!;

    /// <summary>
    /// Dashboard refresh state
    /// </summary>
    [Inject]
    public DashboardRefreshState DashboardRefreshState { get; set; } = null!;

    /// <summary>
    /// Persistent component state used to reuse the prerendered data on the interactive render
    /// </summary>
    [Inject]
    public PersistentComponentState PersistentState { get; set; } = null!;

    /// <summary>
    /// Indicates whether vulnerability refresh is enabled
    /// </summary>
    private bool VulnerabilitiesEnabled => OptionsMonitor.CurrentValue.Vulnerabilities.Enabled;

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

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Load the scan history
    /// </summary>
    /// <returns>Task</returns>
    private async Task LoadAsync()
    {
        var scans = await ViewService.GetScanHistoryAsync()
                                     .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _scans = scans;

                              StateHasChanged();
                          }).ConfigureAwait(false);
    }

    /// <summary>
    /// Persist the current scan-history data so the interactive render can reuse the prerendered data
    /// </summary>
    /// <returns>Task</returns>
    private Task PersistScanHistory()
    {
        if (_scans is not null)
        {
            PersistentState.PersistAsJson(ScanHistoryStateKey, _scans);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Trigger a manual vulnerability refresh
    /// </summary>
    /// <returns>Task</returns>
    private async Task TriggerVulnerabilityRefreshAsync()
    {
        await InvokeAsync(() =>
                          {
                              _isRefreshingVulnerabilities = true;
                              _errorMessage = null;
                          }).ConfigureAwait(false);

        try
        {
            var scope = ServiceScopeFactory.CreateAsyncScope();

            await using (scope.ConfigureAwait(false))
            {
                var enrichmentService = scope.ServiceProvider.GetRequiredService<IVulnerabilityEnrichmentService>();

                await enrichmentService.RefreshAsync(ScanTriggerSource.Manual)
                                       .ConfigureAwait(false);
            }

            DashboardRefreshState.NotifyChanged();

            await LoadAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await InvokeAsync(() =>
                              {
                                  _errorMessage = exception.Message;
                              }).ConfigureAwait(false);
        }
        finally
        {
            await InvokeAsync(() =>
                              {
                                  _isRefreshingVulnerabilities = false;

                                  StateHasChanged();
                              }).ConfigureAwait(false);
        }
    }

    #endregion // Methods

    #region ComponentBase

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        _persistingSubscription = PersistentState.RegisterOnPersisting(PersistScanHistory);

        if (PersistentState.TryTakeFromJson<IReadOnlyList<ScanHistoryItemData>>(ScanHistoryStateKey, out var restoredScans)
            && restoredScans is not null)
        {
            _scans = restoredScans;

            return;
        }

        await LoadAsync().ConfigureAwait(false);
    }

    #endregion // ComponentBase

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        _persistingSubscription.Dispose();
    }

    #endregion // IDisposable
}