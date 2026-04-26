namespace DockerUpdateGuard.Images;

/// <summary>
/// Derived base runtime kind
/// </summary>
public enum DerivedBaseRuntimeKind
{
    /// <summary>
    /// No runtime kind set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// .NET runtime
    /// </summary>
    DotNet = 1,

    /// <summary>
    /// NGINX runtime
    /// </summary>
    Nginx = 2,
}