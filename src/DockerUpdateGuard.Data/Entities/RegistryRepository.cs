namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Normalized repository in a registry
/// </summary>
public class RegistryRepository
{
    #region Properties

    /// <summary>
    /// Entity identifier
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Registry host or name
    /// </summary>
    public string Registry { get; set; } = string.Empty;

    /// <summary>
    /// Repository path
    /// </summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Normalized image versions
    /// </summary>
    public ICollection<ImageVersion> ImageVersions { get; } = [];

    #endregion // Properties
}