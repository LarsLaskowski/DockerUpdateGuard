using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.DockerHub;

/// <summary>
/// Source-generated logging contracts for <see cref="DockerHubClient"/>
/// </summary>
internal static partial class DockerHubClientLogging
{
    #region Methods

    /// <summary>
    /// Log that a Docker Hub operation was requested for an unsupported registry
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="registry">Registry value</param>
    /// <param name="operationName">Operation name</param>
    [LoggerMessage(EventId = 3200,
                   Level = LogLevel.Information,
                   Message = "Docker Hub adapter does not support registry {Registry} for {OperationName}")]
    public static partial void DockerHubRegistryUnsupported(this ILogger logger,
                                                            string registry,
                                                            string operationName);

    /// <summary>
    /// Log that a Docker Hub target was not found
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="operationName">Operation name</param>
    /// <param name="targetName">Lookup target</param>
    [LoggerMessage(EventId = 3201,
                   Level = LogLevel.Information,
                   Message = "Docker Hub {OperationName} target {TargetName} was not found")]
    public static partial void DockerHubTargetNotFound(this ILogger logger,
                                                       string operationName,
                                                       string targetName);

    /// <summary>
    /// Log that a Docker Hub request failed with a non-success status code
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="operationName">Operation name</param>
    /// <param name="targetName">Lookup target</param>
    /// <param name="statusCode">HTTP status code</param>
    [LoggerMessage(EventId = 3202,
                   Level = LogLevel.Warning,
                   Message = "Docker Hub {OperationName} request failed for {TargetName} with status code {StatusCode}")]
    public static partial void DockerHubRequestFailed(this ILogger logger,
                                                      string operationName,
                                                      string targetName,
                                                      int statusCode);

    /// <summary>
    /// Log that the authenticated Docker Hub account was resolved
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="accountName">Docker Hub account name</param>
    [LoggerMessage(EventId = 3204,
                   Level = LogLevel.Information,
                   Message = "Resolved authenticated Docker Hub account {AccountName}")]
    public static partial void DockerHubAuthenticatedAccountResolved(this ILogger logger, string accountName);

    /// <summary>
    /// Log that Docker Hub repositories were listed successfully
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="accountName">Docker Hub account name</param>
    /// <param name="repositoryCount">Repository count</param>
    [LoggerMessage(EventId = 3205,
                   Level = LogLevel.Information,
                   Message = "Read {RepositoryCount} Docker Hub repositories for account {AccountName}")]
    public static partial void DockerHubRepositoriesListed(this ILogger logger,
                                                           string accountName,
                                                           int repositoryCount);

    /// <summary>
    /// Log that Docker Hub authentication is not possible because the user name is missing
    /// </summary>
    /// <param name="logger">Logger</param>
    [LoggerMessage(EventId = 3206,
                   Level = LogLevel.Warning,
                   Message = "Docker Hub authentication requires DockerHub:UserName when a PAT is configured")]
    public static partial void DockerHubAuthenticationUserNameMissing(this ILogger logger);

    /// <summary>
    /// Log that Docker Hub base image chain resolution completed successfully
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="imageReference">Image reference</param>
    /// <param name="baseImageCount">Number of resolved base images</param>
    [LoggerMessage(EventId = 3207,
                   Level = LogLevel.Information,
                   Message = "Resolved {BaseImageCount} base image(s) in chain for {ImageReference}")]
    public static partial void DockerHubBaseImageChainResolved(this ILogger logger,
                                                               string imageReference,
                                                               int baseImageCount);

    /// <summary>
    /// Log that a Docker Registry token request failed
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="imageReference">Image reference</param>
    /// <param name="statusCode">HTTP status code</param>
    [LoggerMessage(EventId = 3208,
                   Level = LogLevel.Warning,
                   Message = "Docker Registry token request failed for {ImageReference} with status code {StatusCode}")]
    public static partial void DockerHubRegistryTokenFailed(this ILogger logger,
                                                            string imageReference,
                                                            int statusCode);

    /// <summary>
    /// Log that a Docker Registry manifest fetch failed
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="imageReference">Image reference</param>
    /// <param name="statusCode">HTTP status code</param>
    [LoggerMessage(EventId = 3209,
                   Level = LogLevel.Warning,
                   Message = "Docker Registry manifest fetch failed for {ImageReference} with status code {StatusCode}")]
    public static partial void DockerHubManifestFetchFailed(this ILogger logger,
                                                            string imageReference,
                                                            int statusCode);

    /// <summary>
    /// Log that Docker Hub base image resolution failed with an exception
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="exception">Exception</param>
    /// <param name="imageReference">Image reference</param>
    [LoggerMessage(EventId = 3210,
                   Level = LogLevel.Warning,
                   Message = "Docker Hub base image resolution failed for {ImageReference}")]
    public static partial void DockerHubBaseImageResolutionFailed(this ILogger logger,
                                                                  Exception exception,
                                                                  string imageReference);

    #endregion // Methods
}