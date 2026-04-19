namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Helper for resolving the configured connection string
/// </summary>
public static class DockerUpdateGuardConnectionStringResolver
{
    #region Methods

    /// <summary>
    /// Resolve the configured connection string value
    /// </summary>
    /// <param name="options">Application options</param>
    /// <param name="configuration">Configuration root</param>
    /// <returns>Connection string value</returns>
    public static string ResolveConnectionString(DockerUpdateGuardOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(options.ConnectionString) == false)
        {
            return options.ConnectionString.Trim();
        }

        return configuration.GetConnectionString(options.ConnectionStringName) ?? string.Empty;
    }

    #endregion // Methods
}