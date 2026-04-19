namespace DockerUpdateGuard.Telemetry;

/// <summary>
/// Activity names used across DockerUpdateGuard telemetry
/// </summary>
public static class TelemetryActivityNames
{
    #region Const fields

    /// <summary>
    /// Scan lifecycle activity
    /// </summary>
    public const string ScanRun = "scan.run";

    /// <summary>
    /// Docker Hub request activity
    /// </summary>
    public const string DockerHubRequest = "dockerhub.request";

    /// <summary>
    /// Docker Engine request activity
    /// </summary>
    public const string DockerEngineRequest = "dockerengine.request";

    /// <summary>
    /// CVE provider request activity
    /// </summary>
    public const string CveProviderRequest = "cve.request";

    /// <summary>
    /// Portainer request activity
    /// </summary>
    public const string PortainerRequest = "portainer.request";

    /// <summary>
    /// Portainer action activity
    /// </summary>
    public const string PortainerAction = "portainer.action";

    /// <summary>
    /// Persistence operation activity
    /// </summary>
    public const string PersistenceOperation = "persistence.operation";

    #endregion // Const fields
}