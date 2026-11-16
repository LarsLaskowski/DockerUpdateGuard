using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// How the second of two scans of a container on an alias tag behaves
/// </summary>
internal sealed class AliasScanSecondRun
{
    #region Properties

    /// <summary>
    /// Image reference the container has in the second scan; the reference of the first scan when omitted
    /// </summary>
    public string? ImageReference { get; init; }

    /// <summary>
    /// Container identifier in the second scan; the container of the first scan when omitted
    /// </summary>
    public string? ContainerId { get; init; }

    /// <summary>
    /// Exception the image inspect of the second scan throws, if any
    /// </summary>
    public Exception? InspectException { get; init; }

    /// <summary>
    /// Exception the image catalog call of the second scan throws, if any
    /// </summary>
    public Exception? CatalogException { get; init; }

    /// <summary>
    /// Answer of the tag list lookup of the second scan; the answer of the first scan when omitted
    /// </summary>
    public Func<Task<ExternalOperationResult<IReadOnlyList<DockerHubTagData>>>>? Tags { get; init; }

    #endregion // Properties
}