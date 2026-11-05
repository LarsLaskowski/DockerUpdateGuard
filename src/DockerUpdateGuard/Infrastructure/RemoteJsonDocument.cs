using System.Text.Json;

namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Parses a remote JSON response and refuses a body whose strings do not decode as Unicode text
/// </summary>
internal static class RemoteJsonDocument
{
    #region Constants

    /// <summary>
    /// Message of the exception for a JSON string that does not decode
    /// </summary>
    private const string UndecodableStringMessage = "A JSON string of the remote response does not decode as Unicode text";

    #endregion // Constants

    #region Static methods

    /// <summary>
    /// Parse a stream into a JSON document and verify that every string decodes
    /// </summary>
    /// <param name="stream">Stream to parse</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed document</returns>
    public static async Task<JsonDocument> ParseAsync(Stream stream, CancellationToken cancellationToken)
    {
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        try
        {
            EnsureStringsDecode(document.RootElement);
        }
        catch (InvalidOperationException exception)
        {
            document.Dispose();

            throw new JsonException(UndecodableStringMessage, exception);
        }

        return document;
    }

    /// <summary>
    /// Verify that every string value and property name of an element decodes
    /// </summary>
    /// <param name="element">Element to walk</param>
    private static void EnsureStringsDecode(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                {
                    _ = element.GetString();
                }
                break;

            case JsonValueKind.Object:
                {
                    foreach (var property in element.EnumerateObject())
                    {
                        _ = property.Name;

                        EnsureStringsDecode(property.Value);
                    }
                }
                break;

            case JsonValueKind.Array:
                {
                    foreach (var item in element.EnumerateArray())
                    {
                        EnsureStringsDecode(item);
                    }
                }
                break;
        }
    }

    #endregion // Static methods
}