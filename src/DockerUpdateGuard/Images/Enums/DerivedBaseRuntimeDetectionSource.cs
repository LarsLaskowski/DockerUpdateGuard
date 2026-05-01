namespace DockerUpdateGuard.Images.Enums;

/// <summary>
/// Source used to detect a derived base runtime
/// </summary>
public enum DerivedBaseRuntimeDetectionSource
{
    /// <summary>
    /// No detection source set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Inspect environment variable
    /// </summary>
    InspectEnvironment = 1,

    /// <summary>
    /// History environment marker
    /// </summary>
    HistoryEnvironment = 2,

    /// <summary>
    /// Inspect ASP.NET environment variable
    /// </summary>
    InspectAspNetEnvironment = 3,

    /// <summary>
    /// History ASP.NET environment marker
    /// </summary>
    HistoryAspNetEnvironment = 4,

    /// <summary>
    /// History command text
    /// </summary>
    HistoryCommand = 5,
}