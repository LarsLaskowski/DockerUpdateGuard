using DockerUpdateGuard.Data.Entities;

namespace DockerUpdateGuard.Images.Data;

/// <summary>
/// Subject of a runtime container update finding
/// </summary>
public class RuntimeFindingContext
{
    #region Properties

    /// <summary>
    /// Owning scan run
    /// </summary>
    public ScanRun ScanRun { get; set; } = null!;

    /// <summary>
    /// Container snapshot the finding belongs to
    /// </summary>
    public ContainerSnapshot Snapshot { get; set; } = null!;

    /// <summary>
    /// Image version the finding is reported for
    /// </summary>
    public ImageVersion SubjectImageVersion { get; set; } = null!;

    /// <summary>
    /// Name of the owning Docker instance
    /// </summary>
    public string DockerInstanceName { get; set; } = string.Empty;

    /// <summary>
    /// True when the image belongs to an observed own image
    /// </summary>
    public bool IsOwnImage { get; set; }

    #endregion // Properties
}