using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Registration rejection raised when the reference is already tracked as an own image, whose message is safe to show to users
/// </summary>
public sealed class OwnImageAlreadyTrackedException : ReportableException
{
    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    public OwnImageAlreadyTrackedException()
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Error message</param>
    public OwnImageAlreadyTrackedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="innerException">Inner exception</param>
    public OwnImageAlreadyTrackedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="observedImageId">Identifier of the tracked own image</param>
    public OwnImageAlreadyTrackedException(string message, Guid observedImageId)
        : base(message)
    {
        ObservedImageId = observedImageId;
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Identifier of the tracked own image
    /// </summary>
    public Guid ObservedImageId { get; }

    #endregion // Properties
}