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
#pragma warning disable RH2003 // Skeleton body, replaced by the implementation
        throw new NotImplementedException();
#pragma warning restore RH2003
    }

    /// <summary>
    /// Describe the origin of a rejected next-page link without user info, path or query
    /// </summary>
    /// <param name="currentRequestUri">URI of the request that returned the link</param>
    /// <param name="nextLink">Raw next-page link</param>
    /// <returns>Scheme, host and non-default port, or the unparseable link description</returns>
    public static string DescribeOrigin(Uri currentRequestUri, string? nextLink)
    {
#pragma warning disable RH2003 // Skeleton body, replaced by the implementation
        throw new NotImplementedException();
#pragma warning restore RH2003
    }

    /// <summary>
    /// Create the failure message of a listing stopped by a rejected next-page link
    /// </summary>
    /// <param name="listingDescription">Description of the listing</param>
    /// <param name="rejectedOrigin">Described origin of the rejected link</param>
    /// <returns>Failure message</returns>
    public static string CreateRejectionMessage(string listingDescription, string rejectedOrigin)
    {
#pragma warning disable RH2003 // Skeleton body, replaced by the implementation
        throw new NotImplementedException();
#pragma warning restore RH2003
    }

    #endregion // Methods
}