namespace DockerUpdateGuard.Telemetry;

/// <summary>
/// Configuration for OpenTelemetry registration
/// </summary>
public class TelemetryOptions
{
    #region Const fields

    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "Telemetry";

    #endregion // Const fields

    #region Properties

    /// <summary>
    /// Service name used in telemetry resources
    /// </summary>
    public string ServiceName { get; set; } = DockerUpdateGuardTelemetry.DefaultServiceName;

    /// <summary>
    /// OTLP endpoint
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// Logical deployment instance name used in telemetry resources
    /// </summary>
    public string? Instance { get; set; }

    /// <summary>
    /// Enable logging pipeline
    /// </summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// Enable metrics pipeline
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Enable tracing pipeline
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    #endregion // Properties
}