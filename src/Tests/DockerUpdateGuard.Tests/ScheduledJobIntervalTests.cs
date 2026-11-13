using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for the interval the scheduled jobs read from their options on every cycle
/// </summary>
[TestClass]
public class ScheduledJobIntervalTests
{
    #region Constants

    /// <summary>
    /// Interval in minutes configured after the job was created, different from every default
    /// </summary>
    private const int ConfiguredMinutes = 41;

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Verify the Docker Hub account discovery job waits the interval currently configured
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerHubAccountImageDiscoveryBackgroundServiceExecuteAsyncWaitsConfiguredIntervalAsync()
    {
        await AssertWaitsConfiguredIntervalAsync((options, scopeFactory, clock) => new DockerHubAccountImageDiscoveryBackgroundService(NullLogger<DockerHubAccountImageDiscoveryBackgroundService>.Instance,
                                                                                                                                       options,
                                                                                                                                       scopeFactory,
                                                                                                                                       clock),
                                                 scanning => scanning.DockerHubAccountDiscoveryIntervalMinutes = ConfiguredMinutes).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify the instance discovery job waits the interval currently configured
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DockerInstanceDiscoveryBackgroundServiceExecuteAsyncWaitsConfiguredIntervalAsync()
    {
        await AssertWaitsConfiguredIntervalAsync((options, scopeFactory, clock) => new DockerInstanceDiscoveryBackgroundService(NullLogger<DockerInstanceDiscoveryBackgroundService>.Instance,
                                                                                                                                options,
                                                                                                                                scopeFactory,
                                                                                                                                clock),
                                                 scanning => scanning.DiscoveryIntervalMinutes = ConfiguredMinutes).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify the resource statistics job waits the interval currently configured
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ResourceStatisticsRefreshBackgroundServiceExecuteAsyncWaitsConfiguredIntervalAsync()
    {
        await AssertWaitsConfiguredIntervalAsync((options, scopeFactory, clock) => new ResourceStatisticsRefreshBackgroundService(NullLogger<ResourceStatisticsRefreshBackgroundService>.Instance,
                                                                                                                                  options,
                                                                                                                                  scopeFactory,
                                                                                                                                  clock),
                                                 scanning => scanning.ResourceStatisticsIntervalMinutes = ConfiguredMinutes).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify the runtime container refresh job waits the interval currently configured
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainerRefreshBackgroundServiceExecuteAsyncWaitsConfiguredIntervalAsync()
    {
        await AssertWaitsConfiguredIntervalAsync((options, scopeFactory, clock) => new RuntimeContainerRefreshBackgroundService(NullLogger<RuntimeContainerRefreshBackgroundService>.Instance,
                                                                                                                                options,
                                                                                                                                scopeFactory,
                                                                                                                                clock),
                                                 scanning => scanning.RuntimeImageUpdateScanIntervalMinutes = ConfiguredMinutes).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify the vulnerability refresh job waits the interval currently configured
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task VulnerabilityRefreshBackgroundServiceExecuteAsyncWaitsConfiguredIntervalAsync()
    {
        await AssertWaitsConfiguredIntervalAsync((options, scopeFactory, clock) => new VulnerabilityRefreshBackgroundService(NullLogger<VulnerabilityRefreshBackgroundService>.Instance,
                                                                                                                             options,
                                                                                                                             scopeFactory,
                                                                                                                             clock),
                                                 scanning => scanning.VulnerabilityRefreshIntervalMinutes = ConfiguredMinutes).ConfigureAwait(false);
    }

    /// <summary>
    /// Create the job with the default interval, change the options, run it and assert its first wait is the changed interval
    /// </summary>
    /// <param name="createJob">Factory of the job under test</param>
    /// <param name="configure">Sets the interval of the job to the configured value</param>
    /// <returns>Task</returns>
    private static async Task AssertWaitsConfiguredIntervalAsync(Func<IOptionsMonitor<DockerUpdateGuardOptions>, IServiceScopeFactory, TimeProvider, BackgroundService> createJob,
                                                                 Action<ScanningOptions> configure)
    {
        var options = new DockerUpdateGuardOptions();
        var optionsMonitor = Substitute.For<IOptionsMonitor<DockerUpdateGuardOptions>>();
        var clock = new WaitRecordingTimeProvider();

        optionsMonitor.CurrentValue.Returns(options);

        using var job = createJob(optionsMonitor, Substitute.For<IServiceScopeFactory>(), clock.TimeProvider);

        configure(options.Scanning);

        await job.StartAsync(CancellationToken.None).ConfigureAwait(false);

        var waitStarted = await clock.WaitForNextWaitAsync(new TaskCompletionSource().Task).ConfigureAwait(false);

        await job.StopAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(waitStarted, "The job must start its first wait");
        Assert.AreEqual(TimeSpan.FromMinutes(ConfiguredMinutes),
                        clock.Waits[0],
                        "The job must wait the interval read from the current options, not the interval it was created with");
    }

    #endregion // Methods
}