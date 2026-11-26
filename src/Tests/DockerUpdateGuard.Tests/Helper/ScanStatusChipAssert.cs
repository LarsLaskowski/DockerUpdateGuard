using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Assertions shared by the tests of the scan status chips of the pages
/// </summary>
internal static class ScanStatusChipAssert
{
    #region Static methods

    /// <summary>
    /// Wait until the finder returns a chip showing the status, then assert its text and color class
    /// </summary>
    /// <typeparam name="TComponent">Page type</typeparam>
    /// <param name="page">Rendered page</param>
    /// <param name="findChips">Finder of the candidate scan status chips</param>
    /// <param name="status">Status shown by the chip</param>
    /// <param name="expectedColorClass">Expected chip color class</param>
    /// <returns>Task</returns>
    public static async Task AssertChipAsync<TComponent>(IRenderedComponent<TComponent> page,
                                                         Func<IRenderedComponent<TComponent>, IEnumerable<IElement>> findChips,
                                                         string status,
                                                         string expectedColorClass)
        where TComponent : IComponent
    {
        await page.WaitForAssertionAsync(() => Assert.IsNotEmpty(findChips(page).ToList(), "The page must render the scan status chip"),
                                         TimeSpan.FromSeconds(DetailPageAssert.WaitSeconds)).ConfigureAwait(false);

        var chip = findChips(page).First();

        Assert.AreEqual(status, chip.TextContent.Trim(), "The chip text must be the status unchanged");
        Assert.Contains(expectedColorClass, chip.ClassName ?? string.Empty, $"Status {status} must be shown in the chip color '{expectedColorClass}'");
    }

    /// <summary>
    /// Find the chips of a table cell of a section whose header starts with the given heading
    /// </summary>
    /// <param name="root">Rendered markup to search</param>
    /// <param name="heading">Section heading</param>
    /// <param name="cellLabel">Data label of the table cell</param>
    /// <returns>Chips of the cell</returns>
    public static IEnumerable<IElement> FindSectionCellChips(IRenderedComponent<IComponent> root, string heading, string cellLabel)
    {
        return root.FindAll(".section-paper")
                   .Where(section => section.QuerySelector(".section-header")?.TextContent.Trim().StartsWith(heading, StringComparison.Ordinal) == true)
                   .SelectMany(section => section.QuerySelectorAll($"td[data-label='{cellLabel}'] .mud-chip"));
    }

    /// <summary>
    /// Find the chips of a summary card with the given label
    /// </summary>
    /// <param name="root">Rendered markup to search</param>
    /// <param name="label">Summary card label</param>
    /// <returns>Chips of the card</returns>
    public static IEnumerable<IElement> FindSummaryCardChips(IRenderedComponent<IComponent> root, string label)
    {
        return root.FindAll(".summary-card")
                   .Where(card => card.QuerySelector(".summary-label")?.TextContent.Trim() == label)
                   .SelectMany(card => card.QuerySelectorAll(".mud-chip"));
    }

    #endregion // Static methods
}