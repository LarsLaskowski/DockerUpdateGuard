namespace DockerUpdateGuard.Telemetry;

/// <summary>
/// Activity and metric tag names used across DockerUpdateGuard telemetry
/// </summary>
public static class TelemetryTagNames
{
    #region Const fields

    /// <summary>
    /// Image reference tag
    /// </summary>
    public const string ImageReference = "docker.image.reference";

    /// <summary>
    /// Docker instance name tag
    /// </summary>
    public const string DockerInstanceName = "docker.instance.name";

    /// <summary>
    /// Scan type tag
    /// </summary>
    public const string ScanType = "scan.type";

    /// <summary>
    /// Result status tag
    /// </summary>
    public const string ResultStatus = "result.status";

    /// <summary>
    /// Action type tag
    /// </summary>
    public const string ActionType = "action.type";

    /// <summary>
    /// Error class tag
    /// </summary>
    public const string ErrorClass = "error.class";

    /// <summary>
    /// Scan identifier tag
    /// </summary>
    public const string ScanId = "scan.id";

    #endregion // Const fields
}