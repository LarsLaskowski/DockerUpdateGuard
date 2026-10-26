namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Oversized remote-derived values shared by the tests of bounded failure messages
/// </summary>
internal static class OversizedRemoteText
{
    #region Constants

    /// <summary>
    /// Number of characters of an oversized response body
    /// </summary>
    public const int BodyLength = 10000;

    /// <summary>
    /// Number of characters of an oversized response reason phrase
    /// </summary>
    public const int ReasonPhraseLength = 1000;

    /// <summary>
    /// Length of a stored assessment message column
    /// </summary>
    public const int AssessmentMessageLimit = 2000;

    #endregion // Constants

    #region Static methods

    /// <summary>
    /// Create the excerpt an oversized body is cut to: 500 characters followed by the ellipsis
    /// </summary>
    /// <returns>Excerpt</returns>
    public static string CreateExpectedExcerpt()
    {
        return $"{new string('x', 500)}…";
    }

    /// <summary>
    /// Create a host of 60 labels of 63 characters
    /// </summary>
    /// <returns>Host name</returns>
    public static string CreateOversizedHost()
    {
        return string.Join('.', Enumerable.Repeat(new string('a', 63), 60));
    }

    /// <summary>
    /// Create a registry host name of exactly 200 characters
    /// </summary>
    /// <returns>Registry host name</returns>
    public static string CreateRegistryOfStoredLimit()
    {
        return $"{new string('a', 63)}.{new string('b', 63)}.{new string('c', 63)}.dddddddd";
    }

    #endregion // Static methods
}