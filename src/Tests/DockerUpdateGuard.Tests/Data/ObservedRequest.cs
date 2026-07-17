namespace DockerUpdateGuard.Tests.Data;

/// <summary>
/// Observed outbound request data
/// </summary>
internal sealed class ObservedRequest
{
    #region Properties

    /// <summary>
    /// Authorization parameter
    /// </summary>
    public string? AuthorizationParameter { get; init; }

    /// <summary>
    /// Authorization scheme
    /// </summary>
    public string? AuthorizationScheme { get; init; }

    /// <summary>
    /// X-API-Key header value
    /// </summary>
    public string? ApiKeyHeader { get; init; }

    /// <summary>
    /// HTTP method
    /// </summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>
    /// Request URI
    /// </summary>
    public string RequestUri { get; init; } = string.Empty;

    #endregion // Properties
}