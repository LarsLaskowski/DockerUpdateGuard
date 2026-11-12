namespace DockerUpdateGuard.Infrastructure;

/// <summary>
/// Factories for generic external operation results
/// </summary>
public static class ExternalOperationResult
{
    #region Static methods

    /// <summary>
    /// Create a successful result
    /// </summary>
    /// <typeparam name="T">Payload type</typeparam>
    /// <param name="data">Payload</param>
    /// <returns>Result</returns>
    public static ExternalOperationResult<T> Succeeded<T>(T data)
    {
        return new ExternalOperationResult<T>(ExternalOperationStatus.Succeeded, data, null);
    }

    /// <summary>
    /// Create a not configured result
    /// </summary>
    /// <typeparam name="T">Payload type</typeparam>
    /// <param name="message">Result message</param>
    /// <returns>Result</returns>
    public static ExternalOperationResult<T> NotConfigured<T>(string message)
    {
        return Create<T>(ExternalOperationStatus.NotConfigured, message);
    }

    /// <summary>
    /// Create an unsupported result
    /// </summary>
    /// <typeparam name="T">Payload type</typeparam>
    /// <param name="message">Result message</param>
    /// <returns>Result</returns>
    public static ExternalOperationResult<T> Unsupported<T>(string message)
    {
        return Create<T>(ExternalOperationStatus.Unsupported, message);
    }

    /// <summary>
    /// Create a not found result
    /// </summary>
    /// <typeparam name="T">Payload type</typeparam>
    /// <param name="message">Result message</param>
    /// <returns>Result</returns>
    public static ExternalOperationResult<T> NotFound<T>(string message)
    {
        return Create<T>(ExternalOperationStatus.NotFound, message);
    }

    /// <summary>
    /// Create a failed result
    /// </summary>
    /// <typeparam name="T">Payload type</typeparam>
    /// <param name="message">Result message</param>
    /// <returns>Result</returns>
    public static ExternalOperationResult<T> Failed<T>(string message)
    {
        return Create<T>(ExternalOperationStatus.Failed, message);
    }

    /// <summary>
    /// Create an unknown result
    /// </summary>
    /// <typeparam name="T">Payload type</typeparam>
    /// <param name="message">Result message</param>
    /// <returns>Result</returns>
    public static ExternalOperationResult<T> Unknown<T>(string message)
    {
        return Create<T>(ExternalOperationStatus.Unknown, message);
    }

    /// <summary>
    /// Create a result without payload
    /// </summary>
    /// <typeparam name="T">Payload type</typeparam>
    /// <param name="status">Result status</param>
    /// <param name="message">Result message</param>
    /// <returns>Result</returns>
    private static ExternalOperationResult<T> Create<T>(ExternalOperationStatus status, string message)
    {
        return new ExternalOperationResult<T>(status, default, message);
    }

    #endregion // Static methods
}