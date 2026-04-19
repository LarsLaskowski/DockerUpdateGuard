namespace DockerUpdateGuard.DockerHub;

/// <summary>
/// Docker Hub repository metadata
/// </summary>
public class DockerHubRepositoryData
{
    #region Properties

    /// <summary>
    /// Registry name
    /// </summary>
    public string Registry { get; set; } = string.Empty;

    /// <summary>
    /// Repository path
    /// </summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// Optional repository description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional last update timestamp
    /// </summary>
    public DateTimeOffset? LastUpdatedAtUtc { get; set; }

    #endregion // Properties
}