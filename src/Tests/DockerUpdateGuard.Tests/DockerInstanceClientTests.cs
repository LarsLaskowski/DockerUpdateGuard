using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Docker;
using DockerUpdateGuard.Infrastructure;

using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DockerInstanceClient"/>
/// </summary>
[TestClass]
public class DockerInstanceClientTests
{
    #region Methods

    /// <summary>
    /// Verify disabled Docker instances return a not-configured result and log the skip decision
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientDiscoverContainersAsyncWhenInstanceDisabledReturnsNotConfiguredAndLogsSkipAsync()
    {
        var logger = new TestLogger<DockerInstanceClient>();
        var client = new DockerInstanceClient(logger);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "https://docker.example.test",
                                  Enabled = false,
                              };

        var result = await client.DiscoverContainersAsync(instanceOptions, CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.NotConfigured,
                        result.Status,
                        "Disabled Docker instances must return a not-configured result");
        Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 3100
                                                  && entry.LogLevel == LogLevel.Information
                                                  && entry.Message.Contains("Production", StringComparison.Ordinal)),
                      "Disabled Docker instances must log the skip decision with the configured instance name");
    }

    /// <summary>
    /// Verify unsupported Docker endpoints return an unsupported result and log the warning
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceClientDiscoverContainersAsyncWithUnsupportedEndpointReturnsUnsupportedAndLogsWarningAsync()
    {
        var logger = new TestLogger<DockerInstanceClient>();
        var client = new DockerInstanceClient(logger);
        var instanceOptions = new DockerInstanceOptions
                              {
                                  Name = "Production",
                                  BaseUrl = "ssh://docker.example.test",
                                  Enabled = true,
                              };

        var result = await client.DiscoverContainersAsync(instanceOptions, CancellationToken.None)
                                 .ConfigureAwait(false);

        Assert.AreEqual(ExternalOperationStatus.Unsupported,
                        result.Status,
                        "Unsupported Docker endpoints must return an unsupported result");
        Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 3101
                                                  && entry.LogLevel == LogLevel.Warning
                                                  && entry.Message.Contains("ssh://docker.example.test", StringComparison.Ordinal)),
                      "Unsupported Docker endpoints must log the rejected endpoint value as a warning");
    }

    #endregion // Methods
}