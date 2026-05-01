using DockerUpdateGuard.Images.Enums;

namespace DockerUpdateGuard.Images.Data;

/// <summary>
/// Internal runtime candidate
/// </summary>
internal sealed class RuntimeCandidate
{
    #region Properties

    /// <summary>
    /// Runtime kind
    /// </summary>
    public DerivedBaseRuntimeKind Kind { get; set; }

    /// <summary>
    /// Runtime version
    /// </summary>
    public Version? Version { get; set; }

    /// <summary>
    /// Channel version
    /// </summary>
    public string? ChannelVersion { get; set; }

    /// <summary>
    /// Detection source
    /// </summary>
    public DerivedBaseRuntimeDetectionSource Source { get; set; }

    /// <summary>
    /// Source priority
    /// </summary>
    public int SourcePriority { get; set; }

    /// <summary>
    /// Encounter order
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Whether the candidate has an exact version
    /// </summary>
    public bool HasExactVersion { get; set; }

    /// <summary>
    /// Evidence string
    /// </summary>
    public string? Evidence { get; set; }

    #endregion // Properties
}