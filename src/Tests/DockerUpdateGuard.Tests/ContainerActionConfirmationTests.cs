using Bunit;

using DockerUpdateGuard.Components.Common;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Tests.Helper;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ContainerActionConfirmation"/>
/// </summary>
[TestClass]
public class ContainerActionConfirmationTests
{
    #region Methods

    /// <summary>
    /// Verify the dialog names the action, container, instance and environment, adds the consequence of stop and restart, and styles the confirmation of stop and restart destructive
    /// </summary>
    /// <param name="actionType">Confirmed action type</param>
    /// <param name="displayName">Display name of the action</param>
    /// <param name="consequence">Expected consequence line; empty when the action has none</param>
    /// <param name="buttonColor">Expected color of the confirmation button</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow(ContainerActionType.StartContainer, "Start", "", "primary")]
    [DataRow(ContainerActionType.StopContainer, "Stop", "The container stops and stays stopped until it is started again.", "error")]
    [DataRow(ContainerActionType.RestartContainer, "Restart", "The container is unavailable while it restarts.", "error")]
    public async Task ContainerActionConfirmationForStopShowsTextConsequenceAndDestructiveButtonAsync(ContainerActionType actionType,
                                                                                                      string displayName,
                                                                                                      string consequence,
                                                                                                      string buttonColor)
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var provider = await ShowDialogAsync(testContext, actionType, false).ConfigureAwait(false);
            var text = provider.Find(".mud-dialog").TextContent;
            var confirmButton = provider.FindAll(".mud-dialog button")
                                        .Single(button => button.TextContent.Trim() == displayName);

            Assert.Contains($"{displayName} container?", text, "The dialog title must name the action");
            Assert.Contains($"{displayName} container 'my-container' on Docker instance 'Production' through Portainer environment 7.",
                            text,
                            "The dialog must name the action, container, instance and environment");
            Assert.Contains(consequence, text, $"The dialog of {displayName} must state its consequence");
            Assert.ContainsSingle(button => button.TextContent.Trim() == "Cancel",
                                  provider.FindAll(".mud-dialog button"),
                                  "The dialog must offer Cancel");
            Assert.Contains($"-{buttonColor}", confirmButton.ClassName ?? string.Empty, $"The {displayName} button must use the {buttonColor} color");
            Assert.DoesNotContain("own container", text, "The own-container warning must only appear for the application's own container");
        }
    }

    /// <summary>
    /// Verify the dialog warns when the target is the application's own container
    /// </summary>
    /// <param name="actionType">Confirmed action type</param>
    /// <param name="verb">Expected verb of the warning</param>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow(ContainerActionType.StopContainer, "stops")]
    [DataRow(ContainerActionType.RestartContainer, "restarts")]
    public async Task ContainerActionConfirmationForOwnContainerShowsWarningAsync(ContainerActionType actionType, string verb)
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var provider = await ShowDialogAsync(testContext, actionType, true).ConfigureAwait(false);
            var warnings = provider.FindAll(".mud-dialog .mud-alert");

            Assert.ContainsSingle(alert => alert.TextContent.Contains($"This is DockerUpdateGuard's own container: the application {verb} and this page loses its connection.", StringComparison.Ordinal)
                                           && (alert.ClassName ?? string.Empty).Contains("warning", StringComparison.Ordinal),
                                  warnings,
                                  "The dialog must show the own-container warning as a warning alert");
        }
    }

    /// <summary>
    /// Render the dialog provider and open the confirmation for the test container
    /// </summary>
    /// <param name="testContext">Test context</param>
    /// <param name="actionType">Confirmed action type</param>
    /// <param name="isOwnContainer">Whether the target is the application's own container</param>
    /// <returns>Rendered dialog provider</returns>
    private static async Task<IRenderedComponent<MudDialogProvider>> ShowDialogAsync(Bunit.BunitContext testContext, ContainerActionType actionType, bool isOwnContainer)
    {
        var provider = testContext.Render<MudDialogProvider>();
        var dialogService = testContext.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ContainerActionConfirmation>
                         {
                             { dialog => dialog.ActionType, actionType },
                             { dialog => dialog.ContainerName, "my-container" },
                             { dialog => dialog.DockerInstanceName, "Production" },
                             { dialog => dialog.EndpointId, "7" },
                             { dialog => dialog.IsOwnContainer, isOwnContainer }
                         };

        await provider.InvokeAsync(() => dialogService.ShowAsync<ContainerActionConfirmation>(null, parameters))
                      .ConfigureAwait(false);

        return provider;
    }

    #endregion // Methods
}