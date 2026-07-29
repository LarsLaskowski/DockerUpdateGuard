using DockerUpdateGuard.Images;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ScheduledBackgroundService"/>
/// </summary>
[TestClass]
public class ScheduledBackgroundServiceTests
{
    #region Methods

    /// <summary>
    /// Verify the startup step runs once before the first scheduled execution
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ScheduledBackgroundServiceExecuteAsyncRunsStartupStepBeforeFirstExecutionAsync()
    {
        var logger = new TestLogger<ScheduledBackgroundService>();

        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            var service = new TestScheduledBackgroundService(logger,
                                                             TimeSpan.FromMinutes(5),
                                                             null);
            var runTask = service.RunAsync(cancellationTokenSource.Token);

            await service.ExecutionSignal.ConfigureAwait(false);
            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            await runTask.ConfigureAwait(false);

            Assert.AreEqual(1,
                            service.StartupExecutionCount,
                            "The scheduled loop must run the startup step exactly once");
            Assert.IsTrue(service.StartupRanBeforeExecution, "The startup step must run before the first scheduled execution");
            Assert.IsGreaterThanOrEqualTo(1,
                                          service.ExecutionCount,
                                          "The scheduled loop must still execute the background operation after the startup step");
            Assert.DoesNotContain(entry => entry.EventId.Id == 2004, logger.Entries, "A successful startup step must not be logged as a failure");
        }
    }

    /// <summary>
    /// Verify a failing startup step is logged and does not stop the scheduled loop
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ScheduledBackgroundServiceExecuteAsyncStartupFailureIsLoggedAndLoopContinuesAsync()
    {
        var logger = new TestLogger<ScheduledBackgroundService>();

        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            var service = new TestScheduledBackgroundService(logger,
                                                             TimeSpan.FromMinutes(5),
                                                             new InvalidOperationException("startup boom"));
            var runTask = service.RunAsync(cancellationTokenSource.Token);

            await service.ExecutionSignal.ConfigureAwait(false);
            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            await runTask.ConfigureAwait(false);

            Assert.Contains(entry => entry.EventId.Id == 2004
                                     && entry.Exception is InvalidOperationException,
                            logger.Entries,
                            "A failing startup step must be logged as a background service failure");
            Assert.IsGreaterThanOrEqualTo(1,
                                          service.ExecutionCount,
                                          "A failing startup step must not stop the scheduled loop");
        }
    }

    /// <summary>
    /// Verify a startup step cancelled during shutdown is not logged as a failure
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ScheduledBackgroundServiceExecuteAsyncStartupCancellationDuringShutdownIsNotLoggedAsync()
    {
        var logger = new TestLogger<ScheduledBackgroundService>();

        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);

            var service = new TestScheduledBackgroundService(logger,
                                                             TimeSpan.FromMinutes(5),
                                                             null);

            await service.RunAsync(cancellationTokenSource.Token)
                         .ConfigureAwait(false);

            Assert.AreEqual(0,
                            service.StartupExecutionCount,
                            "A startup step cancelled during shutdown must not complete");
            Assert.DoesNotContain(entry => entry.EventId.Id == 2004, logger.Entries, "Cancelling the startup step during shutdown must not be logged as a failure");
        }
    }

    #endregion // Methods
}