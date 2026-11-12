namespace DockerUpdateGuard.Images;

/// <summary>
/// Source-generated logging contracts for <see cref="NginxReleaseMetadataService"/>
/// </summary>
internal static partial class NginxReleaseMetadataServiceLogging
{
    #region Methods

    /// <summary>
    /// Log that loading the release metadata failed with an exception
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="exception">Exception that stopped the load</param>
    [LoggerMessage(EventId = 2102,
                   Level = LogLevel.Warning,
                   Message = "Loading NGINX release metadata failed")]
    public static partial void NginxReleaseMetadataLoadFailed(this ILogger logger, Exception exception);

    #endregion // Methods
}