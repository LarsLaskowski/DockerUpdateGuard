using System.Reflection;

using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NSubstitute;

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
        Assert.StartsWith("M ",
                          path,
                          "A sparkline path must start with an SVG move command");
        Assert.Contains(" C ",
                        path,
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
    /// Verify a failed update check shows a fixed reason instead of the exception message and logs the exception
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RuntimeContainersFailedUpdateCheckShowsFixedReasonAndLogsAsync()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var orchestrator = Substitute.For<IRuntimeContainerScanOrchestrator>();
            var logger = new TestLogger<RuntimeContainers>();
            var exception = new InvalidOperationException("connection to db.internal.test:5432 refused");

            viewService.GetRuntimeContainersAsync(Arg.Any<CancellationToken>())
                       .Returns(new List<RuntimeContainerListItemData>());
            orchestrator.ScanAllAsync(Arg.Any<ScanTriggerSource>(), Arg.Any<CancellationToken>())
                        .Returns(Task.FromException(exception));

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(orchestrator);
            testContext.Services.AddSingleton(new DashboardRefreshState());
            testContext.Services.AddSingleton<ILogger<RuntimeContainers>>(logger);

            var component = testContext.Render<RuntimeContainers>();

            await component.Find("button").ClickAsync().ConfigureAwait(false);

            UserActionFailureAssert.AssertShown(component.Markup, "Update check failed: unexpected error", "db.internal.test", "refused");
            UserActionFailureAssert.AssertLogged(logger, "UpdateCheck", exception);
        }
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