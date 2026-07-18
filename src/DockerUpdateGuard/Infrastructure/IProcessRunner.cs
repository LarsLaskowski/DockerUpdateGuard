namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// External process runner contract
/// </summary>
public interface IProcessRunner
{
    #region Methods

    /// <summary>
    /// Run an external process and capture its output
    /// </summary>
    /// <param name="fileName">Executable file name or path</param>
    /// <param name="arguments">Process arguments</param>
    /// <param name="timeout">Maximum execution time before the process is terminated</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Process execution result</returns>
    Task<ProcessExecutionResult> RunAsync(string fileName,
                                          IReadOnlyList<string> arguments,
                                          TimeSpan timeout,
                                          CancellationToken cancellationToken = default);

    #endregion // Methods
}