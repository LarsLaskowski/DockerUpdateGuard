using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Portainer;

using Microsoft.EntityFrameworkCore;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ContainerActionRunReconciler"/>
/// </summary>
[TestClass]
public class ContainerActionRunReconcilerTests
{
    #region Constants

    /// <summary>
    /// Message of a run interrupted by an application stop (E10)
    /// </summary>
    private const string InterruptedMessage = "Interrupted: the application stopped before the action completed";

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Current time of the clock
    /// </summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Completion time of the runs that were already terminal
    /// </summary>
    private static readonly DateTimeOffset _completedAtUtc = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Test context providing information about and functionality for the current test run
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify every Requested or Running run becomes Failed with the interrupted message and the current time, and terminal runs stay unchanged
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC9")]
    public async Task ContainerActionRunReconcilerReconcileInterruptedRunsAsyncFailsOpenRunsAndKeepsTerminalOnesAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var clock = Substitute.For<TimeProvider>();

        clock.GetUtcNow().Returns(_now);

        await SeedRunsAsync(databaseName).ConfigureAwait(false);

        var dbContext = CreateDbContext(databaseName);
        int reconciled;

        await using (dbContext.ConfigureAwait(false))
        {
            reconciled = await ContainerActionRunReconciler.ReconcileInterruptedRunsAsync(dbContext, clock, TestContext.CancellationToken)
                                                           .ConfigureAwait(false);
        }

        var runs = await LoadRunsByNameAsync(databaseName).ConfigureAwait(false);

        Assert.AreEqual(2, reconciled, "Exactly the Requested and the Running run must be reconciled");
        AssertRun(runs["requested"], ContainerActionStatus.Failed, InterruptedMessage, _now, "A Requested run");
        AssertRun(runs["running"], ContainerActionStatus.Failed, InterruptedMessage, _now, "A Running run");
        AssertRun(runs["succeeded"], ContainerActionStatus.Succeeded, "done", _completedAtUtc, "A Succeeded run");
        AssertRun(runs["failed"], ContainerActionStatus.Failed, "failed before", _completedAtUtc, "A Failed run");
        AssertRun(runs["cancelled"], ContainerActionStatus.Cancelled, "cancelled before", _completedAtUtc, "A Cancelled run");
    }

    /// <summary>
    /// Assert the state of a run
    /// </summary>
    /// <param name="run">Run</param>
    /// <param name="status">Expected status</param>
    /// <param name="message">Expected message</param>
    /// <param name="completedAtUtc">Expected completion time</param>
    /// <param name="description">Description of the run in the assertion messages</param>
    private static void AssertRun(ContainerActionRun run,
                                  ContainerActionStatus status,
                                  string message,
                                  DateTimeOffset completedAtUtc,
                                  string description)
    {
        Assert.AreEqual(status, run.Status, $"{description} must end {status}");
        Assert.AreEqual(message, run.ErrorMessage, $"{description} must carry the message '{message}'");
        Assert.AreEqual(completedAtUtc, run.CompletedAtUtc, $"{description} must be completed at {completedAtUtc:O}");
    }

    /// <summary>
    /// Create a context on the named in-memory database
    /// </summary>
    /// <param name="databaseName">In-memory database name</param>
    /// <returns>Context</returns>
    private static DockerUpdateGuardDbContext CreateDbContext(string databaseName)
    {
        return new DockerUpdateGuardDbContext(new DbContextOptionsBuilder<DockerUpdateGuardDbContext>().UseInMemoryDatabase(databaseName)
                                                                                                       .Options);
    }

    /// <summary>
    /// Create a run of the instance, named by its resource name
    /// </summary>
    /// <param name="instance">Docker instance</param>
    /// <param name="name">Resource name identifying the run</param>
    /// <param name="status">Status</param>
    /// <param name="message">Message; null for an open run</param>
    /// <returns>Run</returns>
    private static ContainerActionRun CreateRun(DockerInstance instance, string name, ContainerActionStatus status, string? message)
    {
        return new ContainerActionRun
               {
                   DockerInstance = instance,
                   ActionType = ContainerActionType.RestartContainer,
                   ResourceType = PortainerResourceType.Container,
                   ResourceName = name,
                   RequestedBy = "UI",
                   Status = status,
                   ErrorMessage = message,
                   RequestedAtUtc = _completedAtUtc.AddMinutes(-5),
                   CompletedAtUtc = message is null ? null : _completedAtUtc,
               };
    }

    /// <summary>
    /// Load the runs by their resource name
    /// </summary>
    /// <param name="databaseName">In-memory database name</param>
    /// <returns>Runs by resource name</returns>
    private async Task<Dictionary<string, ContainerActionRun>> LoadRunsByNameAsync(string databaseName)
    {
        var dbContext = CreateDbContext(databaseName);

        await using (dbContext.ConfigureAwait(false))
        {
            return await dbContext.ContainerActionRuns.AsNoTracking()
                                                      .ToDictionaryAsync(run => run.ResourceName, TestContext.CancellationToken)
                                                      .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Seed one run of every status
    /// </summary>
    /// <param name="databaseName">In-memory database name</param>
    /// <returns>Task</returns>
    private async Task SeedRunsAsync(string databaseName)
    {
        var dbContext = CreateDbContext(databaseName);

        await using (dbContext.ConfigureAwait(false))
        {
            var instance = new DockerInstance
                           {
                               Name = "Production",
                               EndpointUri = "https://docker.example.test",
                               ConnectionKind = DockerConnectionKind.Https,
                           };

            dbContext.DockerInstances.Add(instance);
            dbContext.ContainerActionRuns.AddRange(CreateRun(instance, "requested", ContainerActionStatus.Requested, null),
                                                   CreateRun(instance, "running", ContainerActionStatus.Running, null),
                                                   CreateRun(instance, "succeeded", ContainerActionStatus.Succeeded, "done"),
                                                   CreateRun(instance, "failed", ContainerActionStatus.Failed, "failed before"),
                                                   CreateRun(instance, "cancelled", ContainerActionStatus.Cancelled, "cancelled before"));

            await dbContext.SaveChangesAsync(TestContext.CancellationToken)
                           .ConfigureAwait(false);
        }
    }

    #endregion // Methods
}