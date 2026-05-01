using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Interfaces;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Default image reference parser
/// </summary>
public class ImageReferenceParser : IImageReferenceParser
{
    #region Methods

    /// <inheritdoc/>
    public ImageReference Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmedValue = value.Trim();
        var digest = default(string);
        var digestSeparatorIndex = trimmedValue.IndexOf('@');

        if (digestSeparatorIndex >= 0)
        {
            digest = trimmedValue[(digestSeparatorIndex + 1)..].Trim();
            trimmedValue = trimmedValue[..digestSeparatorIndex];
        }

        var lastSlashIndex = trimmedValue.LastIndexOf('/');
        var lastColonIndex = trimmedValue.LastIndexOf(':');
        var tag = lastColonIndex > lastSlashIndex ? trimmedValue[(lastColonIndex + 1)..].Trim() : "latest";
        var namePart = lastColonIndex > lastSlashIndex ? trimmedValue[..lastColonIndex] : trimmedValue;
        var segments = namePart.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            throw new ArgumentException("The image reference does not contain a repository name", nameof(value));
        }

        string registry;
        string repository;

        if (segments.Length == 1)
        {
            registry = "docker.io";
            repository = $"library/{segments[0].ToLowerInvariant()}";
        }
        else if (LooksLikeRegistry(segments[0]))
        {
            registry = segments[0].ToLowerInvariant();
            repository = string.Join('/', segments.Skip(1)).ToLowerInvariant();
        }
        else
        {
            registry = "docker.io";
            repository = string.Join('/', segments).ToLowerInvariant();
        }

        NormalizeWrappedRegistry(ref registry, ref repository);

        return new ImageReference
               {
                   Registry = registry,
                   Repository = repository,
                   Tag = string.IsNullOrWhiteSpace(tag) ? "latest" : tag,
                   Digest = NormalizeDigest(digest),
               };
    }

    /// <inheritdoc/>
    public string Format(ImageVersion imageVersion)
    {
        ArgumentNullException.ThrowIfNull(imageVersion);
        ArgumentNullException.ThrowIfNull(imageVersion.RegistryRepository);

        return string.IsNullOrWhiteSpace(imageVersion.Digest)
                   ? $"{imageVersion.RegistryRepository.Registry}/{imageVersion.RegistryRepository.Repository}:{imageVersion.Tag}"
                   : $"{imageVersion.RegistryRepository.Registry}/{imageVersion.RegistryRepository.Repository}:{imageVersion.Tag}@{imageVersion.Digest}";
    }

    /// <summary>
    /// Normalize a digest value that may contain a full image reference
    /// </summary>
    /// <param name="digest">Digest or digest-bearing image reference</param>
    /// <returns>Normalized digest value or null</returns>
    internal static string? NormalizeDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        var trimmedDigest = digest.Trim();
        var digestSeparatorIndex = trimmedDigest.LastIndexOf('@');

        if (digestSeparatorIndex >= 0
            && digestSeparatorIndex < (trimmedDigest.Length - 1))
        {
            trimmedDigest = trimmedDigest[(digestSeparatorIndex + 1)..];
        }

        return trimmedDigest.ToLowerInvariant();
    }

    /// <summary>
    /// Determine whether a path segment looks like a registry host
    /// </summary>
    /// <param name="segment">Path segment</param>
    /// <returns>True when the segment looks like a registry</returns>
    private static bool LooksLikeRegistry(string segment)
    {
        return segment.Contains('.')
               || segment.Contains(':')
               || string.Equals(segment,
                                "localhost",
                                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalize docker.io wrapped references for well-known external registries
    /// </summary>
    /// <param name="registry">Registry host</param>
    /// <param name="repository">Repository path</param>
    private static void NormalizeWrappedRegistry(ref string registry, ref string repository)
    {
        if (string.Equals(registry, "docker.io", StringComparison.OrdinalIgnoreCase) == false)
        {
            return;
        }

        var repositorySegments = repository.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (repositorySegments.Length < 2)
        {
            return;
        }

        var wrappedRegistry = repositorySegments[0];

        if (IsKnownWrappedRegistry(wrappedRegistry) == false)
        {
            return;
        }

        registry = wrappedRegistry.ToLowerInvariant();
        repository = string.Join('/',
                                 repositorySegments.Skip(1))
                           .ToLowerInvariant();
    }

    /// <summary>
    /// Determine whether the repository prefix is a known wrapped registry host
    /// </summary>
    /// <param name="registry">Candidate registry host</param>
    /// <returns>True when the prefix should be unwrapped</returns>
    private static bool IsKnownWrappedRegistry(string registry)
    {
        return string.Equals(registry, "mcr.microsoft.com", StringComparison.OrdinalIgnoreCase)
               || string.Equals(registry, "ghcr.io", StringComparison.OrdinalIgnoreCase)
               || string.Equals(registry, "quay.io", StringComparison.OrdinalIgnoreCase)
               || (registry.Contains("harbor", StringComparison.OrdinalIgnoreCase) && LooksLikeRegistry(registry));
    }

    #endregion // Methods
}