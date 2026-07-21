using Microsoft.AspNetCore.Components;

namespace DockerUpdateGuard.Components.Shared;

/// <summary>
/// Skeleton placeholder that mirrors a section table while data is loading
/// </summary>
public sealed partial class TableSkeleton
{
    #region Properties

    /// <summary>
    /// Number of placeholder rows to render
    /// </summary>
    [Parameter]
    public int Rows { get; set; } = 5;

    /// <summary>
    /// Optional section title; when set, a title placeholder bar is rendered above the rows
    /// </summary>
    [Parameter]
    public string? Title { get; set; }

    #endregion // Properties
}