namespace DockerUpdateGuard.Images.Data;

/// <summary>
/// Manifest metadata used by update evaluation and base-image resolution
/// </summary>
internal sealed class ManifestMetadata
{
    #region Properties

    /// <summary>
    /// Resolved manifest digest
    /// </summary>
    public string? Digest { get; set; }

    /// <summary>
    /// Resolved config blob digest
    /// </summary>
    public string? ConfigDigest { get; set; }

    /// <summary>
    /// Optional created timestamp
    /// </summary>
    public DateTimeOffset? PublishedAtUtc { get; set; }

    #endregion // Properties
}