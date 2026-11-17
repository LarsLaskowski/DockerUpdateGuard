using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Normalizes the host configuration after binding and before validation
/// </summary>
public sealed class DockerUpdateGuardOptionsPostConfigure : IPostConfigureOptions<DockerUpdateGuardOptions>
{
    #region IPostConfigureOptions

    /// <inheritdoc/>
    public void PostConfigure(string? name, DockerUpdateGuardOptions options)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // IPostConfigureOptions
}