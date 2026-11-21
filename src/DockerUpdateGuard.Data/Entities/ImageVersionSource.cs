namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Kind of component that registered or last refreshed an image version through the catalog, stored as its integer number;
/// number 4 (registry metadata) is retired and never reused
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
    BaseImageResolution = 3
}