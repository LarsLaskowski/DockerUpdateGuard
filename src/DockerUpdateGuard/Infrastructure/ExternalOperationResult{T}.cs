namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Generic external operation result
/// </summary>
/// <typeparam name="T">Payload type</typeparam>
public class ExternalOperationResult<T>
{
    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="status">Result status</param>
    /// <param name="data">Optional payload</param>
    /// <param name="message">Optional human readable message</param>
    internal ExternalOperationResult(ExternalOperationStatus status, T? data, string? message)
    {
        Status = status;
        Data = data;
        Message = message;
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Result status
    /// </summary>
    public ExternalOperationStatus Status { get; }

    /// <summary>
    /// Optional payload
    /// </summary>
    public T? Data { get; }

    /// <summary>
    /// Optional human readable message
    /// </summary>
    public string? Message { get; }

    #endregion // Properties
}