namespace DockerUpdateGuard.DockerHub;

/// <summary>
/// The host names that identify Docker Hub as the configured registry
/// </summary>
public static class DockerHubHosts
{
    #region Properties

    /// <summary>
    /// Host names of Docker Hub, compared without regard to case
    /// </summary>
    public static IReadOnlyList<string> Names => throw new InvalidOperationException("Skeleton: not implemented");

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Checks whether a host equals one of the Docker Hub host names, ignoring case and without trimming
    /// </summary>
    /// <param name="host">Host to check</param>
    /// <returns>True when the host is a Docker Hub host name</returns>
    public static bool IsDockerHubHost(string? host)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Methods
}