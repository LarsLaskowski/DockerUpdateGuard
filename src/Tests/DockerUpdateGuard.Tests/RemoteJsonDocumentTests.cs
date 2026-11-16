using System.Text;
using System.Text.Json;

using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="RemoteJsonDocument"/>
/// </summary>
[TestClass]
public class RemoteJsonDocumentTests
{
    #region Constants

    /// <summary>
    /// Backslash that starts a JSON escape
    /// </summary>
    private const char Backslash = '\\';

    #endregion // Constants

    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify a body whose strings and property names all decode is returned as a parsed document
    /// </summary>
    /// <param name="label">Name of the case</param>
    /// <param name="body">Response body</param>
    /// <param name="isEscape">Whether the case is a JSON escape that must keep its backslash</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DynamicData(nameof(GetDecodableBodies))]
    public async Task RemoteJsonDocumentParseAsyncWhenEveryStringDecodesReturnsDocumentAsync(string label, byte[] body, bool isEscape)
    {
        if (isEscape)
        {
            Assert.Contains((byte)Backslash, body, $"The input of case '{label}' must still carry the backslash of its escape");
        }

        using var stream = new MemoryStream(body);

        using var document = await RemoteJsonDocument.ParseAsync(stream, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(JsonValueKind.Object, document.RootElement.ValueKind, $"The document of case '{label}' must be returned with its root object");
    }

    /// <summary>
    /// Verify a body with one string or property name that does not decode is refused as a JSON exception
    /// </summary>
    /// <param name="label">Name of the case</param>
    /// <param name="body">Response body</param>
    /// <param name="isEscape">Whether the case is a JSON escape that must keep its backslash</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DynamicData(nameof(GetUndecodableBodies))]
    public async Task RemoteJsonDocumentParseAsyncWhenAStringDoesNotDecodeThrowsJsonExceptionAsync(string label, byte[] body, bool isEscape)
    {
        if (isEscape)
        {
            Assert.Contains((byte)Backslash, body, $"The input of case '{label}' must still carry the backslash of its escape");
        }

        using var stream = new MemoryStream(body);

        var exception = await Assert.ThrowsExactlyAsync<JsonException>(() => RemoteJsonDocument.ParseAsync(stream, TestContext.CancellationToken),
                                                                       $"Case '{label}' must be refused as a JSON exception")
                                    .ConfigureAwait(false);

        Assert.IsInstanceOfType<InvalidOperationException>(exception.InnerException, $"The refusal of case '{label}' must keep the decoding error as inner exception");
    }

    /// <summary>
    /// Verify malformed JSON is still refused as a JSON exception
    /// </summary>
    /// <param name="json">Malformed body</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("""{ "a": """)]
    [DataRow("{")]
    [DataRow("nul")]
    [DataRow("")]
    public async Task RemoteJsonDocumentParseAsyncWhenJsonIsMalformedThrowsJsonExceptionAsync(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        await Assert.ThrowsAsync<JsonException>(() => RemoteJsonDocument.ParseAsync(stream, TestContext.CancellationToken),
                                                $"Malformed JSON '{json}' must be refused as a JSON exception")
                    .ConfigureAwait(false);
    }

    /// <summary>
    /// Verify malformed JSON is refused with the fixed message and the parser's position only
    /// </summary>
    /// <param name="label">Name of the case</param>
    /// <param name="json">Malformed JSON</param>
    /// <param name="remoteText">Remote text the input must carry</param>
    /// <param name="expectedMessage">Expected message</param>
    /// <param name="expectedLineNumber">Expected line</param>
    /// <param name="expectedBytePosition">Expected byte position</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    [DynamicData(nameof(ExternalJsonTests.GetMalformedDocuments), typeof(ExternalJsonTests), DynamicDataDisplayName = nameof(ExternalJsonTests.GetRowDisplayName), DynamicDataDisplayNameDeclaringType = typeof(ExternalJsonTests))]
    public async Task RemoteJsonDocumentParseAsyncWhenJsonIsMalformedThrowsFixedMessageAsync(string label,
                                                                                             string json,
                                                                                             string remoteText,
                                                                                             string expectedMessage,
                                                                                             long expectedLineNumber,
                                                                                             long expectedBytePosition)
    {
        Assert.Contains(remoteText, json, $"The input of case '{label}' must still carry its remote text");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var exception = await Assert.ThrowsAsync<Exception>(() => RemoteJsonDocument.ParseAsync(stream, TestContext.CancellationToken),
                                                            $"Case '{label}' must be refused")
                                    .ConfigureAwait(false);

        JsonParseFailureAssert.IsRewritten(exception, expectedMessage, expectedLineNumber, expectedBytePosition, $"The refusal of case '{label}'");
    }

    #endregion // Methods

    #region Static methods

    /// <summary>
    /// Bodies whose strings decode
    /// </summary>
    /// <returns>Rows of case name and body</returns>
    public static IEnumerable<object[]> GetDecodableBodies()
    {
        yield return ["surrogate pair escape", Encoding.UTF8.GetBytes($"{{ \"a\": \"{Backslash}ud83d{Backslash}ude00\" }}"), true];
        yield return ["control character escape", Encoding.UTF8.GetBytes("""{ "a": "\u001b" }"""), true];
        yield return ["raw four byte character", CreateBody([(byte)'{', (byte)'"', (byte)'a', (byte)'"', (byte)':', (byte)'"', 0xF0, 0x9F, 0x98, 0x80, (byte)'"', (byte)'}']), false];
        yield return ["empty object", Encoding.UTF8.GetBytes("{}"), false];
    }

    /// <summary>
    /// Bodies with one string that does not decode
    /// </summary>
    /// <returns>Rows of case name, body and whether the case is an escape</returns>
    public static IEnumerable<object[]> GetUndecodableBodies()
    {
        yield return ["lone high surrogate", Encoding.UTF8.GetBytes("""{ "a": "\ud800" }"""), true];
        yield return ["lone high surrogate upper case", Encoding.UTF8.GetBytes("""{ "a": "\uD800" }"""), true];
        yield return ["lone low surrogate", Encoding.UTF8.GetBytes("""{ "a": "\udc00" }"""), true];
        yield return ["reversed pair", Encoding.UTF8.GetBytes("""{ "a": "\udc00\ud800" }"""), true];
        yield return ["high surrogate then plain character", Encoding.UTF8.GetBytes("""{ "a": "\ud800a" }"""), true];
        yield return ["property name", Encoding.UTF8.GetBytes("""{ "\ud800": 1 }"""), true];
        yield return ["array element", Encoding.UTF8.GetBytes("""{ "a": [ "x", "\ud800" ] }"""), true];
        yield return ["three levels deep", Encoding.UTF8.GetBytes("""{ "a": { "b": { "c": "\ud800" } } }"""), true];
        yield return ["raw byte 0xFF", CreateBody([(byte)'{', (byte)'"', (byte)'a', (byte)'"', (byte)':', (byte)'"', 0xFF, (byte)'"', (byte)'}']), false];
        yield return ["raw encoded surrogate", CreateBody([(byte)'{', (byte)'"', (byte)'a', (byte)'"', (byte)':', (byte)'"', 0xED, 0xA0, 0x80, (byte)'"', (byte)'}']), false];
    }

    /// <summary>
    /// Create a body from bytes
    /// </summary>
    /// <param name="bytes">Bytes of the body</param>
    /// <returns>Body</returns>
    private static byte[] CreateBody(byte[] bytes)
    {
        return bytes;
    }

    #endregion // Static methods
}