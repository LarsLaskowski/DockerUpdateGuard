using System.Text.Json;

namespace DockerUpdateGuard.Images.Helper;

/// <summary>
/// Chooses the platform manifest of a multi-platform image index by one rule shared by the registry clients
/// </summary>
internal static class ManifestPlatformSelector
{
    #region Methods

    // Skeleton: removed with the implementation
#pragma warning disable S1172
    /// <summary>
    /// Select the digest of the manifest that matches the requested platform
    /// </summary>
    /// <param name="manifestsElement">The manifests array of an image index</param>
    /// <param name="operatingSystem">Requested operating system</param>
    /// <param name="architecture">Requested architecture</param>
    /// <returns>Digest of the selected manifest or null when none can be selected</returns>
    public static string? SelectManifestDigest(JsonElement manifestsElement, string? operatingSystem, string? architecture)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }
#pragma warning restore S1172

    #endregion // Methods
}