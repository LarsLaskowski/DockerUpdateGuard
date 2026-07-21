using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Base images page
/// </summary>
public sealed partial class SharedBaseImages : IDisposable
{
    #region Constants

    /// <summary>
    /// Persistent-state key for the prerendered base-image list
    /// </summary>
    private const string BaseImagesStateKey = "BaseImages.List";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Persisting-state subscription used to hand the prerendered data to the interactive render
    /// </summary>
    private PersistingComponentStateSubscription _persistingSubscription;

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

    /// <summary>
    /// Persistent component state used to reuse the prerendered data on the interactive render
    /// </summary>
    [Inject]
    public PersistentComponentState PersistentState { get; set; } = null!;

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

    #region Methods

    /// <summary>
    /// Persist the current base-image list so the interactive render can reuse the prerendered data
    /// </summary>
    /// <returns>Task</returns>
    private Task PersistItems()
    {
        if (_items is not null)
        {
            PersistentState.PersistAsJson(BaseImagesStateKey, _items);
        }

        return Task.CompletedTask;
    }

    #endregion // Methods

    #region ComponentBase

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        _persistingSubscription = PersistentState.RegisterOnPersisting(PersistItems);

        if (PersistentState.TryTakeFromJson<IReadOnlyList<SharedBaseImageListItemData>>(BaseImagesStateKey, out var restoredItems)
            && restoredItems is not null)
        {
            _items = restoredItems;

            return;
        }

        var items = await ViewService.GetBaseImagesAsync()
                                     .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _items = items;
                          }).ConfigureAwait(false);
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