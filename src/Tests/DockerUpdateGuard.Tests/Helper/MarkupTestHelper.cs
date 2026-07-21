namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Helper methods for asserting against rendered component markup
/// </summary>
internal static class MarkupTestHelper
{
    #region Constants

    /// <summary>
    /// Marks the start of a rendered MudBlazor skeleton element
    /// </summary>
    private const string SkeletonMarker = "class=\"mud-skeleton";

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Count non-overlapping occurrences of a token within a text
    /// </summary>
    /// <param name="text">Text to search</param>
    /// <param name="token">Token to count</param>
    /// <returns>Number of occurrences</returns>
    public static int CountOccurrences(string text, string token)
    {
        return text.Split(token).Length - 1;
    }

    /// <summary>
    /// Count the rendered skeleton placeholder elements within markup
    /// </summary>
    /// <param name="markup">Rendered markup</param>
    /// <returns>Number of skeleton placeholder elements</returns>
    public static int CountSkeletons(string markup)
    {
        return CountOccurrences(markup, SkeletonMarker);
    }

    #endregion // Methods
}