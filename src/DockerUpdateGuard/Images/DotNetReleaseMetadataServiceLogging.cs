namespace DockerUpdateGuard.Images;

/// <summary>
/// Source-generated logging contracts for <see cref="DotNetReleaseMetadataService"/>
/// </summary>
internal static partial class DotNetReleaseMetadataServiceLogging
{
    #region Methods

    /// <summary>
    /// Log that loading the release metadata failed with an exception
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="exception">Exception that stopped the load</param>
    [LoggerMessage(EventId = 2101,
                   Level = LogLevel.Warning,
                   Message = "Loading .NET release metadata failed")]
    public static partial void DotNetReleaseMetadataLoadFailed(this ILogger logger, Exception exception);

    #endregion // Methods
}