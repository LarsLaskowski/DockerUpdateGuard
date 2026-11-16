using System.Reflection;
using System.Text;
using System.Text.Json;

using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ExternalJson"/>
/// </summary>
[TestClass]
public class ExternalJsonTests
{
    #region Constants

    /// <summary>
    /// Message of a parse failure whose position is unknown
    /// </summary>
    private const string MessageWithoutPosition = "Remote JSON is not valid";

    /// <summary>
    /// Message of the refusal of a response whose character set is not valid
    /// </summary>
    private const string InvalidCharsetMessage = "The character set of the remote response is not valid";

    /// <summary>
    /// Number of characters the long literal remainder carries
    /// </summary>
    private const int LongRemainderLength = 100000;

    /// <summary>
    /// Valid JSON whose property name differs in case from the property of <see cref="ExternalJsonValue"/>
    /// </summary>
    private const string UpperCaseNameJson = """{ "VALUE": 5 }""";

    /// <summary>
    /// Valid JSON object with one number property
    /// </summary>
    private const string ValidDocumentJson = """{ "a": 1 }""";

    #endregion // Constants

    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Malformed inputs every parse method refuses: a forged log line, a long literal remainder and an escape sequence
    /// </summary>
    /// <returns>Rows of case name, JSON, remote text the input must carry, expected message, line and byte position</returns>
    public static IEnumerable<object[]> GetMalformedDocuments()
    {
        yield return ["forged log line", "{\"a\":nul" + "\r\n" + "FAKE LOG LINE}", "\r\n" + "FAKE LOG LINE", "Remote JSON is not valid at line 0, byte 8 (counted from 0)", 0L, 8L];
        yield return ["long literal remainder", "{\"a\":tru" + new string('A', LongRemainderLength), new string('A', LongRemainderLength), "Remote JSON is not valid at line 0, byte 8 (counted from 0)", 0L, 8L];
        yield return ["escape sequence", "{\"a\":fals" + JsonParseFailureAssert.CreateEscape() + "[31mRED}", JsonParseFailureAssert.CreateEscape() + "[31m", "Remote JSON is not valid at line 0, byte 9 (counted from 0)", 0L, 9L];
    }

    /// <summary>
    /// Malformed inputs the serializer methods refuse: those of <see cref="GetMalformedDocuments"/> and a dictionary key with an escaped line break
    /// </summary>
    /// <returns>Rows of case name, JSON, remote text the input must carry, expected message, line and byte position</returns>
    public static IEnumerable<object[]> GetMalformedValues()
    {
        foreach (var row in GetMalformedDocuments())
        {
            yield return row;
        }

        yield return ["dictionary key", """{"k\r\nFAKE LINE":"x"}""", """\r\nFAKE LINE""", "Remote JSON is not valid at line 0, byte 21 (counted from 0)", 0L, 21L];
    }

    /// <summary>
    /// Positions of a parser's exception of which the line, the byte position or both are unknown
    /// </summary>
    /// <returns>Rows of case name, line and byte position</returns>
    public static IEnumerable<object?[]> GetUnknownPositions()
    {
        yield return ["neither", null, null];
        yield return ["line only", 0L, null];
        yield return ["byte position only", null, 3L];
    }

    /// <summary>
    /// Name a data row by its case name, so a long input does not become part of the test name
    /// </summary>
    /// <param name="methodInfo">Test method</param>
    /// <param name="data">Data row</param>
    /// <returns>Display name</returns>
    public static string GetRowDisplayName(MethodInfo methodInfo, object?[] data)
    {
        return $"{methodInfo.Name} ({data[0]})";
    }

