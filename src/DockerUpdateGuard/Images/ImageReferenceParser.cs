using DockerUpdateGuard.Data.Entities;

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

        return new ImageReference
               {
                   Registry = registry,
                   Repository = repository,
                   Tag = string.IsNullOrWhiteSpace(tag) ? "latest" : tag,
                   Digest = string.IsNullOrWhiteSpace(digest) ? null : digest.ToLowerInvariant(),
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

    #endregion // Methods
}