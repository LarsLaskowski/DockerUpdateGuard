using DockerUpdateGuard.Images;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="OwnImageAlreadyTrackedException"/>
/// </summary>
[TestClass]
public class OwnImageAlreadyTrackedExceptionTests
{
    #region Methods

    /// <summary>
    /// Verify the constructor with an image identifier keeps the message and the identifier
    /// </summary>
    [TestMethod]
    public void OwnImageAlreadyTrackedExceptionWithImageIdKeepsMessageAndIdentifier()
    {
        var imageId = Guid.NewGuid();
        var exception = new OwnImageAlreadyTrackedException("Tracked as own image", imageId);

        Assert.AreEqual("Tracked as own image", exception.Message, "The message must be kept");
        Assert.AreEqual(imageId, exception.ObservedImageId, "The identifier must be kept");
        Assert.IsInstanceOfType<ReportableException>(exception, "The exception must be reportable");
    }

    /// <summary>
    /// Verify the message-only constructor keeps the message and leaves the identifier empty
    /// </summary>
    [TestMethod]
    public void OwnImageAlreadyTrackedExceptionWithMessageKeepsMessageAndEmptyIdentifier()
    {
        var exception = new OwnImageAlreadyTrackedException("Tracked as own image");

        Assert.AreEqual("Tracked as own image", exception.Message, "The message must be kept");
        Assert.AreEqual(Guid.Empty, exception.ObservedImageId, "The identifier must be empty");
    }

    /// <summary>
    /// Verify the constructor with an inner exception keeps the message and the inner exception
    /// </summary>
    [TestMethod]
    public void OwnImageAlreadyTrackedExceptionWithInnerExceptionKeepsMessageAndInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var exception = new OwnImageAlreadyTrackedException("Tracked as own image", inner);

        Assert.AreEqual("Tracked as own image", exception.Message, "The message must be kept");
        Assert.AreSame(inner, exception.InnerException, "The inner exception must be kept");
        Assert.AreEqual(Guid.Empty, exception.ObservedImageId, "The identifier must be empty");
    }

    /// <summary>
    /// Verify the parameterless constructor leaves the identifier and the inner exception empty
    /// </summary>
    [TestMethod]
    public void OwnImageAlreadyTrackedExceptionParameterlessHasEmptyIdentifierAndNoInnerException()
    {
        var exception = new OwnImageAlreadyTrackedException();

        Assert.AreEqual(Guid.Empty, exception.ObservedImageId, "The identifier must be empty");
        Assert.IsNull(exception.InnerException, "There must be no inner exception");
    }

    #endregion // Methods
}