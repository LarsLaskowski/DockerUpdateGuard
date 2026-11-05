using System.Text.Json;

namespace DockerUpdateGuard.Infrastructure;

#pragma warning disable S1144, S1172 // Skeleton: removed with the implementation

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
        await Task.CompletedTask.ConfigureAwait(false);

        throw new InvalidOperationException("Skeleton: not implemented");
    }

    /// <summary>
    /// Verify that every string value and property name of an element decodes
    /// </summary>
    /// <param name="element">Element to walk</param>
    private static void EnsureStringsDecode(JsonElement element)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Static methods
}
#pragma warning restore S1144, S1172