    /// <summary>
    /// Create JSON content of an HTTP response
    /// </summary>
    /// <param name="json">JSON text</param>
    /// <returns>Content</returns>
    private static StringContent CreateJsonContent(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify a malformed stream is refused with the fixed message and the parser's position only
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
    [DynamicData(nameof(GetMalformedDocuments), DynamicDataDisplayName = nameof(GetRowDisplayName))]
    public async Task ExternalJsonParseAsyncWhenJsonIsMalformedThrowsFixedMessageAsync(string label,
                                                                                       string json,
                                                                                       string remoteText,
                                                                                       string expectedMessage,
                                                                                       long expectedLineNumber,
                                                                                       long expectedBytePosition)
    {
        Assert.Contains(remoteText, json, $"The input of case '{label}' must still carry its remote text");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var exception = await Assert.ThrowsAsync<Exception>(() => ExternalJson.ParseAsync(stream, TestContext.CancellationToken),
                                                            $"Case '{label}' must be refused")
                                    .ConfigureAwait(false);

        JsonParseFailureAssert.IsRewritten(exception, expectedMessage, expectedLineNumber, expectedBytePosition, $"The refusal of case '{label}'");
    }

    /// <summary>
    /// Verify malformed bytes are refused with the fixed message and the parser's position only
    /// </summary>
    /// <param name="label">Name of the case</param>
    /// <param name="json">Malformed JSON</param>
    /// <param name="remoteText">Remote text the input must carry</param>
    /// <param name="expectedMessage">Expected message</param>
    /// <param name="expectedLineNumber">Expected line</param>
    /// <param name="expectedBytePosition">Expected byte position</param>
    [TestMethod]
    [TestCategory("AC1")]
    [DynamicData(nameof(GetMalformedDocuments), DynamicDataDisplayName = nameof(GetRowDisplayName))]
    public void ExternalJsonParseWhenJsonIsMalformedThrowsFixedMessage(string label,
                                                                       string json,
                                                                       string remoteText,
                                                                       string expectedMessage,
                                                                       long expectedLineNumber,
                                                                       long expectedBytePosition)
    {
        Assert.Contains(remoteText, json, $"The input of case '{label}' must still carry its remote text");

        var bytes = Encoding.UTF8.GetBytes(json);

        var exception = Assert.Throws<Exception>(() => ExternalJson.Parse(bytes), $"Case '{label}' must be refused");

        JsonParseFailureAssert.IsRewritten(exception, expectedMessage, expectedLineNumber, expectedBytePosition, $"The refusal of case '{label}'");
    }

    /// <summary>
    /// Verify malformed response content is refused with the fixed message and the parser's position only
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
    [DynamicData(nameof(GetMalformedValues), DynamicDataDisplayName = nameof(GetRowDisplayName))]
    public async Task ExternalJsonReadFromJsonAsyncWhenJsonIsMalformedThrowsFixedMessageAsync(string label,
                                                                                              string json,
                                                                                              string remoteText,
                                                                                              string expectedMessage,
                                                                                              long expectedLineNumber,
                                                                                              long expectedBytePosition)
    {
        Assert.Contains(remoteText, json, $"The input of case '{label}' must still carry its remote text");

        using var content = CreateJsonContent(json);

        var exception = await Assert.ThrowsAsync<Exception>(() => ExternalJson.ReadFromJsonAsync<Dictionary<string, int>>(content, TestContext.CancellationToken),
                                                            $"Case '{label}' must be refused")
                                    .ConfigureAwait(false);

        JsonParseFailureAssert.IsRewritten(exception, expectedMessage, expectedLineNumber, expectedBytePosition, $"The refusal of case '{label}'");
    }

    /// <summary>
    /// Verify a malformed string is refused with the fixed message and the parser's position only
    /// </summary>
    /// <param name="label">Name of the case</param>
    /// <param name="json">Malformed JSON</param>
    /// <param name="remoteText">Remote text the input must carry</param>
    /// <param name="expectedMessage">Expected message</param>
    /// <param name="expectedLineNumber">Expected line</param>
    /// <param name="expectedBytePosition">Expected byte position</param>
    [TestMethod]
    [TestCategory("AC1")]
    [DynamicData(nameof(GetMalformedValues), DynamicDataDisplayName = nameof(GetRowDisplayName))]
    public void ExternalJsonDeserializeWhenJsonIsMalformedThrowsFixedMessage(string label,
                                                                             string json,
                                                                             string remoteText,
                                                                             string expectedMessage,
                                                                             long expectedLineNumber,
                                                                             long expectedBytePosition)
    {
        Assert.Contains(remoteText, json, $"The input of case '{label}' must still carry its remote text");

        var exception = Assert.Throws<Exception>(() => ExternalJson.Deserialize<Dictionary<string, int>>(json), $"Case '{label}' must be refused");

        JsonParseFailureAssert.IsRewritten(exception, expectedMessage, expectedLineNumber, expectedBytePosition, $"The refusal of case '{label}'");
    }

    /// <summary>
    /// Verify a parser's exception whose line or byte position is unknown is rewritten into the message without position
    /// </summary>
    /// <param name="label">Name of the case</param>
    /// <param name="lineNumber">Line of the parser's exception</param>
    /// <param name="bytePosition">Byte position of the parser's exception</param>
    [TestMethod]
    [TestCategory("AC2")]
    [DynamicData(nameof(GetUnknownPositions))]
    public void ExternalJsonCreateParseFailureWithoutPositionReturnsMessageWithoutPosition(string label, long? lineNumber, long? bytePosition)
    {
        var remoteText = "'nul" + "\r\n" + "FAKE LOG LINE' is an invalid JSON literal.";
        var parserException = new JsonException(remoteText,
                                                "$['k" + "\r\n" + "FAKE LINE']",
                                                lineNumber,
                                                bytePosition,
                                                new InvalidOperationException(remoteText));

        Assert.Contains("\r\n", parserException.Message, $"The parser's message of case '{label}' must still carry the line break");

        var failure = ExternalJson.CreateParseFailure(parserException);

        JsonParseFailureAssert.IsRewritten(failure, MessageWithoutPosition, lineNumber, bytePosition, $"The rewritten failure of case '{label}'");
    }

    /// <summary>
    /// Verify response content is read with the web defaults, which match property names regardless of case
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ExternalJsonReadFromJsonAsyncWhenNamesDifferInCaseReadsValueAsync()
    {
        using var content = CreateJsonContent(UpperCaseNameJson);

        var value = await ExternalJson.ReadFromJsonAsync<ExternalJsonValue>(content, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(value, "Valid response content must be read into a value");
        Assert.AreEqual(5, value.Value, "Response content must be read with the web defaults, which match property names regardless of case");
    }

    /// <summary>
    /// Verify a string is deserialized with the general defaults, which match property names by exact case
    /// </summary>
    [TestMethod]
    [TestCategory("AC4")]
    public void ExternalJsonDeserializeWhenNamesDifferInCaseLeavesValueUnset()
    {
        var value = ExternalJson.Deserialize<ExternalJsonValue>(UpperCaseNameJson);

        Assert.IsNotNull(value, "A valid string must be deserialized into a value");
        Assert.AreEqual(0, value.Value, "A string must be deserialized with the general defaults, which match property names by exact case");
    }

    /// <summary>
    /// Verify a valid stream is returned as a parsed document
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task ExternalJsonParseAsyncWhenJsonIsValidReturnsDocumentAsync()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ValidDocumentJson));

