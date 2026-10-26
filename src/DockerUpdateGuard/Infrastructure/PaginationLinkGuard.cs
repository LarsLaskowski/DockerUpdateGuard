namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Keeps paginated listings on the origin of their first request
/// </summary>
internal static class PaginationLinkGuard
{
    #region Constants

    /// <summary>
    /// Description used for a link that cannot be parsed
    /// </summary>
    public const string UnparseableLinkDescription = "unparseable link";

    /// <summary>
    /// Maximum number of characters of a described origin
    /// </summary>
    public const int MaxOriginDescriptionLength = 300;

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Resolve a next-page link against the current request and accept it only on the allowed origin
    /// </summary>
    /// <param name="currentRequestUri">URI of the request that returned the link</param>
    /// <param name="allowedOrigin">Origin the listing must stay on</param>
    /// <param name="nextLink">Raw next-page link</param>
    /// <returns>Resolved link or null when it is empty, unparseable or on another origin</returns>
    public static Uri? ResolveNextLink(Uri currentRequestUri, Uri allowedOrigin, string? nextLink)
    {
        if (string.IsNullOrWhiteSpace(nextLink))
        {
            return null;
        }

        if (Uri.TryCreate(currentRequestUri, nextLink, out var resolved) == false)
        {
            return null;
        }

        if (string.Equals(resolved.Scheme, allowedOrigin.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(resolved.IdnHost, allowedOrigin.IdnHost, StringComparison.OrdinalIgnoreCase)
            && resolved.Port == allowedOrigin.Port)
        {
            return resolved;
        }

        return null;
    }

    /// <summary>
    /// Describe the origin of a rejected next-page link without user info, path or query
    /// </summary>
    /// <param name="currentRequestUri">URI of the request that returned the link</param>
    /// <param name="nextLink">Raw next-page link</param>
    /// <returns>Scheme, host and non-default port, or the unparseable link description</returns>
    public static string DescribeOrigin(Uri currentRequestUri, string? nextLink)
    {
        if (Uri.TryCreate(currentRequestUri, nextLink, out var resolved) == false)
        {
            return UnparseableLinkDescription;
        }

        var origin = $"{resolved.Scheme}://{resolved.IdnHost}";

        if (resolved.IsDefaultPort == false)
        {
            origin = $"{origin}:{resolved.Port}";
        }

        return ResponseBodyExcerpt.Truncate(origin, MaxOriginDescriptionLength);
    }

    /// <summary>
    /// Create the failure message of a listing stopped by a rejected next-page link
    /// </summary>
    /// <param name="listingDescription">Description of the listing</param>
    /// <param name="rejectedOrigin">Described origin of the rejected link</param>
    /// <returns>Failure message</returns>
    public static string CreateRejectionMessage(string listingDescription, string rejectedOrigin)
    {
        return $"{listingDescription} stopped: the next-page link pointed to a different origin ({rejectedOrigin})";
    }

    #endregion // Methods
}