using System.Diagnostics;

namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Default process runner backed by <see cref="Process"/>
/// </summary>
public class ProcessRunner : IProcessRunner
{
    #region IProcessRunner

    /// <inheritdoc/>
    public async Task<ProcessExecutionResult> RunAsync(string fileName,
                                                       IReadOnlyList<string> arguments,
                                                       TimeSpan timeout,
                                                       CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
                        {
                            FileName = fileName,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using (var process = new Process())
        {
            process.StartInfo = startInfo;
            process.Start();

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var timedOut = false;

            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutSource.CancelAfter(timeout);

                try
                {
                    await process.WaitForExitAsync(timeoutSource.Token)
                                 .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false)
                {
                    timedOut = true;

                    process.Kill(true);
                    await process.WaitForExitAsync(CancellationToken.None)
                                 .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(true);

                    throw;
                }
            }

            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);

            return new ProcessExecutionResult
                   {
                       ExitCode = process.ExitCode,
                       StandardOutput = standardOutput,
                       StandardError = standardError,
                       TimedOut = timedOut,
                   };
        }
    }

    #endregion // IProcessRunner
}