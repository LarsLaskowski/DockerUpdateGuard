using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// State of one container after two scans
/// </summary>
internal sealed class TwoScanOutcome
{
    #region Properties

    /// <summary>
    /// Id of the first scan's snapshot
    /// </summary>
    public Guid FirstSnapshotId { get; init; }

    /// <summary>
    /// Id of the second scan's snapshot
    /// </summary>
    public Guid SecondSnapshotId { get; init; }

    /// <summary>
    /// Update assessment of the second scan's snapshot
    /// </summary>
    public UpdateAssessmentStatus SecondAssessmentStatus { get; init; }

    /// <summary>
    /// Findings after the first scan
    /// </summary>
    public List<UpdateFinding> FirstFindings { get; init; } = [];

    /// <summary>
    /// Tag candidate counts per finding after the first scan
    /// </summary>
    public IReadOnlyDictionary<Guid, int> FirstCandidateCounts { get; init; } = new Dictionary<Guid, int>();

    /// <summary>
    /// Findings after the second scan
    /// </summary>
    public List<UpdateFinding> Findings { get; init; } = [];

    /// <summary>
    /// Tag candidate counts per finding after the second scan
    /// </summary>
    public IReadOnlyDictionary<Guid, int> CandidateCounts { get; init; } = new Dictionary<Guid, int>();

    /// <summary>
    /// Id of the derived finding of the first scan
    /// </summary>
    public Guid FirstDerivedFindingId => FirstFindings.Single(entity => entity.Type == UpdateFindingType.DerivedBaseRuntimeUpdate).Id;

    #endregion // Properties
}