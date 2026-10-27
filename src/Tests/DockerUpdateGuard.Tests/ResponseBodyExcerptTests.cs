using System.Text;

using DockerUpdateGuard.Infrastructure;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ResponseBodyExcerpt"/>
/// </summary>
[TestClass]
public class ResponseBodyExcerptTests
{
    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify a missing text is returned as the empty string
    /// </summary>
    [TestMethod]
    public void ResponseBodyExcerptTruncateWhenTextIsNullReturnsEmpty()
    {
        var excerpt = ResponseBodyExcerpt.Truncate(null, 10);

        Assert.AreEqual(string.Empty, excerpt, "A missing text must become the empty string so a message never contains the word null");
    }

    /// <summary>
    /// Verify a text within the limit is returned unchanged
    /// </summary>
    /// <param name="text">Text to cut</param>
    /// <param name="maxLength">Maximum length</param>
    [TestMethod]
    [DataRow("abc", 10)]
    [DataRow("abc", 3)]
    [DataRow("", 3)]
    public void ResponseBodyExcerptTruncateWhenTextFitsReturnsTextUnchanged(string text, int maxLength)
    {
        var excerpt = ResponseBodyExcerpt.Truncate(text, maxLength);

        Assert.AreEqual(text, excerpt, "A text of at most the maximum length must stay unchanged and carry no marker");
    }

    /// <summary>
    /// Verify a longer text is cut to its prefix followed by the truncation marker
    /// </summary>
    [TestMethod]
    public void ResponseBodyExcerptTruncateWhenTextIsLongerReturnsPrefixAndMarker()
    {
        var excerpt = ResponseBodyExcerpt.Truncate("abcdef", 3);

        Assert.AreEqual("abc…", excerpt, "A longer text must be cut to its first characters followed by the ellipsis character");
    }

    /// <summary>
    /// Verify a cut that would split a surrogate pair is made before the pair
    /// </summary>
    [TestMethod]
    public void ResponseBodyExcerptTruncateWhenCutSplitsSurrogatePairCutsBeforePair()
    {
        var pair = char.ConvertFromUtf32(0x1F600);
        var text = $"ab{pair}cd";

        var excerpt = ResponseBodyExcerpt.Truncate(text, 3);

        Assert.AreEqual("ab…", excerpt, "A cut between the two halves of a surrogate pair must be moved before the pair");
        Assert.DoesNotContain(pair[0].ToString(), excerpt, "The excerpt must not carry a lone high surrogate");
    }

