using System.Globalization;
using System.Text.Json;

namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Parses external JSON and rewrites every parse failure into an exception that carries no remote text
/// </summary>
internal static class ExternalJson
{
    #region Constants

    /// <summary>
    /// Message of a parse failure whose position is unknown
    /// </summary>
    private const string ParseFailureMessage = "Remote JSON is not valid";

    /// <summary>
    /// Message of the refusal of an unusable content character set
    /// </summary>
    private const string InvalidCharsetMessage = "The character set of the remote response is not valid";

    #endregion // Constants

    #region Static methods

    /// <summary>
    /// Parse a stream into a JSON document
    /// </summary>
    /// <param name="stream">Stream to parse</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed document</returns>
    public static async Task<JsonDocument> ParseAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw CreateParseFailure(exception);
        }
    }

    /// <summary>
    /// Parse UTF-8 bytes into a JSON document
    /// </summary>
    /// <param name="utf8Json">UTF-8 JSON bytes</param>
    /// <returns>Parsed document</returns>
    public static JsonDocument Parse(byte[] utf8Json)
    {
        try
        {
            return JsonDocument.Parse(utf8Json);
        }
        catch (JsonException exception)
        {
            throw CreateParseFailure(exception);
        }
    }

    /// <summary>
    /// Read the JSON content of an HTTP response into a value
    /// </summary>
    /// <typeparam name="T">Type to read</typeparam>
    /// <param name="content">Content to read</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Value read</returns>
    public static async Task<T?> ReadFromJsonAsync<T>(HttpContent content, CancellationToken cancellationToken)
    {
        try
        {
            return await content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw CreateParseFailure(exception);
        }
        catch (InvalidOperationException exception) when (exception.InnerException is ArgumentException)
        {
            throw new InvalidOperationException(InvalidCharsetMessage);
        }
    }

    /// <summary>
    /// Deserialize a JSON string into a value
    /// </summary>
    /// <typeparam name="T">Type to read</typeparam>
    /// <param name="json">JSON text</param>
    /// <returns>Value read</returns>
    public static T? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException exception)
        {
            throw CreateParseFailure(exception);
        }
    }

    /// <summary>
    /// Build the exception that replaces a parse failure and carries no remote text
    /// </summary>
    /// <param name="exception">Exception of the parser</param>
    /// <returns>Exception with a fixed message</returns>
    public static JsonException CreateParseFailure(JsonException exception)
    {
        var line = exception.LineNumber;
        var position = exception.BytePositionInLine;

        if (line is null || position is null)
        {
            return new JsonException(ParseFailureMessage, null, line, position);
        }

        var message = string.Create(CultureInfo.InvariantCulture, $"{ParseFailureMessage} at line {line.Value}, byte {position.Value} (counted from 0)");

        return new JsonException(message, null, line, position);
    }

    #endregion // Static methods
}