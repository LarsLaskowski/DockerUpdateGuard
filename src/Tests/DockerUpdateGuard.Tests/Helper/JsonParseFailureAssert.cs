using System.Text.Json;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Malformed remote JSON that forges a log line, and the assertions that a parse failure carries the fixed message only
/// </summary>
internal static class JsonParseFailureAssert
{
    #region Constants

    /// <summary>
    /// Message of the rewritten parse failure of the forged-line body, which fails at line 0, byte 3
    /// </summary>
    public const string ForgedLineMessage = "Remote JSON is not valid at line 0, byte 3 (counted from 0)";

    #endregion // Constants

    #region Static methods

    /// <summary>
    /// Create the body that is not JSON and carries a line break followed by a forged log line
    /// </summary>
    /// <returns>Body</returns>
    public static string CreateForgedLineBody()
    {
        return "nul" + "\r\n" + "FAKE LOG LINE";
    }

    /// <summary>
    /// Create the escape character that starts a terminal control sequence
    /// </summary>
    /// <returns>Escape character as text</returns>
    public static string CreateEscape()
    {
        return ((char)0x1B).ToString();
    }

    /// <summary>
    /// Assert the body still carries the line break of the forged log line
    /// </summary>
    /// <param name="body">Body of the test</param>
    public static void CarriesForgedLine(string body)
    {
        Assert.Contains("\r\n" + "FAKE LOG LINE", body, "The test input must still carry the line break and the forged log line");
    }

    /// <summary>
    /// Assert an exception is the rewritten parse failure of the forged-line body
    /// </summary>
    /// <param name="exception">Exception logged or thrown</param>
    /// <param name="context">What logged or threw the exception</param>
    public static void IsForgedLineRewritten(Exception? exception, string context)
    {
        IsRewritten(exception, ForgedLineMessage, 0, 3, context);
    }

    /// <summary>
    /// Assert an exception is exactly a JSON exception with the fixed message, no path, no inner exception and the parser's position
    /// </summary>
    /// <param name="exception">Exception logged or thrown</param>
    /// <param name="expectedMessage">Fixed message</param>
    /// <param name="expectedLineNumber">Line of the parser's exception</param>
    /// <param name="expectedBytePosition">Byte position of the parser's exception</param>
    /// <param name="context">What logged or threw the exception</param>
    public static void IsRewritten(Exception? exception,
                                   string expectedMessage,
                                   long? expectedLineNumber,
                                   long? expectedBytePosition,
                                   string context)
    {
        Assert.IsNotNull(exception, $"{context} must carry the parse failure");
        Assert.AreEqual(expectedMessage, exception.Message, $"{context} must carry the fixed message with the position only, never the parser's quote of the remote text");
        Assert.DoesNotContain("\r", exception.Message, $"The message of {context} must carry no carriage return");
        Assert.DoesNotContain("\n", exception.Message, $"The message of {context} must carry no line feed");
        Assert.DoesNotContain(CreateEscape(), exception.Message, $"The message of {context} must carry no escape character");

        var jsonException = Assert.IsExactInstanceOfType<JsonException>(exception, $"{context} must be exactly a JSON exception, not the parser's subclass");

        Assert.IsNull(jsonException.Path, $"{context} must carry no path, which can hold a decoded remote key");
        Assert.IsNull(jsonException.InnerException, $"{context} must carry no inner exception, which can repeat the remote text");
        Assert.AreEqual(expectedLineNumber, jsonException.LineNumber, $"{context} must keep the parser's line number");
        Assert.AreEqual(expectedBytePosition, jsonException.BytePositionInLine, $"{context} must keep the parser's byte position");
    }

    #endregion // Static methods
}