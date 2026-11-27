using System.Text.Json;

namespace DockerUpdateGuard.Images.Helper;

/// <summary>
/// Chooses the platform manifest of a multi-platform image index by one rule shared by the registry clients
/// </summary>
internal static class ManifestPlatformSelector
{
    #region Constants

    /// <summary>
    /// Platform value of attestation and other non-runnable entries
    /// </summary>
    private const string UnknownPlatformValue = "unknown";

    /// <summary>
    /// Operating system preferred when no requested platform matches
    /// </summary>
    private const string LinuxOperatingSystem = "linux";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Select the digest of the manifest that matches the requested platform
    /// </summary>
    /// <param name="manifestsElement">The manifests array of an image index</param>
    /// <param name="operatingSystem">Requested operating system</param>
    /// <param name="architecture">Requested architecture</param>
    /// <returns>Digest of the selected manifest or null when none can be selected</returns>
    public static string? SelectManifestDigest(JsonElement manifestsElement, string? operatingSystem, string? architecture)
    {
        var requestedOperatingSystem = NormalizeRequestedValue(operatingSystem);
        var requestedArchitecture = NormalizeRequestedValue(architecture);
        var architectureDigest = default(string);
        var linuxDigest = default(string);
        var firstDigest = default(string);

        foreach (var manifest in manifestsElement.EnumerateArray())
        {
            var digest = TryGetString(manifest, "digest");

            if (string.IsNullOrWhiteSpace(digest))
            {
                continue;
            }

            var (manifestOperatingSystem, manifestArchitecture) = ReadPlatform(manifest);

            if (IsUnknown(manifestOperatingSystem) || IsUnknown(manifestArchitecture))
            {
                continue;
            }

            var architectureMatches = IsSame(manifestArchitecture, requestedArchitecture);

            if (architectureMatches && IsSame(manifestOperatingSystem, requestedOperatingSystem))
            {
                return digest;
            }

            if (architectureMatches)
            {
                architectureDigest ??= digest;
            }

            if (IsSame(manifestOperatingSystem, LinuxOperatingSystem))
            {
                linuxDigest ??= digest;
            }

            firstDigest ??= digest;
        }

        return architectureDigest ?? linuxDigest ?? firstDigest;
    }

    /// <summary>
    /// Read the operating system and architecture of an index entry
    /// </summary>
    /// <param name="manifest">Index entry</param>
    /// <returns>Operating system and architecture, each null when the entry does not state it</returns>
    private static (string? OperatingSystem, string? Architecture) ReadPlatform(JsonElement manifest)
    {
        if (manifest.TryGetProperty("platform", out var platformElement) == false)
        {
            return (null, null);
        }

        return (TryGetString(platformElement, "os"), TryGetString(platformElement, "architecture"));
    }

    /// <summary>
    /// Compare an entry value with a wanted value
    /// </summary>
    /// <param name="value">Entry value</param>
    /// <param name="wanted">Wanted value or null when none is wanted</param>
    /// <returns>True when a value is wanted and equal ignoring case</returns>
    private static bool IsSame(string? value, string? wanted)
    {
        return wanted is not null
               && string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Trim a requested platform value
    /// </summary>
    /// <param name="value">Requested value</param>
    /// <returns>Trimmed value or null when blank</returns>
    private static string? NormalizeRequestedValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Check whether a platform value marks an entry that is not a runnable image
    /// </summary>
    /// <param name="value">Platform value of an entry</param>
    /// <returns>True when the value is "unknown"</returns>
    private static bool IsUnknown(string? value)
    {
        return string.Equals(value, UnknownPlatformValue, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Read an optional string property from a JSON element
    /// </summary>
    /// <param name="element">JSON element</param>
    /// <param name="propertyName">Property name</param>
    /// <returns>String value or null</returns>
    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var propertyElement) && propertyElement.ValueKind == JsonValueKind.String
                   ? propertyElement.GetString()
                   : null;
    }

    #endregion // Methods
}