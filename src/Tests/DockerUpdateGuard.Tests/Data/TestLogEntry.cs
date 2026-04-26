using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests.Data;

/// <summary>
/// Captured log entry details
/// </summary>
internal sealed class TestLogEntry
{
    #region Properties

    /// <summary>
    /// Event identifier
    /// </summary>
    public EventId EventId { get; init; }

    /// <summary>
    /// Logged exception
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Log level
    /// </summary>
    public LogLevel LogLevel { get; init; }

    /// <summary>
    /// Formatted log message
    /// </summary>
    public string Message { get; init; } = string.Empty;

    #endregion // Properties
}