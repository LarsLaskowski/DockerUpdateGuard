using System.Reflection;

using DockerUpdateGuard.Components.Pages;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="Dashboard"/>
/// </summary>
[TestClass]
public class DashboardTests
{
    #region Fields

    /// <summary>
    /// Non-public dashboard route resolver
    /// </summary>
    private static readonly MethodInfo GetMetricNavigationTargetMethod = typeof(Dashboard).GetMethod("GetMetricNavigationTarget",
                                                                                                     BindingFlags.NonPublic | BindingFlags.Static)
                                                                             ?? throw new InvalidOperationException("The dashboard page must expose the non-public GetMetricNavigationTarget method for metric-card navigation");

    #endregion // Fields

    #region Methods

    /// <summary>
    /// Verify dashboard metric cards point to the expected overview routes
    /// </summary>
    /// <param name="metricLabel">Metric label</param>
    /// <param name="expectedRoute">Expected route</param>
    [TestMethod]
    [DataRow("Observed Images", "/observed-images")]
    [DataRow("Docker Instances", "/docker-instances")]
    [DataRow("Runtime Containers", "/runtime-containers")]
    [DataRow("Shared Bases", "/shared-base-images")]
    public void DashboardGetMetricNavigationTargetKnownMetricReturnsExpectedRoute(string metricLabel, string expectedRoute)
    {
        var route = GetMetricNavigationTargetMethod.Invoke(null, [metricLabel]) as string;

        Assert.AreEqual(expectedRoute,
                        route,
                        $"The dashboard metric card '{metricLabel}' must navigate to '{expectedRoute}'");
    }

    #endregion // Methods
}