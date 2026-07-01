using System.Reflection;

using DockerUpdateGuard.Components.Pages;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="RuntimeContainers"/>
/// </summary>
[TestClass]
public class RuntimeContainersTests
{
    #region Fields

    /// <summary>
    /// Non-public sparkline-path builder method
    /// </summary>
    private static readonly MethodInfo _buildSparklinePathMethod = typeof(RuntimeContainers).GetMethod("BuildSparklinePath",
                                                                                                       BindingFlags.NonPublic | BindingFlags.Static)
                                                                       ?? throw new InvalidOperationException("The runtime containers page must expose the non-public BuildSparklinePath method for sparkline rendering");

    #endregion // Fields

    #region Methods

    /// <summary>
    /// Verify a single sparkline sample renders as a centered horizontal path
    /// </summary>
    [TestMethod]
    public void RuntimeContainersBuildSparklinePathSingleValueReturnsCenteredHorizontalLine()
    {
        var path = InvokeBuildSparklinePath([42d]);

        Assert.AreEqual("M 0,13 L 100,13",
                        path,
                        "A single sparkline sample must render as a centered horizontal line");
    }

    /// <summary>
    /// Verify multiple sparkline samples render as a smoothed cubic path
    /// </summary>
    [TestMethod]
    public void RuntimeContainersBuildSparklinePathMultipleValuesReturnsSmoothCurve()
    {
        var path = InvokeBuildSparklinePath([10d, 20d, 15d, 30d]);

        Assert.IsFalse(string.IsNullOrWhiteSpace(path), "A sparkline with multiple samples must produce SVG path data");
        StringAssert.StartsWith(path,
                                "M ",
                                "A sparkline path must start with an SVG move command");
        StringAssert.Contains(path,
                              " C ",
                              "A sparkline with multiple samples must use cubic Bezier segments for smoothing");
    }

    /// <summary>
    /// Verify empty sparkline series render no path
    /// </summary>
    [TestMethod]
    public void RuntimeContainersBuildSparklinePathEmptyValuesReturnsEmptyString()
    {
        var path = InvokeBuildSparklinePath([]);

        Assert.AreEqual(string.Empty,
                        path,
                        "An empty sparkline series must not render any SVG path data");
    }

    /// <summary>
    /// Invoke the non-public sparkline-path builder
    /// </summary>
    /// <param name="values">Series values</param>
    /// <returns>SVG path data</returns>
    private static string InvokeBuildSparklinePath(IEnumerable<double> values)
    {
        var path = _buildSparklinePathMethod.Invoke(null, [values]);

        return path as string ?? throw new InvalidOperationException("The sparkline-path builder must return SVG path data as a string");
    }

    #endregion // Methods
}