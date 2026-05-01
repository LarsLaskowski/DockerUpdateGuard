using System.Reflection;

using DockerUpdateGuard.Components.Layout;
using DockerUpdateGuard.UI;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="MainLayout"/>
/// </summary>
[TestClass]
public class MainLayoutTests
{
    #region Fields

    /// <summary>
    /// Non-public protected-assets resolver
    /// </summary>
    private static readonly MethodInfo _getProtectedAssetCountMethod = typeof(MainLayout).GetMethod("GetProtectedAssetCount",
                                                                                                    BindingFlags.NonPublic | BindingFlags.Instance)
                                                                       ?? throw new InvalidOperationException("MainLayout must expose the non-public GetProtectedAssetCount method");

    private static readonly FieldInfo _dashboardSummaryField = typeof(MainLayout).GetField("_dashboardSummary",
                                                                                           BindingFlags.NonPublic | BindingFlags.Instance)
                                                               ?? throw new InvalidOperationException("MainLayout must expose the _dashboardSummary field for test setup");

    #endregion // Fields

    #region Methods

    /// <summary>
    /// Verify protected assets include manual images, own images, and runtime containers
    /// </summary>
    [TestMethod]
    public void MainLayoutGetProtectedAssetCountReturnsCombinedObservedMyImagesAndRuntimeContainers()
    {
        var layout = new MainLayout();

        _dashboardSummaryField.SetValue(layout,
                                        new DashboardViewData
                                        {
                                            ObservedImageCount = 3,
                                            MyImageCount = 2,
                                            RuntimeContainerCount = 5,
                                        });

        var protectedAssetCount = (int)_getProtectedAssetCountMethod.Invoke(layout, [])!;

        Assert.AreEqual(10,
                        protectedAssetCount,
                        "Protected assets must include manual observed images, discovery-owned images, and runtime containers");
    }

    /// <summary>
    /// Verify protected assets return zero when no dashboard summary is loaded yet
    /// </summary>
    [TestMethod]
    public void MainLayoutGetProtectedAssetCountWithoutDashboardSummaryReturnsZero()
    {
        var layout = new MainLayout();

        var protectedAssetCount = (int)_getProtectedAssetCountMethod.Invoke(layout, [])!;

        Assert.AreEqual(0,
                        protectedAssetCount,
                        "Protected assets must return zero before the dashboard summary is available");
    }

    #endregion // Methods
}