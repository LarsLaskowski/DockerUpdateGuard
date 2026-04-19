namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Generic external operation result
/// </summary>
/// <typeparam name="T">Payload type</typeparam>
public class ExternalOperationResult<T>
{
    #region Properties

    /// <summary>
    /// Result status
    /// </summary>
    public ExternalOperationStatus Status { get; private set; }

    /// <summary>
    /// Optional payload
    /// </summary>
    public T? Data { get; private set; }

    /// <summary>
    /// Optional human readable message
    /// </summary>
    public string? Message { get; private set; }

    #endregion // Properties

    #region Static methods

    /// <summary>
    /// Create a successful result
    /// </summary>
    /// <param name="data">Payload</param>
    /// <returns>Result</returns>
    public static ExternalOperationResult<T> Succeeded(T data)
    {
        return new ExternalOperationResult<T>
               {
                   Status = ExternalOperationStatus.Succeeded,
                   Data = data,
               };
    }

    /// <summary>
    /// Create a not configured result
    /// </summary>
    /// <param name="message">Result message</param>
    /// <returns>Result</returns>
    public static ExternalOperationResult<T> NotConfigured(string message)
    {
        return Create(ExternalOperationStatus.NotConfigured, message);
    }

    /// <summary>
    /// Create an unsupported result
    /// </summary>
    /// <param name="message">Result message</param>
    /// <returns>Result</returns>
    public static ExternalOperationResult<T> Unsupported(string message)
    {
        return Create(ExternalOperationStatus.Unsupported, message);
    }

    /// <summary>
    /// Create a not found result
    /// </summary>
    /// <param name="message">Result message</param>
    /// <returns>Result</returns>
    public static ExternalOperationResult<T> NotFound(string message)
    {
        return Create(ExternalOperationStatus.NotFound, message);
    }

    /// <summary>
    /// Create a failed result
    /// </summary>
    /// <param name="message">Result message</param>
    /// <returns>Result</returns>
    public static ExternalOperationResult<T> Failed(string message)
    {
        return Create(ExternalOperationStatus.Failed, message);
    }

    /// <summary>
    /// Create an unknown result
    /// </summary>
    /// <param name="message">Result message</param>
    /// <returns>Result</returns>
    public static ExternalOperationResult<T> Unknown(string message)
    {
        return Create(ExternalOperationStatus.Unknown, message);
    }

    /// <summary>
    /// Create a result without payload
    /// </summary>
    /// <param name="status">Result status</param>
    /// <param name="message">Result message</param>
    /// <returns>Result</returns>
    private static ExternalOperationResult<T> Create(ExternalOperationStatus status, string message)
    {
        return new ExternalOperationResult<T>
               {
                   Status = status,
                   Message = message,
               };
    }

    #endregion // Static methods
}