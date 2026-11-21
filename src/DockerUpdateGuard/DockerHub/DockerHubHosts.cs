using System.Collections.ObjectModel;

namespace DockerUpdateGuard.DockerHub;

/// <summary>
/// The host names that identify Docker Hub as the configured registry
/// </summary>
public static class DockerHubHosts
{
    #region Fields

    /// <summary>
    /// Read-only list of the Docker Hub host names, built once
    /// </summary>
    private static readonly ReadOnlyCollection<string> _names = Array.AsReadOnly(["docker.io", "index.docker.io", "registry-1.docker.io", "hub.docker.com", "docker.com", "www.docker.com"]);

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Host names of Docker Hub, compared without regard to case
    /// </summary>
    public static IReadOnlyList<string> Names => _names;

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Checks whether a host equals one of the Docker Hub host names, ignoring case and without trimming
    /// </summary>
    /// <param name="host">Host to check</param>
    /// <returns>True when the host is a Docker Hub host name</returns>
    public static bool IsDockerHubHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return _names.Any(name => string.Equals(host, name, StringComparison.OrdinalIgnoreCase));
    }

    #endregion // Methods
}