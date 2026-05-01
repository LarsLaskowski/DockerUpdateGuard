namespace DockerUpdateGuard.Images.Data;

/// <summary>
/// Normalized NGINX channel release data
/// </summary>
public class NginxChannelReleaseData
{
    #region Properties

    /// <summary>
    /// Channel version
    /// </summary>
    public string ChannelVersion { get; set; } = string.Empty;

    /// <summary>
    /// Latest channel version
    /// </summary>
    public Version LatestVersion { get; set; } = new(0, 0, 0);

    #endregion // Properties
}