using DockerUpdateGuard.Configuration;

using Microsoft.Extensions.Options;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Builds options monitors through the real options pipeline with the post-configure step of the host registered
/// </summary>
internal static class PostConfiguredOptions
{
    #region Static methods

    /// <summary>
    /// Create an options monitor whose value is built from the given options by the options factory, which runs the post-configure step
    /// </summary>
    /// <param name="source">Options as they are configured, before the post-configure step</param>
    /// <returns>Options monitor</returns>
    public static IOptionsMonitor<DockerUpdateGuardOptions> CreateMonitor(DockerUpdateGuardOptions source)
    {
        var factory = new OptionsFactory<DockerUpdateGuardOptions>([new ConfigureOptions<DockerUpdateGuardOptions>(options => Copy(source, options))],
                                                                   [new DockerUpdateGuardOptionsPostConfigure()]);

        return new OptionsMonitor<DockerUpdateGuardOptions>(factory,
                                                            [],
                                                            new OptionsCache<DockerUpdateGuardOptions>());
    }

    /// <summary>
    /// Copy the instances and the scanning settings of the source into the options the factory created
    /// </summary>
    /// <param name="source">Configured options</param>
    /// <param name="target">Options created by the factory</param>
    private static void Copy(DockerUpdateGuardOptions source, DockerUpdateGuardOptions target)
    {
        target.DockerInstances = source.DockerInstances;
        target.Scanning = source.Scanning;
    }

    #endregion // Static methods
}