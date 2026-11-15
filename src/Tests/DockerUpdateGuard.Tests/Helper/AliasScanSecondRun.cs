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