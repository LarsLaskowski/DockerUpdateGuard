using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.DockerHub;

/// <summary>
/// Process-wide cache of the Docker Hub web API access token, shared by all client instances and scopes; a token is
/// reused only for the login address, user name and personal access token it was obtained with, one login runs at a
/// time and is cancelled once the login timeout has passed
/// </summary>
public sealed class DockerHubAccessTokenCache : IDisposable
{
    #region Constants

    /// <summary>
    /// Minutes before the expiry from which a token is no longer reused
    /// </summary>
    private const int ReuseMarginMinutes = 1;

    /// <summary>
    /// Minutes a token without an expiry claim is kept
    /// </summary>
    private const int FallbackLifetimeMinutes = 5;

    /// <summary>
    /// Smallest expiry in Unix seconds that <see cref="DateTimeOffset"/> can represent
    /// </summary>
    private const long MinimumExpirationUnixTimeSeconds = -62135596800;

    /// <summary>
    /// Largest expiry in Unix seconds that <see cref="DateTimeOffset"/> can represent
    /// </summary>
    private const long MaximumExpirationUnixTimeSeconds = 253402300799;

    #endregion // Constants

    #region Fields

    /// <summary>
    /// Time provider
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Login lock
    /// </summary>
    private readonly SemaphoreSlim _loginLock = new(1, 1);

    /// <summary>
    /// Current entry, read and replaced as a whole
    /// </summary>
    private DockerHubAccessTokenCacheEntry? _entry;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="timeProvider">Time provider</param>
    public DockerHubAccessTokenCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Build the comparison key of a login address
    /// </summary>
    /// <param name="loginUri">Login address</param>
    /// <returns>Key of scheme, host, port and path</returns>
    private static string CreateLoginKey(Uri loginUri)
    {
        return $"{loginUri.Scheme}://{loginUri.Host}:{loginUri.Port}{loginUri.AbsolutePath}";
    }

    /// <summary>
    /// Build the SHA-256 fingerprint of a personal access token
    /// </summary>
    /// <param name="personalAccessToken">Personal access token</param>
    /// <returns>Fingerprint</returns>
    private static byte[] CreateFingerprint(string personalAccessToken)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(personalAccessToken));
    }

    /// <summary>
    /// Decode a Base64 URL value
    /// </summary>
    /// <param name="value">Encoded value</param>
    /// <returns>Decoded bytes</returns>
    private static byte[] DecodeBase64Url(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalizedValue = value.Replace('-', '+')
                                   .Replace('_', '/');
        var paddingLength = (4 - (normalizedValue.Length % 4)) % 4;
        var paddedValue = normalizedValue.PadRight(normalizedValue.Length + paddingLength, '=');

        try
        {
            return Convert.FromBase64String(paddedValue);
        }
        catch (FormatException exception)
        {
            throw new JsonException("The access token payload is not base64url", exception);
        }
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Return the cached access token or request a new one
    /// </summary>
    /// <param name="loginUri">Login address</param>
    /// <param name="userName">User name</param>
    /// <param name="personalAccessToken">Personal access token</param>
    /// <param name="loginTimeout">Timeout of the login request</param>
    /// <param name="requestToken">Function requesting a new token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Access token result</returns>
    public async Task<ExternalOperationResult<string>> GetOrRequestAsync(Uri loginUri,
                                                                         string userName,
                                                                         string personalAccessToken,
                                                                         TimeSpan loginTimeout,
                                                                         Func<CancellationToken, Task<ExternalOperationResult<string>>> requestToken,
                                                                         CancellationToken cancellationToken)
    {
        var loginKey = CreateLoginKey(loginUri);
        var trimmedUserName = userName.Trim();
        var fingerprint = CreateFingerprint(personalAccessToken);

        if (TryGetValidToken(loginKey, trimmedUserName, fingerprint, out var cachedToken))
        {
            return ExternalOperationResult.Succeeded<string>(cachedToken);
        }

        await _loginLock.WaitAsync(cancellationToken)
                        .ConfigureAwait(false);

        try
        {
            if (TryGetValidToken(loginKey, trimmedUserName, fingerprint, out cachedToken))
            {
                return ExternalOperationResult.Succeeded<string>(cachedToken);
            }

            using var timeoutSource = new CancellationTokenSource(loginTimeout, _timeProvider);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

            var tokenResult = await requestToken(linkedSource.Token).ConfigureAwait(false);

            if (tokenResult.Status != ExternalOperationStatus.Succeeded
                || string.IsNullOrWhiteSpace(tokenResult.Data))
            {
                return tokenResult;
            }

            var expiresAtUtc = ResolveTokenExpiry(tokenResult.Data);

            Volatile.Write(ref _entry, new DockerHubAccessTokenCacheEntry(tokenResult.Data, expiresAtUtc, loginKey, trimmedUserName, fingerprint));

            return tokenResult;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    /// <summary>
    /// Look up a token that is still reusable for the given credentials
    /// </summary>
    /// <param name="loginKey">Login address key</param>
    /// <param name="userName">Trimmed user name</param>
    /// <param name="fingerprint">Fingerprint of the personal access token</param>
    /// <param name="token">Cached token</param>
    /// <returns>True when a token can be reused</returns>
    private bool TryGetValidToken(string loginKey, string userName, byte[] fingerprint, out string token)
    {
        var entry = Volatile.Read(ref _entry);

        if (entry is not null
            && string.Equals(entry.LoginKey, loginKey, StringComparison.Ordinal)
            && string.Equals(entry.UserName, userName, StringComparison.Ordinal)
            && CryptographicOperations.FixedTimeEquals(entry.Fingerprint, fingerprint)
            && entry.ExpiresAtUtc > _timeProvider.GetUtcNow().AddMinutes(ReuseMarginMinutes))
        {
            token = entry.Token;

            return true;
        }

        token = string.Empty;

        return false;
    }

    /// <summary>
    /// Resolve the expiry of a Docker Hub access token
    /// </summary>
    /// <param name="token">JWT access token</param>
    /// <returns>Expiry timestamp</returns>
    private DateTimeOffset ResolveTokenExpiry(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var segments = token.Split('.');

        if (segments.Length >= 2)
        {
            var payloadBytes = DecodeBase64Url(segments[1]);

            if (payloadBytes.Length > 0)
            {
                using var jsonDocument = JsonDocument.Parse(payloadBytes);

                if (jsonDocument.RootElement.ValueKind == JsonValueKind.Object
                    && jsonDocument.RootElement.TryGetProperty("exp", out var expirationElement)
                    && expirationElement.ValueKind == JsonValueKind.Number
                    && expirationElement.TryGetInt64(out var expirationUnixTimeSeconds)
                    && expirationUnixTimeSeconds >= MinimumExpirationUnixTimeSeconds
                    && expirationUnixTimeSeconds <= MaximumExpirationUnixTimeSeconds)
                {
                    return DateTimeOffset.FromUnixTimeSeconds(expirationUnixTimeSeconds);
                }
            }
        }

        return _timeProvider.GetUtcNow().AddMinutes(FallbackLifetimeMinutes);
    }

    #endregion // Methods

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        _loginLock.Dispose();
    }

    #endregion // IDisposable
}