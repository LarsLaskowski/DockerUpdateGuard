namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Persisted manual tag selection for a runtime container
/// </summary>
public class RuntimeContainerTagSelection
{
    #region Properties

    /// <summary>
    /// Entity identifier
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Related Docker instance identifier
    /// </summary>
    public Guid DockerInstanceId { get; set; }

    /// <summary>
    /// Related registry repository identifier
    /// </summary>
    public Guid RegistryRepositoryId { get; set; }

    /// <summary>
    /// Runtime container identifier
    /// </summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// Selected tag
    /// </summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// Optional selected digest
    /// </summary>
    public string? Digest { get; set; }

    /// <summary>
    /// Selection timestamp
    /// </summary>
    public DateTimeOffset SelectedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Related Docker instance
    /// </summary>
    public DockerInstance DockerInstance { get; set; } = null!;

    /// <summary>
    /// Related registry repository
    /// </summary>
    public RegistryRepository RegistryRepository { get; set; } = null!;

    #endregion // Properties
}