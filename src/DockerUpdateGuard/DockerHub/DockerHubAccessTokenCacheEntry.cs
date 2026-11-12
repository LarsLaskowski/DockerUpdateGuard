namespace DockerUpdateGuard.DockerHub;

/// <summary>
/// Immutable entry of the Docker Hub access token cache
/// </summary>
internal sealed class DockerHubAccessTokenCacheEntry
{
    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="token">Access token</param>
    /// <param name="expiresAtUtc">Expiry timestamp</param>
    /// <param name="loginKey">Login address key</param>
    /// <param name="userName">Trimmed user name</param>
    /// <param name="fingerprint">Fingerprint of the personal access token</param>
    public DockerHubAccessTokenCacheEntry(string token, DateTimeOffset expiresAtUtc, string loginKey, string userName, byte[] fingerprint)
    {
        Token = token;
        ExpiresAtUtc = expiresAtUtc;
        LoginKey = loginKey;
        UserName = userName;
        Fingerprint = fingerprint;
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Access token
    /// </summary>
    public string Token { get; }

    /// <summary>
    /// Expiry timestamp
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; }

    /// <summary>
    /// Login address key
    /// </summary>
    public string LoginKey { get; }

    /// <summary>
    /// Trimmed user name
    /// </summary>
    public string UserName { get; }

    /// <summary>
    /// Fingerprint of the personal access token
    /// </summary>
    public byte[] Fingerprint { get; }

    #endregion // Properties
}