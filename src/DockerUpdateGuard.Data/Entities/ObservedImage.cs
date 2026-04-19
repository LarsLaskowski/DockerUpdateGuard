namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Observed target image configured by the user
/// </summary>
public class ObservedImage
{
    #region Properties

    /// <summary>
    /// Entity identifier
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Display name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Indicates whether monitoring is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Registration source
    /// </summary>
    public RegistrationSource Source { get; set; } = RegistrationSource.Manual;

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Current normalized image version identifier
    /// </summary>
    public Guid CurrentImageVersionId { get; set; }

    /// <summary>
    /// Current normalized image version
    /// </summary>
    public ImageVersion CurrentImageVersion { get; set; } = null!;

    /// <summary>
    /// Related scan runs
    /// </summary>
    public ICollection<ScanRun> ScanRuns { get; } = [];

    /// <summary>
    /// Related update findings
    /// </summary>
    public ICollection<UpdateFinding> UpdateFindings { get; } = [];

    #endregion // Properties
}