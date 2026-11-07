using System.Text.Json;

using DockerUpdateGuard.Infrastructure;

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
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (labelsElement.ValueKind != JsonValueKind.Object)
        {
            return labels;
        }

        foreach (var property in labelsElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            labels.Remove(property.Name);
            labels.Add(property.Name, ResponseBodyExcerpt.ReplaceControlCharacters(property.Value.GetString()) ?? string.Empty);
        }

        return labels;
    }

    #endregion // Methods
}