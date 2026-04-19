namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Registration source for configured entities
/// </summary>
public enum RegistrationSource
{
    /// <summary>
    /// No registration source set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Manually registered
    /// </summary>
    Manual = 1,

    /// <summary>
    /// Loaded from configuration
    /// </summary>
    ConfigurationFile = 2,

    /// <summary>
    /// Discovered from an external source
    /// </summary>
    Discovery = 3,

    /// <summary>
    /// Imported from another system
    /// </summary>
    Import = 4
}