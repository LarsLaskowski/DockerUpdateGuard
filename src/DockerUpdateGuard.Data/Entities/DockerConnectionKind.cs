namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Connection kind for a Docker instance
/// </summary>
public enum DockerConnectionKind
{
    /// <summary>
    /// No connection kind set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// HTTP endpoint
    /// </summary>
    Http = 1,

    /// <summary>
    /// HTTPS endpoint
    /// </summary>
    Https = 2,

    /// <summary>
    /// Windows named pipe endpoint
    /// </summary>
    NamedPipe = 3
}