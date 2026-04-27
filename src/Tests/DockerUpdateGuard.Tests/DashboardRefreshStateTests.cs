using DockerUpdateGuard.UI;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DashboardRefreshState"/>
/// </summary>
[TestClass]
public class DashboardRefreshStateTests
{
    #region Methods

    /// <summary>
    /// Verify refresh notifications raise the change event
    /// </summary>
    [TestMethod]
    public void DashboardRefreshStateNotifyChangedRaisesChangeEvent()
    {
        var refreshState = new DashboardRefreshState();
        var invocationCount = 0;

        refreshState.Changed += () => invocationCount++;

        refreshState.NotifyChanged();

        Assert.AreEqual(1,
                        invocationCount,
                        "Dashboard refresh notifications must raise the Changed event exactly once per call");
    }

    #endregion // Methods
}