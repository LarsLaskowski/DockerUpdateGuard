using DockerUpdateGuard.Configuration;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Calculates the observed-image refresh interval from the configured Docker Hub budget
/// </summary>
public static class ObservedImageScanIntervalCalculator
{
    #region Methods

    /// <summary>
    /// Calculate the refresh interval for scheduled observed-image scans
    /// </summary>
    /// <param name="options">Scanning options</param>
    /// <param name="observedImageCount">Enabled observed image count</param>
    /// <returns>Refresh interval</returns>
    public static TimeSpan CalculateInterval(ScanningOptions options, int observedImageCount)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (observedImageCount <= 0)
        {
            return TimeSpan.FromMinutes(options.OwnImageBaseScanIntervalMinutes);
        }

        var scheduledBudget = options.DockerHubRequestLimitPerWindow - options.DockerHubReservedManualRequestsPerWindow;
        var windowMinutes = options.DockerHubRequestLimitWindowHours * 60d;
        var calculatedIntervalMinutes = (int)Math.Ceiling((windowMinutes * observedImageCount) / scheduledBudget);

        return TimeSpan.FromMinutes(Math.Max(options.OwnImageBaseScanIntervalMinutes, calculatedIntervalMinutes));
    }

    #endregion // Methods
}