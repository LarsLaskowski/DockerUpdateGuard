using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Normalizes the host configuration after binding and before validation: trims every Docker instance name and the Docker Hub registry host
/// </summary>
public sealed class DockerUpdateGuardOptionsPostConfigure : IPostConfigureOptions<DockerUpdateGuardOptions>
{
    #region IPostConfigureOptions

    /// <inheritdoc/>
    public void PostConfigure(string? name, DockerUpdateGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var instance in options.DockerInstances.Where(entry => string.IsNullOrEmpty(entry.Name) == false))
        {
            instance.Name = instance.Name.Trim();
        }

        if (string.IsNullOrEmpty(options.DockerHub.Registry) == false)
        {
            options.DockerHub.Registry = options.DockerHub.Registry.Trim();
        }
    }

    #endregion // IPostConfigureOptions
}