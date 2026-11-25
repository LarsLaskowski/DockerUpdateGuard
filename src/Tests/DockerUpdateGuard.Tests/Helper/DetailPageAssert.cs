using Bunit;

using DockerUpdateGuard.Tests.Data;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Assertions shared by the tests of the pages' loading, not-found and failed-load states
/// </summary>
internal static class DetailPageAssert
{
    #region Constants

    /// <summary>
    /// Seconds a test waits for a page to reach an expected state
    /// </summary>
    public const int WaitSeconds = 2;

    /// <summary>
    /// Event ID of the user-action failure log entry
    /// </summary>
    private const int UserActionFailedEventId = 4000;

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Wait until the page shows the not-found panel and assert its message and back link; no skeleton and no alert
    /// </summary>
    /// <typeparam name="TComponent">Page type</typeparam>
    /// <param name="page">Rendered page</param>
    /// <param name="message">Expected not-found message</param>
    /// <param name="linkText">Expected back link text</param>
    /// <param name="linkHref">Expected back link target</param>
    /// <returns>Task</returns>
    public static async Task AssertNotFoundAsync<TComponent>(IRenderedComponent<TComponent> page, string message, string linkText, string linkHref)
        where TComponent : IComponent
    {
        await page.WaitForAssertionAsync(() => Assert.HasCount(1, page.FindAll(".empty-state-panel"), "The page must show the not-found panel"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

        var panel = page.Find(".empty-state-panel");
        var link = panel.QuerySelector("a");

        Assert.Contains(message, panel.TextContent, "The panel must carry the not-found message");
        Assert.IsNotNull(link, "The panel must carry the back link");
        Assert.AreEqual(linkText, link.TextContent.Trim(), "The back link must carry its text");
        Assert.AreEqual(linkHref, link.GetAttribute("href"), "The back link must point to the list page");
        Assert.IsEmpty(page.FindAll(".mud-skeleton"), "The not-found state must not show the skeleton");
        Assert.IsEmpty(page.FindAll(".mud-alert"), "The not-found state must not show an error alert");
    }

    /// <summary>
    /// Wait until the page shows exactly the expected error alert; no skeleton, no not-found panel and none of the forbidden texts
    /// </summary>
    /// <typeparam name="TComponent">Page type</typeparam>
    /// <param name="page">Rendered page</param>
    /// <param name="expectedText">Expected alert text</param>
    /// <param name="forbiddenTexts">Texts that must not appear in the markup</param>
    /// <returns>Task</returns>
    public static async Task AssertLoadErrorAsync<TComponent>(IRenderedComponent<TComponent> page, string expectedText, params string[] forbiddenTexts)
        where TComponent : IComponent
    {
        await page.WaitForAssertionAsync(() => Assert.HasCount(1, page.FindAll(".mud-alert"), "The page must show one error alert"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

        UserActionFailureAssert.AssertShown(page.Markup, expectedText, forbiddenTexts);
        Assert.IsEmpty(page.FindAll(".mud-skeleton"), "The failed load must not show the skeleton");
        Assert.IsEmpty(page.FindAll(".empty-state-panel"), "The failed load must not show the not-found panel");
    }

    /// <summary>
    /// Assert the page shows the skeleton and neither the not-found panel nor an error alert
    /// </summary>
    /// <typeparam name="TComponent">Page type</typeparam>
    /// <param name="page">Rendered page</param>
    public static void AssertLoading<TComponent>(IRenderedComponent<TComponent> page)
        where TComponent : IComponent
    {
        Assert.IsNotEmpty(page.FindAll(".mud-skeleton"), "A pending load must show the skeleton");
        Assert.IsEmpty(page.FindAll(".empty-state-panel"), "A pending load must not show the not-found panel");
        Assert.IsEmpty(page.FindAll(".mud-alert"), "A pending load must not show an error alert");
    }

    /// <summary>
    /// Wait until the page shows the loaded detail and assert it shows neither skeleton, not-found message nor error alert
    /// </summary>
    /// <typeparam name="TComponent">Page type</typeparam>
    /// <param name="page">Rendered page</param>
    /// <param name="detailText">Text of the loaded detail</param>
    /// <param name="notFoundMessage">Not-found message of the page, which a loaded detail must not show</param>
    /// <returns>Task</returns>
    public static async Task AssertLoadedAsync<TComponent>(IRenderedComponent<TComponent> page, string detailText, string notFoundMessage)
        where TComponent : IComponent
    {
        await page.WaitForAssertionAsync(() => Assert.Contains(detailText, page.Markup, "The page must render the loaded detail"), TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);

        Assert.IsEmpty(page.FindAll(".mud-skeleton"), "A loaded detail must not show the skeleton");
        Assert.DoesNotContain(notFoundMessage, page.Markup, "A loaded detail must not show the not-found message");
        Assert.IsEmpty(page.FindAll(".mud-alert"), "A loaded detail must not show an error alert");
    }

    /// <summary>
    /// Let the answers that are already completed reach the page's dispatcher
    /// </summary>
    /// <typeparam name="TComponent">Page type</typeparam>
    /// <param name="page">Rendered page</param>
    /// <returns>Task</returns>
    public static async Task SettleAsync<TComponent>(IRenderedComponent<TComponent> page)
        where TComponent : IComponent
    {
        for (var round = 0; round < 3; round++)
        {
            await Task.Yield();
            await page.InvokeAsync(() =>
                                   {
                                   }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Wait until the logger holds a user-action failure entry of the action
    /// </summary>
    /// <typeparam name="TComponent">Page type</typeparam>
    /// <typeparam name="TCategoryName">Logger category</typeparam>
    /// <param name="page">Rendered page, used to wait</param>
    /// <param name="logger">Test logger</param>
    /// <param name="action">Expected action name</param>
    /// <returns>Task</returns>
    public static async Task WaitForLoadLoggedAsync<TComponent, TCategoryName>(IRenderedComponent<TComponent> page, TestLogger<TCategoryName> logger, string action)
        where TComponent : IComponent
    {
        await page.WaitForAssertionAsync(() => Assert.Contains(entry => entry.EventId.Id == UserActionFailedEventId && entry.Message == $"User action {action} failed",
                                                               logger.Entries,
                                                               $"The late failed load must be logged as user action {action}"),
                                         TimeSpan.FromSeconds(WaitSeconds)).ConfigureAwait(false);
    }

    /// <summary>
    /// Assert the logger holds exactly one user-action failure entry of the action, carrying the exception; other entries are ignored
    /// </summary>
    /// <typeparam name="TCategoryName">Logger category</typeparam>
    /// <param name="logger">Test logger</param>
    /// <param name="action">Expected action name</param>
    /// <param name="exception">Expected exception instance</param>
    public static void AssertLoadLoggedOnce<TCategoryName>(TestLogger<TCategoryName> logger, string action, Exception exception)
    {
        var entries = logger.Entries
                            .Where(entry => entry.EventId.Id == UserActionFailedEventId && entry.Message == $"User action {action} failed")
                            .ToList();

        Assert.HasCount(1, entries, $"The failed load must be logged exactly once as user action {action}");
        Assert.AreEqual(LogLevel.Error, entries[0].LogLevel, "The failure must be logged as an error");
        Assert.AreSame(exception, entries[0].Exception, "The log entry must carry the caught exception instance");
    }

    #endregion // Methods
}