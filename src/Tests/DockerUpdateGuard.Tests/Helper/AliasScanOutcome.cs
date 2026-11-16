using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// State of one container on an alias tag after two scans
/// </summary>
internal sealed class AliasScanOutcome
{
    #region Properties

    /// <summary>
    /// Snapshot of the first scan
    /// </summary>
    public ContainerSnapshot FirstSnapshot { get; init; } = null!;

    /// <summary>
    /// Snapshots of the second scan
    /// </summary>
    public List<ContainerSnapshot> SecondSnapshots { get; init; } = [];

    /// <summary>
    /// Findings after the first scan
    /// </summary>
    public List<UpdateFinding> FirstFindings { get; init; } = [];

    /// <summary>
    /// Findings after the second scan
    /// </summary>
    public List<UpdateFinding> Findings { get; init; } = [];

    /// <summary>
    /// Status of the second scan's run
    /// </summary>
    public ScanRunStatus SecondRunStatus { get; init; }

    #endregion // Properties
}