using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ProcessRunner"/>
/// </summary>
[TestClass]
public class ProcessRunnerTests
{
    #region Properties

    /// <summary>
    /// Test context providing information about and functionality for the current test run
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify the given environment variables are added to the inherited environment
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task ProcessRunnerRunAsyncAddsEnvironmentVariablesToInheritedEnvironmentAsync()
    {
        var runner = new ProcessRunner();
        var environmentVariables = new Dictionary<string, string>
                                   {
                                       ["DUG_PROCESS_RUNNER_TEST"] = "value-from-runner",
                                   };

        var result = await runner.RunAsync("env",
                                           [],
                                           environmentVariables,
                                           TimeSpan.FromSeconds(30),
                                           TestContext.CancellationToken)
                                 .ConfigureAwait(false);

        var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                         .Select(line => line.TrimEnd('\r'))
                                         .ToList();

        Assert.AreEqual(0, result.ExitCode, "The env process must exit successfully");
        Assert.Contains("DUG_PROCESS_RUNNER_TEST=value-from-runner",
                        lines,
                        "The child must see the given variable with its value");
        Assert.Contains(line => line.StartsWith("PATH=", StringComparison.Ordinal),
                        lines,
                        "The child must still see inherited variables such as PATH");
    }

    /// <summary>
    /// Verify a given environment variable replaces the inherited variable of the same name
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task ProcessRunnerRunAsyncReplacesInheritedEnvironmentVariableAsync()
    {
        Assert.IsFalse(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HOME")),
                       "The test process must have a non-empty HOME that the child would inherit");

        var runner = new ProcessRunner();
        var environmentVariables = new Dictionary<string, string>
                                   {
                                       ["HOME"] = "/dug-overridden",
                                   };

        var result = await runner.RunAsync("env",
                                           [],
                                           environmentVariables,
                                           TimeSpan.FromSeconds(30),
                                           TestContext.CancellationToken)
                                 .ConfigureAwait(false);

        var homeLines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                             .Select(line => line.TrimEnd('\r'))
                                             .Where(line => line.StartsWith("HOME=", StringComparison.Ordinal))
                                             .ToList();

        Assert.AreEqual(0, result.ExitCode, "The env process must exit successfully");
        Assert.HasCount(1, homeLines, "The child must see exactly one HOME variable");
        Assert.AreEqual("HOME=/dug-overridden", homeLines[0], "The given value must replace the inherited HOME");
    }

    /// <summary>
    /// Verify a null environment dictionary is rejected before a process starts
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ProcessRunnerRunAsyncThrowsForNullEnvironmentVariablesAsync()
    {
        var runner = new ProcessRunner();

        var exception = await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => runner.RunAsync("env",
                                                                                                     [],
                                                                                                     null!,
                                                                                                     TimeSpan.FromSeconds(30),
                                                                                                     TestContext.CancellationToken))
                                    .ConfigureAwait(false);

        Assert.AreEqual("environmentVariables",
                        exception.ParamName,
                        "The exception must name the null parameter");
    }

    #endregion // Methods
}