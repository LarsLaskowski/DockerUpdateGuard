namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Source of an image version
/// </summary>
public enum ImageVersionSource
{
    /// <summary>
    /// No image version source set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Manually observed image source
    /// </summary>
    ObservedImage = 1,

    /// <summary>
    /// Runtime container discovery source
    /// </summary>
    RuntimeContainer = 2,

    /// <summary>
    /// Base image resolution source
    /// </summary>
    BaseImageResolution = 3,

    /// <summary>
    /// Registry metadata source
    /// </summary>
    RegistryMetadata = 4
}