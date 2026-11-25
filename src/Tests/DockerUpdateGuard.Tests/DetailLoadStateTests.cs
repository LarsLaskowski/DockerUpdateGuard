using DockerUpdateGuard.Components;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DetailLoadState"/>
/// </summary>
[TestClass]
public class DetailLoadStateTests
{
    #region Constants

    /// <summary>
    /// Logged action name of the loads
    /// </summary>
    private const string LoadAction = "LoadSomething";

    /// <summary>
    /// Message prefix of the loads
    /// </summary>
    private const string LoadPrefix = "Loading something failed";

    /// <summary>
    /// Message of a failed load
    /// </summary>
    private const string FailedMessage = "Loading something failed: unexpected error";

    #endregion // Constants

    #region Static methods

    /// <summary>
    /// Run an action at once, as the dispatcher of a component does when it is already on its context
    /// </summary>
    /// <param name="action">Action to run</param>
    /// <returns>Completed task</returns>
    private static Task InvokeNow(Action action)
    {
        action();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Move the state through the routes of a path, separated by '&gt;'
    /// </summary>
    /// <param name="state">State to move</param>
    /// <param name="path">Routes in the order of the parameter sets</param>
    private static void MoveThrough(DetailLoadState state, string path)
    {
        foreach (var route in path.Split('>'))
        {
            state.SetRoute(route);
        }
    }

    /// <summary>
    /// Start a run of the load that clears the detail through the given list
    /// </summary>
    /// <param name="state">State to run</param>
    /// <param name="load">Load of the run</param>
    /// <param name="logger">Logger of the run</param>
    /// <param name="clears">Receives an entry for every call of the detail clearing</param>
    /// <returns>Task of the run</returns>
    private static Task StartRun(DetailLoadState state, Func<Task> load, TestLogger<DetailLoadStateTests> logger, List<string> clears)
    {
        return state.RunAsync(InvokeNow, load, logger, LoadAction, LoadPrefix, () => clears.Add("cleared"));
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify the route key answers true only when it replaces a different key of an earlier parameter set
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    [TestCategory("AC5")]
    public void DetailLoadStateSetRouteReportsOnlyChangedKey()
    {
        var state = new DetailLoadState();

        var results = new List<bool>
                      {
                          state.SetRoute("a"),
                          state.SetRoute("a"),
                          state.SetRoute("b"),
                          state.SetRoute("b"),
                          state.SetRoute("a")
                      };

        Assert.AreSequenceEqual(new List<bool>
                                {
                                    false,
                                    false,
                                    true,
                                    false,
                                    true
                                },
                                results,
                                "Only a key that replaces a different earlier key may be reported as a route change, never the first key or the same key");
    }

    /// <summary>
    /// Verify an answer for an earlier route changes nothing, and the answer for the current route is recorded
    /// </summary>
    [TestMethod]
    [TestCategory("AC2")]
    public void DetailLoadStateFoundForEarlierRouteKeepsShownDetail()
    {
        var state = new DetailLoadState();

        state.SetRoute("a");
        state.SetRoute("b");

        var loadedEarlier = state.Found("a", "detail-a", "shown-b");
        var notFoundEarlier = state.Found<string>("a", null, "shown-b");
        var isNotFoundAfterEarlier = state.IsNotFound;
        var notFoundCurrent = state.Found<string>("b", null, "shown-b");

        Assert.AreEqual("shown-b", loadedEarlier, "A record of an earlier route must leave the shown detail unchanged");
        Assert.AreEqual("shown-b", notFoundEarlier, "A not-found answer of an earlier route must leave the shown detail unchanged");
        Assert.IsFalse(isNotFoundAfterEarlier, "A not-found answer of an earlier route must not set the not-found state");
        Assert.IsNull(notFoundCurrent, "A not-found answer of the current route must clear the detail");
        Assert.IsTrue(state.IsNotFound, "A not-found answer of the current route must set the not-found state");
    }

    /// <summary>
    /// Verify a failed load started before a route change, also when the page returned to its route, keeps the current outcome and is still logged once
    /// </summary>
    /// <param name="path">Routes after the first one, separated by '&gt;'</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow("b")]
    [DataRow("b>a")]
    public async Task DetailLoadStateLateFailureOfEarlierRouteKeepsCurrentOutcomeAsync(string path)
    {
        var exception = new InvalidOperationException("late failure");
        var logger = new TestLogger<DetailLoadStateTests>();
        var clears = new List<string>();
        var state = new DetailLoadState();
        var pendingEarlier = new TaskCompletionSource();

        state.SetRoute("a");

        var earlierRun = StartRun(state, () => pendingEarlier.Task, logger, clears);

        MoveThrough(state, path);

        await StartRun(state, () => Task.CompletedTask, logger, clears).ConfigureAwait(false);

        pendingEarlier.SetException(exception);

        await earlierRun.ConfigureAwait(false);

        Assert.IsNull(state.ErrorMessage, "A late failure of an earlier route must not set the message of the current route");
        Assert.IsEmpty(clears, "A late failure of an earlier route must not clear the detail of the current route");
        DetailPageAssert.AssertLoadLoggedOnce(logger, LoadAction, exception);
    }

    /// <summary>
    /// Verify a successful load started before a route change, also when the page returned to its route, keeps the current message
    /// </summary>
    /// <param name="path">Routes after the first one, separated by '&gt;'</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    [DataRow("b")]
    [DataRow("b>a")]
    public async Task DetailLoadStateLateSuccessOfEarlierRouteKeepsCurrentAlertAsync(string path)
    {
        var logger = new TestLogger<DetailLoadStateTests>();
        var clears = new List<string>();
        var state = new DetailLoadState();
        var pendingEarlier = new TaskCompletionSource();

        state.SetRoute("a");

        var earlierRun = StartRun(state, () => pendingEarlier.Task, logger, clears);

        MoveThrough(state, path);

        await StartRun(state, () => Task.FromException(new InvalidOperationException("current failure")), logger, clears).ConfigureAwait(false);

        pendingEarlier.SetResult();

        await earlierRun.ConfigureAwait(false);

        Assert.AreEqual(FailedMessage, state.ErrorMessage, "A late success of an earlier route must not hide the message of the current route");
        Assert.HasCount(1, clears, "The failure of the current route must have cleared the detail once, and nothing else");
    }

    #endregion // Methods
}