using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Fleet-wide vulnerability overview page
/// </summary>
public sealed partial class Vulnerabilities : IDisposable
{
    #region Constants

    /// <summary>
    /// Severity filter value that matches every severity
    /// </summary>
    private const string AllSeveritiesFilterValue = "All";

    /// <summary>
    /// Persistent-state key for the prerendered vulnerability overview
    /// </summary>
    private const string VulnerabilityStateKey = "Vulnerabilities.Overview";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Available severity filter values
    /// </summary>
    private static readonly string[] _severityFilterOptions = [AllSeveritiesFilterValue, "Critical", "High", "Medium", "Low"];

    /// <summary>
    /// Available table page sizes
    /// </summary>
    private static readonly int[] _pageSizeOptions = [25, 50, 100];

    /// <summary>
    /// Advisory identifiers whose affected-images row is currently expanded
    /// </summary>
    private readonly HashSet<string> _expandedAdvisoryIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Persisting-state subscription used to hand the prerendered data to the interactive render
    /// </summary>
    private PersistingComponentStateSubscription _persistingSubscription;

    /// <summary>
    /// Vulnerability overview items
    /// </summary>
    private IReadOnlyList<VulnerabilityOverviewItemData>? _items;

    /// <summary>
    /// Free-text filter
    /// </summary>
    private string _searchText = string.Empty;

    /// <summary>
    /// Selected severity filter
    /// </summary>
    private string _severityFilter = AllSeveritiesFilterValue;

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

    /// <summary>
    /// Available severity filter values
    /// </summary>
    private static IReadOnlyList<string> SeverityFilterOptions => _severityFilterOptions;

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Resolve the chip color for a vulnerability severity label
    /// </summary>
    /// <param name="severity">Severity label</param>
    /// <returns>Chip color</returns>
    private static Color GetSeverityColor(string severity)
    {
        return VulnerabilityDisplayFormatter.GetSeverityColor(severity);
    }

    /// <summary>
    /// Resolve the severity-rail CSS class for a vulnerability severity label
    /// </summary>
    /// <param name="severity">Severity label</param>
    /// <returns>Severity-rail CSS class</returns>
    private static string GetSeverityRailClass(string severity)
    {
        return severity.ToUpperInvariant() switch
               {
                   "CRITICAL" => "dug-rail-critical",
                   "HIGH" => "dug-rail-high",
                   "MEDIUM" => "dug-rail-medium",
                   "LOW" => "dug-rail-low",
                   _ => string.Empty,
               };
    }

    /// <summary>
    /// Resolve the detail page link for an affected image
    /// </summary>
    /// <param name="affectedImage">Affected image</param>
    /// <returns>Detail page relative URL</returns>
    private static string GetImageDetailHref(VulnerabilityOverviewAffectedImageData affectedImage)
    {
        return affectedImage.IsOwnImage
                   ? $"/my-images/{affectedImage.ObservedImageId}"
                   : $"/observed-images/{affectedImage.ObservedImageId}";
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Resolve the vulnerability overview items matching the current filter settings
    /// </summary>
    /// <returns>Filtered vulnerability overview items</returns>
    private List<VulnerabilityOverviewItemData> GetFilteredItems()
    {
        var items = (_items ?? []).AsEnumerable();

        if (string.Equals(_severityFilter, AllSeveritiesFilterValue, StringComparison.OrdinalIgnoreCase) == false)
        {
            items = items.Where(entity => string.Equals(entity.Severity, _severityFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (string.IsNullOrWhiteSpace(_searchText) == false)
        {
            items = items.Where(MatchesSearchText);
        }

        return items.ToList();
    }

    /// <summary>
    /// Check whether a vulnerability overview item matches the current free-text filter
    /// </summary>
    /// <param name="item">Vulnerability overview item to check</param>
    /// <returns>True when the item matches</returns>
    private bool MatchesSearchText(VulnerabilityOverviewItemData item)
    {
        return item.AdvisoryId.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || item.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || item.AffectedPackages.Any(package => package.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determine whether the affected-images row of an advisory is expanded
    /// </summary>
    /// <param name="item">Vulnerability overview item</param>
    /// <returns>True when the row is expanded</returns>
    private bool IsExpanded(VulnerabilityOverviewItemData item)
    {
        return _expandedAdvisoryIds.Contains(item.AdvisoryId);
    }

    /// <summary>
    /// Toggle the affected-images row of an advisory
    /// </summary>
    /// <param name="item">Vulnerability overview item</param>
    private void ToggleExpanded(VulnerabilityOverviewItemData item)
    {
        if (_expandedAdvisoryIds.Remove(item.AdvisoryId) == false)
        {
            _expandedAdvisoryIds.Add(item.AdvisoryId);
        }
    }

    /// <summary>
    /// Persist the current vulnerability overview so the interactive render can reuse the prerendered data
    /// </summary>
    /// <returns>Task</returns>
    private Task PersistItems()
    {
        if (_items is not null)
        {
            PersistentState.PersistAsJson(VulnerabilityStateKey, _items);
        }

        return Task.CompletedTask;
    }

    #endregion // Methods

    #region ComponentBase

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        _persistingSubscription = PersistentState.RegisterOnPersisting(PersistItems);

        if (PersistentState.TryTakeFromJson<IReadOnlyList<VulnerabilityOverviewItemData>>(VulnerabilityStateKey, out var restoredItems)
            && restoredItems is not null)
        {
            _items = restoredItems;

            return;
        }

        var items = await ViewService.GetVulnerabilityOverviewAsync()
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