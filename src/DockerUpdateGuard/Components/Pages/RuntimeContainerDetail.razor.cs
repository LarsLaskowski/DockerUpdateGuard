using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Runtime container detail page
/// </summary>
public partial class RuntimeContainerDetail
{
    #region Fields

    private RuntimeContainerDetailViewData? _detail;
    private string? _errorMessage;
    private bool _isBusy;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Docker instance identifier from the route
    /// </summary>
    [Parameter]
    public Guid DockerInstanceId { get; set; }

    /// <summary>
    /// Container identifier from the route
    /// </summary>
    [Parameter]
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// Application view service
    /// </summary>
    [Inject]
    public IApplicationViewService ViewService { get; set; } = null!;

    /// <summary>
    /// Manual tag selection service
    /// </summary>
    [Inject]
    public IRuntimeContainerTagSelectionService TagSelectionService { get; set; } = null!;

    #endregion // Properties

    #region Methods

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        await LoadAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Load the detail view model
    /// </summary>
    /// <returns>Task</returns>
    private async Task LoadAsync()
    {
        var detail = await ViewService.GetRuntimeContainerDetailAsync(DockerInstanceId, ContainerId)
                                      .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _detail = detail;
                          }).ConfigureAwait(false);
    }

    /// <summary>
    /// Save a manual tag selection
    /// </summary>
    /// <param name="tag">Selected tag</param>
    /// <param name="digest">Selected digest</param>
    /// <returns>Task</returns>
    private async Task SaveSelectionAsync(string tag, string? digest)
    {
        await InvokeAsync(() =>
                          {
                              _isBusy = true;
                              _errorMessage = null;
                          }).ConfigureAwait(false);

        try
        {
            await TagSelectionService.SaveSelectionAsync(DockerInstanceId, ContainerId, tag, digest)
                                     .ConfigureAwait(false);
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
                                  _isBusy = false;
                              }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Clear the manual tag selection
    /// </summary>
    /// <returns>Task</returns>
    private async Task ClearSelectionAsync()
    {
        await InvokeAsync(() =>
                          {
                              _isBusy = true;
                              _errorMessage = null;
                          }).ConfigureAwait(false);

        try
        {
            await TagSelectionService.ClearSelectionAsync(DockerInstanceId, ContainerId)
                                     .ConfigureAwait(false);
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
                                  _isBusy = false;
                              }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolve the chip color for an update status
    /// </summary>
    /// <param name="status">Update status</param>
    /// <returns>Chip color</returns>
    private static Color GetUpdateStateColor(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return Color.Default;
        }

        return status.ToUpperInvariant() switch
               {
                   "UPDATE AVAILABLE" => Color.Warning,
                   "UP TO DATE" => Color.Success,
                   "MANUAL REVIEW REQUIRED" => Color.Info,
                   "FAILED" => Color.Error,
                   "UNSUPPORTED" => Color.Default,
                   _ => Color.Info,
               };
    }

    /// <summary>
    /// Resolve the chip color for a vulnerability status
    /// </summary>
    /// <param name="status">Vulnerability status</param>
    /// <returns>Chip color</returns>
    private static Color GetVulnerabilityStatusColor(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return Color.Default;
        }

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
    /// Resolve the chip color for a vulnerability severity
    /// </summary>
    /// <param name="severity">Severity label</param>
    /// <returns>Chip color</returns>
    private static Color GetSeverityColor(string severity)
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

    #endregion // Methods
}