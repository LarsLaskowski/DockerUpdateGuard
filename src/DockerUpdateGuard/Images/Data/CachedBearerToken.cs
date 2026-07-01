namespace DockerUpdateGuard.Images.Data;

/// <summary>
/// Cached OCI registry bearer token with its expiration
/// </summary>
internal sealed class CachedBearerToken
{
    #region Properties

    /// <summary>
    /// Bearer token value
    /// </summary>
    public required string Token { get; init; }

    /// <summary>
    /// Timestamp at which the token must be considered expired
    /// </summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }

    #endregion // Properties
}