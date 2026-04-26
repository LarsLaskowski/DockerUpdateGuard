namespace DockerUpdateGuard.Images;

/// <summary>
/// Reduced registry image configuration data
/// </summary>
public class RegistryImageConfigurationData
{
    #region Properties

    /// <summary>
    /// Configured environment variables
    /// </summary>
    public IReadOnlyList<string> EnvironmentVariables { get; set; } = [];

    /// <summary>
    /// Configured labels
    /// </summary>
    public IReadOnlyDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTimeOffset? CreatedAtUtc { get; set; }

    /// <summary>
    /// Reported operating system
    /// </summary>
    public string? OperatingSystem { get; set; }

    /// <summary>
    /// Reported architecture
    /// </summary>
    public string? Architecture { get; set; }

    #endregion // Properties
}