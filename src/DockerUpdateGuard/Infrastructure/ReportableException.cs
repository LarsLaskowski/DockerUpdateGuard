namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Exception whose message the application composed itself from fixed text, configuration names and masked URLs only, so that it may be shown to users and stored in results; a message that interpolates an exception message, a response body or an unmasked URL must never be given to it
/// </summary>
public class ReportableException : Exception
{
    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    public ReportableException()
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Composed message</param>
    public ReportableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Composed message</param>
    /// <param name="innerException">Inner exception</param>
    public ReportableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    #endregion // Constructors
}