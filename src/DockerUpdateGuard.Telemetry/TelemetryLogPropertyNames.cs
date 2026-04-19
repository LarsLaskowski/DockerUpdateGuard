namespace DockerUpdateGuard.Telemetry;

/// <summary>
/// Log property names shared across DockerUpdateGuard structured logging
/// </summary>
public static class TelemetryLogPropertyNames
{
    #region Const fields

    /// <summary>
    /// Event identifier property
    /// </summary>
    public const string EventId = "event.id";

    /// <summary>
    /// Event name property
    /// </summary>
    public const string EventName = "event.name";

    /// <summary>
    /// Image reference property
    /// </summary>
    public const string ImageReference = TelemetryTagNames.ImageReference;

    /// <summary>
    /// Docker instance name property
    /// </summary>
    public const string DockerInstanceName = TelemetryTagNames.DockerInstanceName;

    /// <summary>
    /// Scan type property
    /// </summary>
    public const string ScanType = TelemetryTagNames.ScanType;

    /// <summary>
    /// Result status property
    /// </summary>
    public const string ResultStatus = TelemetryTagNames.ResultStatus;

    /// <summary>
    /// Action type property
    /// </summary>
    public const string ActionType = TelemetryTagNames.ActionType;

    /// <summary>
    /// Error class property
    /// </summary>
    public const string ErrorClass = TelemetryTagNames.ErrorClass;

    /// <summary>
    /// Scan identifier property
    /// </summary>
    public const string ScanId = TelemetryTagNames.ScanId;

    #endregion // Const fields
}