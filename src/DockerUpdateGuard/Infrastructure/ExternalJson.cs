using System.Text.Json;

namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Parses external JSON and rewrites every parse failure into an exception that carries no remote text
/// </summary>
internal static class ExternalJson
{
    #region Constants

    /// <summary>
    /// Message of the skeleton bodies
    /// </summary>
    private const string SkeletonMessage = "Skeleton: not implemented";

    #endregion // Constants

    #region Static methods

    /// <summary>
    /// Parse a stream into a JSON document
    /// </summary>
    /// <param name="stream">Stream to parse</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed document</returns>
#pragma warning disable S1172 // Skeleton: removed with the implementation
    public static Task<JsonDocument> ParseAsync(Stream stream, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(SkeletonMessage);
    }

    /// <summary>
    /// Parse UTF-8 bytes into a JSON document
    /// </summary>
    /// <param name="utf8Json">UTF-8 JSON bytes</param>
    /// <returns>Parsed document</returns>
    public static JsonDocument Parse(byte[] utf8Json)
    {
        throw new InvalidOperationException(SkeletonMessage);
    }

    /// <summary>
    /// Read the JSON content of an HTTP response into a value
    /// </summary>
    /// <typeparam name="T">Type to read</typeparam>
    /// <param name="content">Content to read</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Value read</returns>
    public static Task<T?> ReadFromJsonAsync<T>(HttpContent content, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(SkeletonMessage);
    }

    /// <summary>
    /// Deserialize a JSON string into a value
    /// </summary>
    /// <typeparam name="T">Type to read</typeparam>
    /// <param name="json">JSON text</param>
    /// <returns>Value read</returns>
    public static T? Deserialize<T>(string json)
    {
        throw new InvalidOperationException(SkeletonMessage);
    }

    /// <summary>
    /// Build the exception that replaces a parse failure and carries no remote text
    /// </summary>
    /// <param name="exception">Exception of the parser</param>
    /// <returns>Exception with a fixed message</returns>
    public static JsonException CreateParseFailure(JsonException exception)
    {
        throw new InvalidOperationException(SkeletonMessage);
    }
#pragma warning restore S1172 // Skeleton: removed with the implementation

    #endregion // Static methods
}