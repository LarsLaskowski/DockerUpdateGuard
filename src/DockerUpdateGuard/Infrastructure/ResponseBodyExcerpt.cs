using System.Text;

namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Bounds remote-derived text before it reaches a failure message
/// </summary>
internal static class ResponseBodyExcerpt
{
    #region Constants

    /// <summary>
    /// Character that replaces every control, format, line or paragraph separator character
    /// </summary>
    public const char ControlCharacterReplacement = ' ';

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

    /// <summary>
    /// Buffer size in bytes of the reader that decodes the body
    /// </summary>
    private const int ReadBufferSize = 1024;

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Read at most the first characters of a response body
    /// </summary>
    /// <param name="content">Response content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Body, or its first characters followed by the truncation marker</returns>
    public static async Task<string> ReadAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            using (var reader = new StreamReader(stream, Encoding.UTF8, true, ReadBufferSize))
            {
                var buffer = new char[MaxBodyLength + 1];
                var count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);

                return Truncate(new string(buffer, 0, count), MaxBodyLength);
            }
        }
    }

    /// <summary>
    /// Replace every control, format, line or paragraph separator character of a text with a space and every lone surrogate with U+FFFD
    /// </summary>
    /// <param name="text">Text to clean</param>
    /// <returns>The text itself when nothing is replaced, otherwise the cleaned copy</returns>
    public static string ReplaceControlCharacters(string text)
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);

        if (text is null)
        {
            return string.Empty;
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = maxLength;

        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return string.Concat(text.AsSpan(0, cut), TruncationMarker);
    }

    #endregion // Methods
}