using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DockerUpdateGuard.Telemetry;

/// <summary>
/// Shared telemetry primitives for DockerUpdateGuard
/// </summary>
public static class DockerUpdateGuardTelemetry
{
    #region Const fields

    /// <summary>
    /// Default service name for telemetry resources
    /// </summary>
    public const string DefaultServiceName = "DockerUpdateGuard";

    /// <summary>
    /// Activity source name for custom traces
    /// </summary>
    public const string ActivitySourceName = "DockerUpdateGuard";

    /// <summary>
    /// Meter name for custom metrics
    /// </summary>
    public const string MeterName = "DockerUpdateGuard";

    /// <summary>
    /// Logger category prefix for shared telemetry logging
    /// </summary>
    public const string LoggerCategoryName = "DockerUpdateGuard";

    #endregion // Const fields

    #region Properties

    /// <summary>
    /// Shared activity source
    /// </summary>
    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName);

    /// <summary>
    /// Shared meter
    /// </summary>
    public static Meter Meter { get; } = new(MeterName);

    #endregion // Properties
}