using System.Text.Json;

namespace DockerUpdateGuard.Images.Helper;

/// <summary>
/// Reads the labels of an image configuration
/// </summary>
internal static class ImageConfigurationLabelReader
{
    #region Methods

    /// <summary>
    /// Read the string labels of a labels object, matching the names case-insensitively
    /// </summary>
    /// <param name="labelsElement">Labels object of the image configuration</param>
    /// <returns>The labels, the last of colliding names winning</returns>
    public static Dictionary<string, string> ReadLabels(JsonElement labelsElement)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Methods
}