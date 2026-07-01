using System.Reflection;

using DockerUpdateGuard.Components.Pages;

using MudBlazor;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="MyImageDetail"/>
/// </summary>
[TestClass]
public class MyImageDetailTests
{
    #region Fields

    /// <summary>
    /// Non-public scan-status chip color resolver
    /// </summary>
    private static readonly MethodInfo _getScanStatusColorMethod = typeof(MyImageDetail).GetMethod("GetScanStatusColor", BindingFlags.NonPublic | BindingFlags.Static)
                                                                       ?? throw new InvalidOperationException("MyImageDetail must expose the non-public static GetScanStatusColor method");

    /// <summary>
    /// Non-public vulnerability-severity chip color resolver
    /// </summary>
    private static readonly MethodInfo _getVulnerabilitySeverityColorMethod = typeof(MyImageDetail).GetMethod("GetVulnerabilitySeverityColor", BindingFlags.NonPublic | BindingFlags.Static)
                                                                                  ?? throw new InvalidOperationException("MyImageDetail must expose the non-public static GetVulnerabilitySeverityColor method");

    /// <summary>
    /// Non-public vulnerability-assessment chip color resolver
    /// </summary>
    private static readonly MethodInfo _getVulnerabilityAssessmentColorMethod = typeof(MyImageDetail).GetMethod("GetVulnerabilityAssessmentColor", BindingFlags.NonPublic | BindingFlags.Static)
                                                                                    ?? throw new InvalidOperationException("MyImageDetail must expose the non-public static GetVulnerabilityAssessmentColor method");

    #endregion // Fields

    #region Methods

    /// <summary>
    /// Verify scan status chip colors map to expected MudBlazor color values
    /// </summary>
    /// <param name="status">Scan status string</param>
    /// <param name="expectedColor">Expected chip color</param>
    [TestMethod]
    [DataRow("Succeeded", Color.Success)]
    [DataRow("SUCCEEDED", Color.Success)]
    [DataRow("Completed", Color.Success)]
    [DataRow("COMPLETED", Color.Success)]
    [DataRow("Partial", Color.Warning)]
    [DataRow("PARTIAL", Color.Warning)]
    [DataRow("Failed", Color.Error)]
    [DataRow("FAILED", Color.Error)]
    [DataRow("Running", Color.Info)]
    [DataRow("RUNNING", Color.Info)]
    [DataRow("Pending", Color.Warning)]
    [DataRow("PENDING", Color.Warning)]
    [DataRow("Unknown", Color.Default)]
    [DataRow("anything-else", Color.Default)]
    public void MyImageDetailGetScanStatusColorKnownStatusReturnsExpectedColor(string status, Color expectedColor)
    {
        var color = (Color)_getScanStatusColorMethod.Invoke(null, [status])!;

        Assert.AreEqual(expectedColor,
                        color,
                        $"Scan status '{status}' must map to Color.{expectedColor}");
    }

    /// <summary>
    /// Verify vulnerability severity chip colors map to expected MudBlazor color values
    /// </summary>
    /// <param name="severity">Severity label</param>
    /// <param name="expectedColor">Expected chip color</param>
    [TestMethod]
    [DataRow("Critical", Color.Error)]
    [DataRow("CRITICAL", Color.Error)]
    [DataRow("High", Color.Warning)]
    [DataRow("HIGH", Color.Warning)]
    [DataRow("Medium", Color.Info)]
    [DataRow("MEDIUM", Color.Info)]
    [DataRow("Low", Color.Success)]
    [DataRow("LOW", Color.Success)]
    [DataRow("Unknown", Color.Default)]
    [DataRow("anything-else", Color.Default)]
    public void MyImageDetailGetVulnerabilitySeverityColorKnownSeverityReturnsExpectedColor(string severity, Color expectedColor)
    {
        var color = (Color)_getVulnerabilitySeverityColorMethod.Invoke(null, [severity])!;

        Assert.AreEqual(expectedColor,
                        color,
                        $"Vulnerability severity '{severity}' must map to Color.{expectedColor}");
    }

    /// <summary>
    /// Verify vulnerability assessment chip colors map to expected MudBlazor color values
    /// </summary>
    /// <param name="status">Vulnerability assessment status string</param>
    /// <param name="expectedColor">Expected chip color</param>
    [TestMethod]
    [DataRow("Findings Detected", Color.Warning)]
    [DataRow("FINDINGS DETECTED", Color.Warning)]
    [DataRow("No Findings", Color.Success)]
    [DataRow("NO FINDINGS", Color.Success)]
    [DataRow("Failed", Color.Error)]
    [DataRow("FAILED", Color.Error)]
    [DataRow("Not Configured", Color.Default)]
    [DataRow("NOT CONFIGURED", Color.Default)]
    [DataRow("Unsupported", Color.Default)]
    [DataRow("UNSUPPORTED", Color.Default)]
    [DataRow("anything-else", Color.Info)]
    public void MyImageDetailGetVulnerabilityAssessmentColorKnownStatusReturnsExpectedColor(string status, Color expectedColor)
    {
        var color = (Color)_getVulnerabilityAssessmentColorMethod.Invoke(null, [status])!;

        Assert.AreEqual(expectedColor,
                        color,
                        $"Vulnerability assessment status '{status}' must map to Color.{expectedColor}");
    }

    #endregion // Methods
}