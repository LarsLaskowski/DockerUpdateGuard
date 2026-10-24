namespace DockerUpdateGuard.Portainer;

/// <summary>
/// Portainer login failure composed by the client, whose message is safe to show to users
/// </summary>
public sealed class PortainerAuthenticationException : Exception
{
    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    public PortainerAuthenticationException()
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Error message</param>
    public PortainerAuthenticationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="innerException">Inner exception</param>
    public PortainerAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    #endregion // Constructors
}