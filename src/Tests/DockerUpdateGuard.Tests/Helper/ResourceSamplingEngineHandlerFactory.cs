using System.Net;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Creates Docker Engine HTTP test doubles for resource sampling with three running containers
/// </summary>
internal static class ResourceSamplingEngineHandlerFactory
{
    #region Constants

    /// <summary>
    /// Container list with the running containers c1, c2 and c3
    /// </summary>
    public const string ThreeContainersJson = """
                                              [
                                                { "Id": "c1", "Names": ["/web-1"] },
                                                { "Id": "c2", "Names": ["/web-2"] },
                                                { "Id": "c3", "Names": ["/web-3"] }
                                              ]
                                              """;

    #endregion // Constants

    #region Static methods

    /// <summary>
    /// Create a handler answering the container list, the stats of every container and a missing host info
    /// </summary>
    /// <param name="failingContainerIds">Containers whose stats request fails</param>
    /// <param name="failureKind">Kind of the failure: Timeout, Transport, NonSuccess, InvalidJson or WrongShape</param>
    /// <param name="requestedPaths">List receiving the path of every request</param>
    /// <returns>Handler</returns>
    public static DelegateHttpMessageHandler Create(IReadOnlyCollection<string> failingContainerIds, string failureKind, List<string> requestedPaths)
    {
        return new DelegateHttpMessageHandler(request => Respond(request, failingContainerIds, failureKind, requestedPaths));
    }

    /// <summary>
    /// Create a stats answer of a container
    /// </summary>
    /// <param name="read">Timestamp of the stats</param>
    /// <param name="totalBytes">Received and transmitted byte total of the one network</param>
    /// <returns>Response</returns>
    public static HttpResponseMessage CreateStatsResponse(string read, long totalBytes)
    {
        return CreateJsonResponse($$"""{ "read": "{{read}}", "networks": { "eth0": { "rx_bytes": {{totalBytes}}, "tx_bytes": {{totalBytes}} } } }""");
    }

    /// <summary>
    /// Create a successful JSON response
    /// </summary>
    /// <param name="json">JSON content</param>
    /// <returns>Response</returns>
    public static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
               {
                   Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
               };
    }

    /// <summary>
    /// Create the failing answer of a stats request, or throw the failure the request ends with
    /// </summary>
    /// <param name="failureKind">Kind of the failure: Timeout, Transport, NonSuccess, InvalidJson or WrongShape</param>
    /// <returns>Response</returns>
    public static HttpResponseMessage CreateFailingResponse(string failureKind)
    {
        return failureKind switch
               {
                   "Timeout" => throw new TaskCanceledException("The request timed out"),
                   "Transport" => throw new HttpRequestException("The connection broke"),
                   "NonSuccess" => new HttpResponseMessage(HttpStatusCode.InternalServerError),
                   "InvalidJson" => CreateJsonResponse("""{ "read": """),
                   _ => CreateJsonResponse("[]"),
               };
    }

    /// <summary>
    /// Answer one request of the engine double
    /// </summary>
    /// <param name="request">Request</param>
    /// <param name="failingContainerIds">Containers whose stats request fails</param>
    /// <param name="failureKind">Kind of the failure</param>
    /// <param name="requestedPaths">List receiving the path of every request</param>
    /// <returns>Response</returns>
    private static HttpResponseMessage Respond(HttpRequestMessage request,
                                               IReadOnlyCollection<string> failingContainerIds,
                                               string failureKind,
                                               List<string> requestedPaths)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        requestedPaths.Add(path);

        if (path.EndsWith("/containers/json", StringComparison.Ordinal))
        {
            return CreateJsonResponse(ThreeContainersJson);
        }

        if (path.EndsWith("/stats", StringComparison.Ordinal) == false)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (failingContainerIds.Any(containerId => path.EndsWith($"/containers/{containerId}/stats", StringComparison.Ordinal)))
        {
            return CreateFailingResponse(failureKind);
        }

        return CreateStatsResponse("2025-06-01T12:00:00Z", 1000);
    }

    #endregion // Static methods
}