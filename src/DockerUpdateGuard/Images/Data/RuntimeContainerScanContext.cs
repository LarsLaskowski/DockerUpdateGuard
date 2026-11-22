using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Images.Data;

/// <summary>
/// Per-instance context of a runtime container scan
/// </summary>
internal sealed class RuntimeContainerScanContext
{
    #region Properties

    /// <summary>
    /// Docker instance being scanned
    /// </summary>
    public DockerInstance DockerInstance { get; init; } = null!;

    /// <summary>
    /// Configured Docker instance options
    /// </summary>
    public DockerInstanceOptions ConfiguredInstance { get; init; } = null!;

    /// <summary>
    /// Scan run of the current pass
    /// </summary>
    public ScanRun ScanRun { get; init; } = null!;

    /// <summary>
    /// Repository keys of the application's own images
    /// </summary>
    public IReadOnlySet<string> OwnRepositoryKeys { get; init; } = null!;

    #endregion // Properties
}