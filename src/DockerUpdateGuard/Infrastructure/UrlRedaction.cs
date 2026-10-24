namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Masks the user info of URLs before they reach logs, messages, stored values or the UI
/// </summary>
internal static class UrlRedaction
{
    #region Constants

    /// <summary>
    /// Text that replaces the user info of a URL
    /// </summary>
    public const string Mask = "***";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Replace the user info of a URL text with the mask
    /// </summary>
    /// <param name="url">URL text</param>
    /// <returns>URL text with masked user info, empty for null or empty input</returns>
    public static string RedactUserInfo(string? url)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    /// <summary>
    /// Replace the user info of a URI with the mask
    /// </summary>
    /// <param name="uri">URI</param>
    /// <returns>URI text with masked user info, empty for null</returns>
    public static string RedactUserInfo(Uri? uri)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Methods
}