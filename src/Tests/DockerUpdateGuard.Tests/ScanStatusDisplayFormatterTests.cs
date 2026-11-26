using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.UI;

using MudBlazor;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ScanStatusDisplayFormatter"/>
/// </summary>
[TestClass]
public class ScanStatusDisplayFormatterTests
{
    #region Methods

    /// <summary>
    /// Verify every scan status the engine produces maps to its chip color, in any letter case
    /// </summary>
    /// <param name="status">Scan status text</param>
    /// <param name="expectedColor">Expected chip color</param>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow(nameof(ScanRunStatus.Succeeded), Color.Success)]
    [DataRow("SUCCEEDED", Color.Success)]
    [DataRow("succeeded", Color.Success)]
    [DataRow(nameof(ScanRunStatus.Partial), Color.Warning)]
    [DataRow("PARTIAL", Color.Warning)]
    [DataRow("partial", Color.Warning)]
    [DataRow(nameof(ScanRunStatus.Pending), Color.Warning)]
    [DataRow("PENDING", Color.Warning)]
    [DataRow("pending", Color.Warning)]
    [DataRow(nameof(ScanRunStatus.Failed), Color.Error)]
    [DataRow("FAILED", Color.Error)]
    [DataRow("failed", Color.Error)]
    [DataRow(nameof(ScanRunStatus.Running), Color.Info)]
    [DataRow("RUNNING", Color.Info)]
    [DataRow("running", Color.Info)]
    public void ScanStatusDisplayFormatterGetStatusColorKnownStatusReturnsExpectedColor(string status, Color expectedColor)
    {
        var color = ScanStatusDisplayFormatter.GetStatusColor(status);

        Assert.AreEqual(expectedColor,
                        color,
                        $"Scan status '{status}' must map to Color.{expectedColor}");
    }

    /// <summary>
    /// Verify every other input resolves to the default chip color
    /// </summary>
    /// <param name="status">Scan status text</param>
    [TestMethod]
    [TestCategory("AC2")]
    [DataRow(nameof(ScanRunStatus.NotSet))]
    [DataRow("NotScanned")]
    [DataRow("Completed")]
    [DataRow("COMPLETED")]
    [DataRow("anything-else")]
    [DataRow("3")]
    [DataRow("99")]
    [DataRow("Pending, Running")]
    [DataRow(" Succeeded ")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void ScanStatusDisplayFormatterGetStatusColorOtherValueReturnsDefault(string? status)
    {
        var color = ScanStatusDisplayFormatter.GetStatusColor(status);

        Assert.AreEqual(Color.Default,
                        color,
                        $"Scan status '{status}' must resolve to Color.Default");
    }

    /// <summary>
    /// Verify every scan run status except NotSet has a chip color of its own
    /// </summary>
    [TestMethod]
    [TestCategory("AC3")]
    public void ScanStatusDisplayFormatterGetStatusColorEveryStatusExceptNotSetHasColor()
    {
        var statuses = Enum.GetValues<ScanRunStatus>()
                           .Where(status => status != ScanRunStatus.NotSet)
                           .ToList();

        Assert.IsNotEmpty(statuses, "The scan run status enum must have values besides NotSet");

        foreach (var status in statuses)
        {
            var color = ScanStatusDisplayFormatter.GetStatusColor(status.ToString());

            Assert.AreNotEqual(Color.Default,
                               color,
                               $"Scan run status {status} must map to a color other than Color.Default");
        }
    }

    #endregion // Methods
}