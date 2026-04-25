using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Images;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ObservedImageScanIntervalCalculator"/>
/// </summary>
[TestClass]
public class ObservedImageScanIntervalCalculatorTests
{
    #region Methods

    /// <summary>
    /// Verify the configured minimum interval is kept when the request budget has enough room
    /// </summary>
    [TestMethod]
    public void ObservedImageScanIntervalCalculatorCalculateIntervalWithinBudgetKeepsMinimumInterval()
    {
        var options = CreateOptions();

        var interval = ObservedImageScanIntervalCalculator.CalculateInterval(options, 10);

        Assert.AreEqual(TimeSpan.FromMinutes(30),
                        interval,
                        "Observed image refreshes must keep the configured minimum interval when the Docker Hub budget is sufficient");
    }

    /// <summary>
    /// Verify the interval stretches when the scheduled request budget would otherwise be exhausted
    /// </summary>
    [TestMethod]
    public void ObservedImageScanIntervalCalculatorCalculateIntervalWithLargeInventoryExtendsInterval()
    {
        var options = CreateOptions();

        var interval = ObservedImageScanIntervalCalculator.CalculateInterval(options, 120);

        Assert.AreEqual(TimeSpan.FromMinutes(270),
                        interval,
                        "Observed image refreshes must stretch the interval to leave room for manual Docker Hub requests");
    }

    /// <summary>
    /// Verify empty inventories fall back to the configured minimum interval
    /// </summary>
    [TestMethod]
    public void ObservedImageScanIntervalCalculatorCalculateIntervalWithoutObservedImagesUsesMinimumInterval()
    {
        var options = CreateOptions();

        var interval = ObservedImageScanIntervalCalculator.CalculateInterval(options, 0);

        Assert.AreEqual(TimeSpan.FromMinutes(30),
                        interval,
                        "Observed image refreshes must use the configured minimum interval when no images are enabled");
    }

    /// <summary>
    /// Create calculator options
    /// </summary>
    /// <returns>Configured options</returns>
    private static ScanningOptions CreateOptions()
    {
        return new ScanningOptions
               {
                   OwnImageBaseScanIntervalMinutes = 30,
                   DockerHubRequestLimitWindowHours = 6,
                   DockerHubRequestLimitPerWindow = 200,
                   DockerHubReservedManualRequestsPerWindow = 40,
               };
    }

    #endregion // Methods
}