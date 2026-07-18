namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Upstream release metadata feed options
/// </summary>
public class ReleaseMetadataOptions
{
    #region Properties

    /// <summary>
    /// Base address of the .NET release metadata feed
    /// </summary>
    public string DotNetBaseUrl { get; set; } = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/";

    /// <summary>
    /// Base address of the nginx release feed
    /// </summary>
    public string NginxBaseUrl { get; set; } = "https://nginx.org/";

    #endregion // Properties
}