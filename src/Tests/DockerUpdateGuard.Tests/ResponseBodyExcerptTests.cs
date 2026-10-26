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

    #endregion // Methods
}