namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Result of an external process execution
/// </summary>
public sealed record ProcessExecutionResult
{
    #region Properties

    /// <summary>
    /// Process exit code
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// Captured standard output
    /// </summary>
    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>
    /// Captured standard error
    /// </summary>
    public string StandardError { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether the process was terminated because the timeout elapsed
    /// </summary>
    public bool TimedOut { get; init; }

    #endregion // Properties
}