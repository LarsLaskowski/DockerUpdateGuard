using DockerUpdateGuard.Tests.Data;

using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Assertions shared by the tests of failed user actions on the pages
/// </summary>
internal static class UserActionFailureAssert
{
    #region Constants

    /// <summary>
    /// Event ID of the user-action failure log entry
    /// </summary>
    private const int UserActionFailedEventId = 4000;

    #endregion // Constants

    #region Methods

    /// <summary>
    /// Assert the markup shows exactly the expected failure text as one text node and none of the forbidden texts
    /// </summary>
    /// <param name="markup">Rendered markup</param>
    /// <param name="expectedText">Expected failure text</param>
    /// <param name="forbiddenTexts">Texts that must not appear in the markup</param>
    public static void AssertShown(string markup, string expectedText, params string[] forbiddenTexts)
    {
        Assert.Contains($">{expectedText}<", markup, $"The page must show exactly '{expectedText}' as the failure text");

        foreach (var forbiddenText in forbiddenTexts)
        {
            Assert.DoesNotContain(forbiddenText, markup, $"The page must not show '{forbiddenText}'");
        }
    }

    /// <summary>
    /// Assert the logger holds exactly one user-action failure entry for the action and the exception
    /// </summary>
    /// <typeparam name="TCategoryName">Logger category</typeparam>
    /// <param name="logger">Test logger</param>
    /// <param name="action">Expected action name</param>
    /// <param name="exception">Expected exception instance</param>
    public static void AssertLogged<TCategoryName>(TestLogger<TCategoryName> logger, string action, Exception exception)
    {
        Assert.HasCount(1, logger.Entries, "A failed user action must be logged exactly once");

        var entry = logger.Entries[0];

        Assert.AreEqual(UserActionFailedEventId, entry.EventId.Id, "The failure must be logged with event ID 4000");
        Assert.AreEqual(LogLevel.Error, entry.LogLevel, "The failure must be logged as an error");
        Assert.AreSame(exception, entry.Exception, "The log entry must carry the caught exception instance");
        Assert.AreEqual($"User action {action} failed", entry.Message, "The log entry must name the failed action");
    }

    #endregion // Methods
}