    /// <summary>
    /// Verify a body within 500 characters is returned whole
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ResponseBodyExcerptReadAsyncWhenBodyFitsReturnsWholeBodyAsync()
    {
        var body = new string('x', ResponseBodyExcerpt.MaxBodyLength);
        using var content = new StringContent(body, Encoding.UTF8, "text/plain");

        var excerpt = await ResponseBodyExcerpt.ReadAsync(content, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(body, excerpt, "A body of exactly the maximum length must be returned whole without a marker");
    }

    /// <summary>
    /// Verify a longer body is returned as its first 500 characters followed by the marker
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ResponseBodyExcerptReadAsyncWhenBodyIsLongerReturnsPrefixAndMarkerAsync()
    {
        using var content = new StringContent(new string('x', ResponseBodyExcerpt.MaxBodyLength + 1), Encoding.UTF8, "text/plain");

        var excerpt = await ResponseBodyExcerpt.ReadAsync(content, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual($"{new string('x', ResponseBodyExcerpt.MaxBodyLength)}…",
                        excerpt,
                        "A longer body must be cut to its first 500 characters followed by the ellipsis character");
    }

    /// <summary>
    /// Verify a body of 1 MiB is cut after reading at most 8192 bytes of the content stream
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ResponseBodyExcerptReadAsyncWhenBodyIsLargeReadsBoundedBytesAsync()
    {
        const long maxBytesRead = 8192;

        using var content = new ByteCountingHttpContent((byte)'x', 1024 * 1024);

        var excerpt = await ResponseBodyExcerpt.ReadAsync(content, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual($"{new string('x', ResponseBodyExcerpt.MaxBodyLength)}…",
                        excerpt,
                        "A large body must be cut to its first 500 characters followed by the ellipsis character");
        Assert.IsGreaterThan(0L, content.BytesRead, "The excerpt must have been read from the content stream");
        Assert.IsLessThanOrEqualTo(maxBytesRead,
                                   content.BytesRead,
                                   "The read must stop within 8192 bytes of a 1 MiB body instead of buffering all of it");
    }

    /// <summary>
    /// Verify every control character is replaced by one space
    /// </summary>
    /// <param name="codePoint">Code point of the control character</param>
    [TestMethod]
    [DataRow(0x000A)]
    [DataRow(0x000D)]
    [DataRow(0x0009)]
    [DataRow(0x000B)]
    [DataRow(0x000C)]
    [DataRow(0x0000)]
    [DataRow(0x001B)]
    [DataRow(0x007F)]
    [DataRow(0x0085)]
    [DataRow(0x009B)]
    [DataRow(0x2028)]
    [DataRow(0x2029)]
    [DataRow(0x202E)]
    [DataRow(0x2066)]
    [DataRow(0x200B)]
    [DataRow(0xFEFF)]
    [DataRow(0x061C)]
    [DataRow(0xE0041)]
    public void ResponseBodyExcerptTruncateWhenTextHasControlCharacterReplacesItWithSpace(int codePoint)
    {
        var character = char.ConvertFromUtf32(codePoint);
        var text = $"a{character}b";

        Assert.Contains(character, text, $"The input must contain the code point {codePoint:X}");

        var excerpt = ResponseBodyExcerpt.Truncate(text, 10);

        Assert.AreEqual("a b", excerpt, $"The code point {codePoint:X} must be replaced by exactly one space");
    }

    /// <summary>
    /// Verify a lone surrogate is replaced by the replacement character
    /// </summary>
    /// <param name="codeUnit">Code unit of the lone surrogate</param>
    [TestMethod]
    [DataRow(0xD800)]
    [DataRow(0xDC00)]
    public void ResponseBodyExcerptTruncateWhenTextHasLoneSurrogateReplacesItWithReplacementCharacter(int codeUnit)
    {
        var surrogate = ((char)codeUnit).ToString();
        var text = $"a{surrogate}b";

        Assert.Contains(surrogate, text, $"The input must contain the lone surrogate {codeUnit:X}");

        var excerpt = ResponseBodyExcerpt.Truncate(text, 10);

        var expected = $"a{char.ConvertFromUtf32(0xFFFD)}b";

        Assert.AreEqual(expected, excerpt, $"The lone surrogate {codeUnit:X} must be replaced by U+FFFD");
    }

    /// <summary>
    /// Verify printable characters are kept
    /// </summary>
    /// <param name="codePoint">Code point of the printable character</param>
    [TestMethod]
    [DataRow(0x00A0)]
    [DataRow(0x00E9)]
    [DataRow(0x1F600)]
    [DataRow(0x2026)]
    [DataRow(0x0020)]
    public void ResponseBodyExcerptTruncateWhenTextHasPrintableCharactersKeepsThem(int codePoint)
    {
        var text = $"a{char.ConvertFromUtf32(codePoint)}b";

        var excerpt = ResponseBodyExcerpt.Truncate(text, 10);

        Assert.AreEqual(text, excerpt, $"The code point {codePoint:X} must be kept");
    }

    /// <summary>
    /// Verify the marker stays when a cut text holds a supplementary control character
    /// </summary>
    [TestMethod]
    public void ResponseBodyExcerptTruncateWhenCutTextHasSupplementaryControlCharacterKeepsMarker()
    {
        var character = char.ConvertFromUtf32(0xE0041);
        var text = $"a{character}bc";

        Assert.Contains(character, text, "The input must contain the tag character");

        var excerpt = ResponseBodyExcerpt.Truncate(text, 3);

        Assert.AreEqual("a …", excerpt, "The pair must be kept whole by the cut, replaced by one space and followed by the marker");
    }

    /// <summary>
    /// Verify no marker is added when the received text fits although the cleaned text is shorter
    /// </summary>
    [TestMethod]
    public void ResponseBodyExcerptTruncateWhenFittingTextHasSupplementaryControlCharacterAddsNoMarker()
    {
        var character = char.ConvertFromUtf32(0xE0041);
        var text = $"a{character}b";

        Assert.Contains(character, text, "The input must contain the tag character");

        var excerpt = ResponseBodyExcerpt.Truncate(text, 4);

        Assert.AreEqual("a b", excerpt, "A received text of exactly the maximum length must carry no marker");
    }

    /// <summary>
    /// Verify no marker is added when a text of the maximum length ends with a control character
    /// </summary>
    [TestMethod]
    public void ResponseBodyExcerptTruncateWhenTextAtLimitEndsWithControlCharacterAddsNoMarker()
    {
        var character = char.ConvertFromUtf32(0x000A);
        var text = $"{new string('x', 499)}{character}";

        Assert.Contains(character, text, "The input must contain the line feed");

        var excerpt = ResponseBodyExcerpt.Truncate(text, 500);

        Assert.AreEqual($"{new string('x', 499)} ", excerpt, "The line feed must become one space and no marker must be added");
    }

    /// <summary>
    /// Verify a body read from content has its control characters replaced
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ResponseBodyExcerptReadAsyncWhenBodyHasControlCharactersReplacesThemAsync()
    {
        var carriageReturn = char.ConvertFromUtf32(0x000D);
        var lineFeed = char.ConvertFromUtf32(0x000A);
        var escape = char.ConvertFromUtf32(0x001B);
        var body = $"line1{carriageReturn}{lineFeed}line2{escape}[31m";

        Assert.Contains(carriageReturn, body, "The body must contain the carriage return");
        Assert.Contains(lineFeed, body, "The body must contain the line feed");
        Assert.Contains(escape, body, "The body must contain the escape character");

        using var content = new StringContent(body, Encoding.UTF8, "text/plain");

        var excerpt = await ResponseBodyExcerpt.ReadAsync(content, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("line1  line2 [31m", excerpt, "Each control character of the body must be replaced by one space");
    }

    /// <summary>
    /// Verify a missing text is returned as null instead of failing
    /// </summary>
    [TestMethod]
    public void ResponseBodyExcerptReplaceControlCharactersWhenTextIsNullReturnsNull()
    {
        var cleaned = ResponseBodyExcerpt.ReplaceControlCharacters(null);

        Assert.IsNull(cleaned, "A missing remote text must stay missing so the callers can keep their null handling");
    }

    #endregion // Methods
}