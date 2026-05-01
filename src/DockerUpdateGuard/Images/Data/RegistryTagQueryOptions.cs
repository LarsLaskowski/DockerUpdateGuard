namespace DockerUpdateGuard.Images.Data;

/// <summary>
/// Query options for bounded registry tag scans
/// </summary>
public class RegistryTagQueryOptions
{
    #region Properties

    /// <summary>
    /// Current image digest
    /// </summary>
    public string? CurrentDigest { get; init; }

    /// <summary>
    /// Current image tag
    /// </summary>
    public string? CurrentTag { get; init; }

    /// <summary>
    /// Maximum number of tags to inspect
    /// </summary>
    public int MaximumTags { get; init; } = 250;

    /// <summary>
    /// Minimum concrete version tag to consider
    /// </summary>
    public string? MinimumVersionTag { get; init; }

    /// <summary>
    /// Version-line tag used to constrain candidates to the same variant family
    /// </summary>
    public string? VersionLineTag { get; init; }

    /// <summary>
    /// Oldest relevant publish timestamp
    /// </summary>
    public DateTimeOffset? PublishedSinceUtc { get; init; }

    #endregion // Properties
}