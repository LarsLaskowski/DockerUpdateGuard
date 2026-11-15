using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DeadlineReadStream"/>
/// </summary>
[TestClass]
public class DeadlineReadStreamTests
{
    #region Properties

    /// <summary>
    /// Test context
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Create a stream over a text body
    /// </summary>
    /// <param name="text">Body text</param>
    /// <returns>Stream</returns>
    private static DeadlineReadStream CreateStream(string text)
    {
        return new DeadlineReadStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)), new StringContent(text), new CancellationTokenSource());
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify the stream is read-only and not seekable
    /// </summary>
    [TestMethod]
    public void DeadlineReadStreamCapabilitiesAreReadOnlyAndNotSeekable()
    {
        using var stream = CreateStream("abc");

        Assert.IsTrue(stream.CanRead, "The stream must be readable");
        Assert.IsFalse(stream.CanSeek, "The stream must not be seekable");
        Assert.IsFalse(stream.CanWrite, "The stream must not be writable");
    }

    /// <summary>
    /// Verify the members a forward-only read stream does not support fail closed
    /// </summary>
    [TestMethod]
    public void DeadlineReadStreamUnsupportedMembersThrowNotSupported()
    {
        using var stream = CreateStream("abc");
        var buffer = new byte[2];

        Assert.ThrowsExactly<NotSupportedException>(() => _ = stream.Length, "Length must not be supported");
        Assert.ThrowsExactly<NotSupportedException>(() => _ = stream.Position, "Reading the position must not be supported");
        Assert.ThrowsExactly<NotSupportedException>(() => stream.Position = 0, "Setting the position must not be supported");
        Assert.ThrowsExactly<NotSupportedException>(() => stream.Read(buffer, 0, buffer.Length), "A synchronous read must not be supported");
        Assert.ThrowsExactly<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin), "Seek must not be supported");
        Assert.ThrowsExactly<NotSupportedException>(() => stream.SetLength(1), "SetLength must not be supported");
        Assert.ThrowsExactly<NotSupportedException>(() => stream.Write(buffer, 0, buffer.Length), "Write must not be supported");
    }

    /// <summary>
    /// Verify Flush does nothing
    /// </summary>
    [TestMethod]
    public void DeadlineReadStreamFlushDoesNothing()
    {
        using var stream = CreateStream("abc");

        stream.Flush();

        Assert.IsTrue(stream.CanRead, "The stream must stay usable after a flush");
    }

    /// <summary>
    /// Verify a read with a token that cannot be cancelled and a read through the array overload return the bytes
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DeadlineReadStreamReadWithoutCancellableTokenReturnsBytesAsync()
    {
        using var stream = CreateStream("abcd");
        var memoryBuffer = new byte[2];
        var arrayBuffer = new byte[4];

        var first = await stream.ReadAsync(memoryBuffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);
        Func<byte[], int, int, CancellationToken, Task<int>> arrayRead = stream.ReadAsync;

        var second = await arrayRead(arrayBuffer, 1, 2, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(2, first, "The read must return the requested bytes");
        Assert.AreSequenceEqual("ab"u8.ToArray(), memoryBuffer, "The first read must return the first bytes");
        Assert.AreEqual(2, second, "The array overload must return the requested bytes");
        Assert.AreSequenceEqual(new byte[] { 0, (byte)'c', (byte)'d', 0 }, arrayBuffer, "The array overload must fill the requested range");
    }

    /// <summary>
    /// Verify a fired deadline cancels a read whose caller token cannot be cancelled
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task DeadlineReadStreamDeadlineFiredCancelsReadAsync()
    {
        using var deadline = new CancellationTokenSource();
        using var content = new StringContent(string.Empty);
        using var stream = new DeadlineReadStream(new System.IO.Pipelines.Pipe().Reader.AsStream(), content, deadline);

        var readTask = stream.ReadAsync(new byte[1].AsMemory(), CancellationToken.None).AsTask();

        await deadline.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAsync<OperationCanceledException>(() => readTask, "The fired deadline must end the pending read").ConfigureAwait(false);
    }

    /// <summary>
    /// Verify disposing twice disposes the deadline source and the inner stream once without failing
    /// </summary>
    [TestMethod]
    public void DeadlineReadStreamDisposedTwiceReleasesResourcesOnce()
    {
        using var deadline = new CancellationTokenSource();
        using var inner = new MemoryStream();
        using var content = new StringContent("x");

        var stream = new DeadlineReadStream(inner, content, deadline);

        stream.Dispose();
        stream.Dispose();

        Assert.IsFalse(inner.CanRead, "The inner stream must be disposed");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = deadline.Token.WaitHandle, "The deadline source must be disposed");
    }

    #endregion // Methods
}