namespace DockerUpdateGuard.Images.Data;

/// <summary>
/// Immutable lookup parameters shared by all pages of a registry tag listing
/// </summary>
public class RegistryTagLookupContext
{
    #region Properties

    /// <summary>
    /// Normalized registry host
    /// </summary>
    public string Registry { get; set; } = string.Empty;

    /// <summary>
    /// Repository name
    /// </summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// Preferred operating system
    /// </summary>
    public string? OperatingSystem { get; set; }

    /// <summary>
    /// Preferred architecture
    /// </summary>
    public string? Architecture { get; set; }

    /// <summary>
    /// Tag query options
    /// </summary>
    public RegistryTagQueryOptions? QueryOptions { get; set; }

    #endregion // Properties
}