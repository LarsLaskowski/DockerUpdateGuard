using DockerUpdateGuard.Images.Enums;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Describes a locally derived base runtime
/// </summary>
public class DerivedBaseRuntimeDescriptor
{
    #region Properties

    /// <summary>
    /// Runtime kind
    /// </summary>
    public DerivedBaseRuntimeKind Kind { get; set; }

    /// <summary>
    /// Detected runtime version
    /// </summary>
    public Version? RuntimeVersion { get; set; }

    /// <summary>
    /// Detected channel version
    /// </summary>
    public string? ChannelVersion { get; set; }

    /// <summary>
    /// Detection source
    /// </summary>
    public DerivedBaseRuntimeDetectionSource Source { get; set; }

    /// <summary>
    /// Evidence string
    /// </summary>
    public string? Evidence { get; set; }

    #endregion // Properties
}