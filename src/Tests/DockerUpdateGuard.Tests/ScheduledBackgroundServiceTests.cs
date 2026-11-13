using DockerUpdateGuard.Images;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ScheduledBackgroundService"/>
/// </summary>
[TestClass]
public class ScheduledBackgroundServiceTests
{
    #region Static methods

    /// <summary>
    /// Create an interval source that returns or throws the supplied steps in order and repeats the last one
    /// </summary>
    /// <param name="steps">Steps, each a <see cref="TimeSpan"/> to return or an <see cref="Exception"/> to throw</param>
    /// <returns>Interval source</returns>
    private static Func<int, TimeSpan> CreateIntervalSource(params object[] steps)
    {
        return lookupNumber =>
               {
                   var step = steps[Math.Min(lookupNumber, steps.Length - 1)];

                   if (step is Exception exception)
                   {
                       throw exception;
                   }

                   return (TimeSpan)step;
               };
    }

    #endregion // Static methods

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

    /// <summary>
    /// Verify a failing lookup before a wait is logged and the job waits its last interval read successfully
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task ScheduledBackgroundServiceExecuteAsyncIntervalLookupThrowsBeforeWaitUsesLastKnownIntervalAsync()
    {
        var logger = new TestLogger<ScheduledBackgroundService>();
        var timeProvider = new WaitRecordingTimeProvider();
        var lookupException = new InvalidOperationException("lookup boom");

        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            var service = new TestScheduledBackgroundService(logger,
                                                             timeProvider.TimeProvider,
                                                             TimeSpan.FromMinutes(5),
                                                             CreateIntervalSource(TimeSpan.FromMinutes(6), lookupException, lookupException),
                                                             null);
            var runTask = service.RunAsync(cancellationTokenSource.Token);

            var firstWaitStarted = await timeProvider.WaitForNextWaitAsync(runTask).ConfigureAwait(false);

            Assert.IsTrue(firstWaitStarted, "A failing interval lookup must not end the job's task before its first wait");

            timeProvider.ElapseLastWait();

            var secondWaitStarted = await timeProvider.WaitForNextWaitAsync(runTask).ConfigureAwait(false);

            Assert.IsTrue(secondWaitStarted, "The job must run again and wait again after the first wait elapsed");

            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            await runTask.ConfigureAwait(false);

            Assert.AreSequenceEqual(new[] { TimeSpan.FromMinutes(6), TimeSpan.FromMinutes(6) },
                                    timeProvider.Waits,
                                    "A failing lookup must make the job wait the last interval it read successfully");
            Assert.AreEqual(2,
                            service.ExecutionCount,
                            "The job must run again after the wait that follows a failing lookup");
            Assert.Contains(entry => entry.EventId.Id == 2005
                                     && entry.LogLevel == LogLevel.Warning
                                     && ReferenceEquals(entry.Exception, lookupException)
                                     && entry.Message.Contains(nameof(TestScheduledBackgroundService), StringComparison.Ordinal),
                            logger.Entries,
                            "A failing lookup must be logged as warning 2005 with the job name and the exception");
            Assert.DoesNotContain(entry => entry.EventId.Id == 2004, logger.Entries, "A failing lookup is not a failed run");
        }
    }

    /// <summary>
    /// Verify a failing lookup at the start-up log line does not end the job's task
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task ScheduledBackgroundServiceExecuteAsyncIntervalLookupThrowsAtStartupLogDoesNotFaultAsync()
    {
        var logger = new TestLogger<ScheduledBackgroundService>();
        var timeProvider = new WaitRecordingTimeProvider();
        var lookupException = new InvalidOperationException("startup lookup boom");

        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            var service = new TestScheduledBackgroundService(logger,
                                                             timeProvider.TimeProvider,
                                                             TimeSpan.FromMinutes(5),
                                                             CreateIntervalSource(lookupException, TimeSpan.FromMinutes(7)),
                                                             null);
            var runTask = service.RunAsync(cancellationTokenSource.Token);

            var waitStarted = await timeProvider.WaitForNextWaitAsync(runTask).ConfigureAwait(false);

            Assert.IsTrue(waitStarted, "A failing lookup at the start-up log line must not end the job's task");

            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            await runTask.ConfigureAwait(false);

            Assert.AreEqual(1,
                            service.ExecutionCount,
                            "The job must still run its immediate execution after a failing start-up lookup");
            Assert.Contains(entry => entry.EventId.Id == 2005
                                     && entry.LogLevel == LogLevel.Warning
                                     && ReferenceEquals(entry.Exception, lookupException),
                            logger.Entries,
                            "A failing start-up lookup must be logged as warning 2005 with the exception");
            Assert.AreSequenceEqual(new[] { TimeSpan.FromMinutes(7) },
                                    timeProvider.Waits,
                                    "The wait must use the interval of the lookup that succeeded");
        }
    }

    /// <summary>
    /// Verify the next wait uses the new interval once the lookup succeeds again
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ScheduledBackgroundServiceExecuteAsyncIntervalLookupRecoversUsesNewIntervalAsync()
    {
        var logger = new TestLogger<ScheduledBackgroundService>();
        var timeProvider = new WaitRecordingTimeProvider();

        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            var service = new TestScheduledBackgroundService(logger,
                                                             timeProvider.TimeProvider,
                                                             TimeSpan.FromMinutes(5),
                                                             CreateIntervalSource(TimeSpan.FromMinutes(6), new InvalidOperationException("reload boom"), TimeSpan.FromMinutes(9)),
                                                             null);
            var runTask = service.RunAsync(cancellationTokenSource.Token);

            var firstWaitStarted = await timeProvider.WaitForNextWaitAsync(runTask).ConfigureAwait(false);

            Assert.IsTrue(firstWaitStarted, "The job must start its first wait");

            timeProvider.ElapseLastWait();

            var secondWaitStarted = await timeProvider.WaitForNextWaitAsync(runTask).ConfigureAwait(false);

            Assert.IsTrue(secondWaitStarted, "The job must keep waiting while the lookup fails and after it recovers");

            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            await runTask.ConfigureAwait(false);

            Assert.AreSequenceEqual(new[] { TimeSpan.FromMinutes(6), TimeSpan.FromMinutes(9) },
                                    timeProvider.Waits,
                                    "The wait after a recovered lookup must use the new interval");
        }
    }

    /// <summary>
    /// Verify a job whose first lookups fail waits the initial interval it was created with
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ScheduledBackgroundServiceExecuteAsyncFirstIntervalLookupThrowsUsesInitialIntervalAsync()
    {
        var logger = new TestLogger<ScheduledBackgroundService>();
        var timeProvider = new WaitRecordingTimeProvider();
        var lookupException = new InvalidOperationException("first lookup boom");

        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            var service = new TestScheduledBackgroundService(logger,
                                                             timeProvider.TimeProvider,
                                                             TimeSpan.FromMinutes(5),
                                                             CreateIntervalSource(lookupException),
                                                             null);
            var runTask = service.RunAsync(cancellationTokenSource.Token);

            var waitStarted = await timeProvider.WaitForNextWaitAsync(runTask).ConfigureAwait(false);

            Assert.IsTrue(waitStarted, "A job whose lookups always fail must still wait");

            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            await runTask.ConfigureAwait(false);

            Assert.AreSequenceEqual(new[] { TimeSpan.FromMinutes(5) },
                                    timeProvider.Waits,
                                    "Before its first successful lookup the job must wait the initial interval");
            Assert.Contains(entry => entry.EventId.Id == 2005
                                     && entry.LogLevel == LogLevel.Warning
                                     && entry.Message.Contains('5', StringComparison.Ordinal),
                            logger.Entries,
                            "The warning must carry the initial interval as the fallback interval");
        }
    }

    /// <summary>
    /// Verify a lookup cancelled during shutdown ends the job quietly
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task ScheduledBackgroundServiceExecuteAsyncIntervalLookupCancelledDuringShutdownIsNotLoggedAsync()
    {
        var logger = new TestLogger<ScheduledBackgroundService>();
        var timeProvider = new WaitRecordingTimeProvider();

        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            var service = new TestScheduledBackgroundService(logger,
                                                             timeProvider.TimeProvider,
                                                             TimeSpan.FromMinutes(5),
                                                             _ =>
                                                             {
                                                                 cancellationTokenSource.Cancel();

                                                                 throw new OperationCanceledException(cancellationTokenSource.Token);
                                                             },
                                                             null);

            await service.RunAsync(cancellationTokenSource.Token)
                         .ConfigureAwait(false);

            Assert.DoesNotContain(entry => entry.EventId.Id == 2005, logger.Entries, "A lookup cancelled during shutdown must not be logged as a failed lookup");
            Assert.DoesNotContain(entry => entry.EventId.Id == 2004, logger.Entries, "A lookup cancelled during shutdown must not be logged as a failed run");
        }
    }

    #endregion // Methods
}