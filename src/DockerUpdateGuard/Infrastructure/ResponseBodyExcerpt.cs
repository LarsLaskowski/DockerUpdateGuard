namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Bounds remote-derived text before it reaches a failure message
/// </summary>
internal static class ResponseBodyExcerpt
{
    #region Constants

    /// <summary>
    /// Maximum number of characters of a response body excerpt
    /// </summary>
    public const int MaxBodyLength = 500;

    /// <summary>
    /// Maximum number of characters of a response reason phrase
    /// </summary>
    public const int MaxReasonPhraseLength = 100;

    /// <summary>
    /// Marker appended to a text that was cut
    /// </summary>
    public const string TruncationMarker = "…";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Read at most the first characters of a response body
    /// </summary>
    /// <param name="content">Response content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Body, or its first characters followed by the truncation marker</returns>
    public static Task<string> ReadAsync(HttpContent content, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    /// <summary>
    /// Cut a text to a maximum length
    /// </summary>
    /// <param name="text">Text to cut</param>
    /// <param name="maxLength">Maximum number of characters before the truncation marker</param>
    /// <returns>Empty string for null, the text when it fits, otherwise its prefix followed by the truncation marker</returns>
    public static string Truncate(string? text, int maxLength)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Methods
}