        using var document = await ExternalJson.ParseAsync(stream, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(1, document.RootElement.GetProperty("a").GetInt32(), "A valid stream must be returned as the parsed document");
    }

    /// <summary>
    /// Verify valid bytes are returned as a parsed document
    /// </summary>
    [TestMethod]
    [TestCategory("AC4")]
    public void ExternalJsonParseWhenJsonIsValidReturnsDocument()
    {
        using var document = ExternalJson.Parse(Encoding.UTF8.GetBytes(ValidDocumentJson));

        Assert.AreEqual(1, document.RootElement.GetProperty("a").GetInt32(), "Valid bytes must be returned as the parsed document");
    }

    /// <summary>
    /// Verify response content whose character set is a quoted string with an escape character is refused with a fixed message and no inner exception
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC6")]
    public async Task ExternalJsonReadFromJsonAsyncWhenCharsetIsInvalidThrowsFixedMessageAsync()
    {
        var escape = JsonParseFailureAssert.CreateEscape();

        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(ValidDocumentJson));

        content.Headers.TryAddWithoutValidation("Content-Type", $"application/json; charset=\"x{escape}[31m\"");

        Assert.Contains(escape, string.Join(",", content.Headers.GetValues("Content-Type")), "The test input must still carry the escape character in its character set");

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ExternalJson.ReadFromJsonAsync<ExternalJsonValue>(content, TestContext.CancellationToken),
                                                                                   "A character set that is not valid must be refused as an invalid operation")
                                    .ConfigureAwait(false);

        Assert.AreEqual(InvalidCharsetMessage, exception.Message, "The refusal must carry the fixed message");
        Assert.IsNull(exception.InnerException, "The refusal must carry no inner exception, which quotes the remote character set");
        Assert.DoesNotContain(escape, exception.ToString(), "The refusal must carry no escape character anywhere");
    }

    #endregion // Methods
}