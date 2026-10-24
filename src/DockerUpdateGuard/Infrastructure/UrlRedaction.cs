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
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        var last = url.LastIndexOf('@');

        if (last < 0)
        {
            return url;
        }

        if (url.IndexOf('@') == last
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && url.Trim().StartsWith($"{uri.Scheme}://", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo) == false)
        {
            var remainder = uri.GetComponents(UriComponents.Host | UriComponents.Port | UriComponents.PathAndQuery | UriComponents.Fragment, UriFormat.UriEscaped);

            return $"{uri.Scheme}://{Mask}@{remainder}";
        }

        var start = 0;
        var separator = url.IndexOf("://", StringComparison.Ordinal);

        if (separator > 0
            && separator < last
            && IsValidScheme(url.AsSpan(0, separator)))
        {
            start = separator + 3;
        }

        return string.Concat(url.AsSpan(0, start), Mask, url.AsSpan(last));
    }

    /// <summary>
    /// Replace the user info of a URI with the mask
    /// </summary>
    /// <param name="uri">URI</param>
    /// <returns>URI text with masked user info, empty for null</returns>
    public static string RedactUserInfo(Uri? uri)
    {
        if (uri is null)
        {
            return string.Empty;
        }

        return RedactUserInfo(uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString);
    }

    #endregion // Methods

    #region Static methods

    /// <summary>
    /// Check whether a text is a valid URI scheme (RFC 3986: ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ))
    /// </summary>
    /// <param name="scheme">Text before the scheme separator</param>
    /// <returns>True when the text is a valid scheme</returns>
    private static bool IsValidScheme(ReadOnlySpan<char> scheme)
    {
        if (scheme.IsEmpty
            || char.IsAsciiLetter(scheme[0]) == false)
        {
            return false;
        }

        foreach (var character in scheme)
        {
            if (char.IsAsciiLetterOrDigit(character) == false
                && character != '+'
                && character != '-'
                && character != '.')
            {
                return false;
            }
        }

        return true;
    }

    #endregion // Static methods
}