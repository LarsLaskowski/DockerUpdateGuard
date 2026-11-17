using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Normalizes the host configuration after binding and before validation: trims every Docker instance name
/// </summary>
public sealed class DockerUpdateGuardOptionsPostConfigure : IPostConfigureOptions<DockerUpdateGuardOptions>
{
    #region IPostConfigureOptions

    /// <inheritdoc/>
    public void PostConfigure(string? name, DockerUpdateGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var instance in options.DockerInstances)
        {
            if (string.IsNullOrEmpty(instance.Name) == false)
            {
                instance.Name = instance.Name.Trim();
            }
        }
    }

    #endregion // IPostConfigureOptions
}