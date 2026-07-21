using Microsoft.AspNetCore.Components;

namespace DockerUpdateGuard.Components.Shared;

/// <summary>
/// Skeleton placeholder that mirrors the dashboard metric-card grid while data is loading
/// </summary>
public sealed partial class MetricGridSkeleton
{
    #region Properties

    /// <summary>
    /// Number of placeholder metric cards to render
    /// </summary>
    [Parameter]
    public int Cards { get; set; } = 6;

    #endregion // Properties
}