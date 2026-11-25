using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ObservedImages"/>
/// </summary>
[TestClass]
public class ObservedImagesTests
{
    #region Constants

    /// <summary>
    /// Name of the list item shown before the submit, unique in the page markup
    /// </summary>
    private const string ExistingImageName = "existing-observed-image";

    #endregion // Constants

    #region Static methods

    /// <summary>
    /// Submit the form and assert the message is shown as validation text and the service was not called
    /// </summary>
    /// <param name="name">Name to type</param>
    /// <param name="imageReference">Image reference to type</param>
    /// <param name="expectedMessage">Expected validation message</param>
    /// <returns>Task</returns>
    private static async Task AssertReportedWithoutRegistrationAsync(string name, string imageReference, string expectedMessage)
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var registrationService = Substitute.For<IImageRegistrationService>();

            viewService.GetManualObservedImagesAsync(Arg.Any<CancellationToken>())
                       .Returns([]);

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(registrationService);
            testContext.Services.AddSingleton(Substitute.For<IImageScanOrchestrator>());
            testContext.Services.AddSingleton(new DashboardRefreshState());
            testContext.Services.AddSingleton<ILogger<ObservedImages>>(new TestLogger<ObservedImages>());

            var component = testContext.Render<ObservedImages>();
            var inputs = component.FindAll("input");

            await inputs[0].ChangeAsync(new ChangeEventArgs
                                        {
                                            Value = name
                                        }).ConfigureAwait(false);
            await inputs[1].ChangeAsync(new ChangeEventArgs
                                        {
                                            Value = imageReference
                                        }).ConfigureAwait(false);
            await component.Find("form").SubmitAsync().ConfigureAwait(false);

            Assert.Contains($">{expectedMessage}<", component.Markup, "The form must show the length message as validation text");
            Assert.DoesNotContain("Registration failed", component.Markup, "A length message is not a failed registration");
            await registrationService.DidNotReceive().RegisterAsync(Arg.Any<ObservedImageRegistrationRequest>(), Arg.Any<CancellationToken>()).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Render the page with a registration and a following list reload that both fail, and submit the form
    /// </summary>
    /// <param name="testContext">Test context</param>
    /// <param name="logger">Logger the page writes to</param>
    /// <param name="registrationException">Exception of the registration</param>
    /// <param name="reloadException">Exception of the list reload after the failed registration</param>
    /// <returns>Rendered page after the submit</returns>
    private static async Task<IRenderedComponent<ObservedImages>> SubmitWithFailingReloadAsync(BunitContext testContext,
                                                                                               TestLogger<ObservedImages> logger,
                                                                                               Exception registrationException,
                                                                                               Exception reloadException)
    {
        var viewService = Substitute.For<IApplicationViewService>();
        var registrationService = Substitute.For<IImageRegistrationService>();
        IReadOnlyList<ObservedImageListItemData> initialList = [
                                                                   new()
                                                                   {
                                                                       Id = Guid.NewGuid(),
                                                                       Name = ExistingImageName,
                                                                       ImageReference = "docker.io/existing/image:2.0.0",
                                                                   }
                                                               ];

        viewService.GetManualObservedImagesAsync(Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult(initialList), Task.FromException<IReadOnlyList<ObservedImageListItemData>>(reloadException));
        registrationService.RegisterAsync(Arg.Any<ObservedImageRegistrationRequest>(), Arg.Any<CancellationToken>())
                           .Returns(Task.FromException<ObservedImage>(registrationException));

        testContext.Services.AddSingleton(viewService);
        testContext.Services.AddSingleton(registrationService);
        testContext.Services.AddSingleton(Substitute.For<IImageScanOrchestrator>());
        testContext.Services.AddSingleton(new DashboardRefreshState());
        testContext.Services.AddSingleton<ILogger<ObservedImages>>(logger);

        var component = testContext.Render<ObservedImages>();
        var inputs = component.FindAll("input");

        await inputs[0].ChangeAsync(new ChangeEventArgs
                                    {
                                        Value = "Company API"
                                    }).ConfigureAwait(false);
        await inputs[1].ChangeAsync(new ChangeEventArgs
                                    {
                                        Value = "docker.io/company/api:1.0.0"
                                    }).ConfigureAwait(false);
        await component.Find("form").SubmitAsync().ConfigureAwait(false);

        return component;
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify a failed registration shows a fixed reason instead of the exception message and logs the exception
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ObservedImagesFailedRegistrationShowsFixedReasonAndLogsAsync()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var registrationService = Substitute.For<IImageRegistrationService>();
            var scanOrchestrator = Substitute.For<IImageScanOrchestrator>();
            var logger = new TestLogger<ObservedImages>();
            var exception = new InvalidOperationException("connection to db.internal.test:5432 refused");

            viewService.GetManualObservedImagesAsync(Arg.Any<CancellationToken>())
                       .Returns([]);
            registrationService.RegisterAsync(Arg.Any<ObservedImageRegistrationRequest>(), Arg.Any<CancellationToken>())
                               .Returns(Task.FromException<ObservedImage>(exception));

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(registrationService);
            testContext.Services.AddSingleton(scanOrchestrator);
            testContext.Services.AddSingleton(new DashboardRefreshState());
            testContext.Services.AddSingleton<ILogger<ObservedImages>>(logger);

            var component = testContext.Render<ObservedImages>();
            var inputs = component.FindAll("input");

            var nameChange = inputs[0].ChangeAsync(new ChangeEventArgs
                                                   {
                                                       Value = "Company API"
                                                   });

            await nameChange.ConfigureAwait(false);

            var referenceChange = inputs[1].ChangeAsync(new ChangeEventArgs
                                                        {
                                                            Value = "docker.io/company/api:1.0.0"
                                                        });

            await referenceChange.ConfigureAwait(false);

            await component.Find("form").SubmitAsync().ConfigureAwait(false);

            UserActionFailureAssert.AssertShown(component.Markup, "Registration failed: unexpected error", "db.internal.test", "refused", "/my-images/", "Open own image");
            UserActionFailureAssert.AssertLogged(logger, "RegisterObservedImage", exception);
            await scanOrchestrator.DidNotReceive().ScanAsync(Arg.Any<Guid>(), Arg.Any<ScanTriggerSource>(), Arg.Any<CancellationToken>()).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify a failed list reload after a failed registration keeps the registration message, the list and the button usable
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    [TestCategory("AC3")]
    public async Task ObservedImagesFailedReloadAfterFailedRegistrationKeepsPageUsableAsync()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var registrationException = new InvalidOperationException("connection to db.internal.test:5432 refused");
            var reloadException = new InvalidOperationException("connection to reload.db.internal.test:5432 refused");

            var component = await SubmitWithFailingReloadAsync(testContext, new TestLogger<ObservedImages>(), registrationException, reloadException).ConfigureAwait(false);

            UserActionFailureAssert.AssertShown(component.Markup,
                                                "Registration failed: unexpected error",
                                                "db.internal.test",
                                                "reload.db.internal.test",
                                                "refused");
            Assert.IsNull(component.Find("button[type=submit]").GetAttribute("disabled"), "The submit button must be enabled again");
            Assert.Contains(ExistingImageName, component.Markup, "The list must still show the rows it showed before the submit");
        }
    }

    /// <summary>
    /// Verify a failed list reload after a failed registration is logged as a second entry of the registration action
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task ObservedImagesFailedReloadAfterFailedRegistrationIsLoggedAsync()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var logger = new TestLogger<ObservedImages>();
            var registrationException = new InvalidOperationException("connection to db.internal.test:5432 refused");
            var reloadException = new InvalidOperationException("connection to reload.db.internal.test:5432 refused");

            await SubmitWithFailingReloadAsync(testContext, logger, registrationException, reloadException).ConfigureAwait(false);

            Assert.HasCount(2, logger.Entries, "The registration and the reload failure must both be logged");

            foreach (var entry in logger.Entries)
            {
                Assert.AreEqual(4000, entry.EventId.Id, "The failure must be logged with event ID 4000");
                Assert.AreEqual(LogLevel.Error, entry.LogLevel, "The failure must be logged as an error");
                Assert.AreEqual("User action RegisterObservedImage failed", entry.Message, "The log entry must name the failed action");
            }

            Assert.AreSame(registrationException, logger.Entries[0].Exception, "The first entry must carry the registration exception");
            Assert.AreSame(reloadException, logger.Entries[1].Exception, "The second entry must carry the reload exception");
        }
    }

    /// <summary>
    /// Verify a rejected registration of a tracked own image shows the reason with a link to the own image and starts no scan
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC9")]
    public async Task ObservedImagesRegistrationOfTrackedOwnImageLinksToOwnImageAsync()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();
            var registrationService = Substitute.For<IImageRegistrationService>();
            var scanOrchestrator = Substitute.For<IImageScanOrchestrator>();
            var logger = new TestLogger<ObservedImages>();
            var ownImageId = Guid.NewGuid();
            var exception = new OwnImageAlreadyTrackedException("This image is already tracked as own image 'myuser/app'.", ownImageId);

            viewService.GetManualObservedImagesAsync(Arg.Any<CancellationToken>())
                       .Returns([]);
            registrationService.RegisterAsync(Arg.Any<ObservedImageRegistrationRequest>(), Arg.Any<CancellationToken>())
                               .Returns(Task.FromException<ObservedImage>(exception));

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(registrationService);
            testContext.Services.AddSingleton(scanOrchestrator);
            testContext.Services.AddSingleton(new DashboardRefreshState());
            testContext.Services.AddSingleton<ILogger<ObservedImages>>(logger);

            var component = testContext.Render<ObservedImages>();
            var inputs = component.FindAll("input");

            await inputs[0].ChangeAsync(new ChangeEventArgs
                                        {
                                            Value = "Mine"
                                        }).ConfigureAwait(false);
            await inputs[1].ChangeAsync(new ChangeEventArgs
                                        {
                                            Value = "docker.io/myuser/app:1.0.0"
                                        }).ConfigureAwait(false);
            await component.Find("form").SubmitAsync().ConfigureAwait(false);

            Assert.Contains("Registration failed: This image is already tracked as own image 'myuser/app'.",
                            component.Markup,
                            "The page must show the own-image rejection reason");

            var link = component.Find($"a[href=\"/my-images/{ownImageId}\"]");

            Assert.AreEqual("Open own image", link.TextContent.Trim(), "The link must be labelled Open own image");
            UserActionFailureAssert.AssertLogged(logger, "RegisterObservedImage", exception);
            await scanOrchestrator.DidNotReceive().ScanAsync(Arg.Any<Guid>(), Arg.Any<ScanTriggerSource>(), Arg.Any<CancellationToken>()).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify a name over 200 characters is reported in the form and nothing is registered
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public Task ObservedImagesTooLongNameIsReportedWithoutRegistrationAsync()
    {
        return AssertReportedWithoutRegistrationAsync(new string('a', 201),
                                                      "docker.io/company/api:1.0.0",
                                                      "The name must not be longer than 200 characters");
    }

    /// <summary>
    /// Verify an over-long tag is reported in the form and nothing is registered
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public Task ObservedImagesTooLongTagIsReportedWithoutRegistrationAsync()
    {
        return AssertReportedWithoutRegistrationAsync("Company API",
                                                      $"docker.io/company/api:{new string('t', 201)}",
                                                      "The tag must not be longer than 200 characters");
    }

    /// <summary>
    /// Verify a failed first load shows the fixed error alert in place of the skeleton and logs the failure once
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    [TestCategory("AC2")]
    public async Task ObservedImagesLoadFailureShowsFixedErrorAndLogs()
    {
        var exception = new InvalidOperationException("secret text");
        var logger = new TestLogger<ObservedImages>();
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetManualObservedImagesAsync(Arg.Any<CancellationToken>())
                       .Returns(Task.FromException<IReadOnlyList<ObservedImageListItemData>>(exception));

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton(Substitute.For<IImageRegistrationService>());
            testContext.Services.AddSingleton(Substitute.For<IImageScanOrchestrator>());
            testContext.Services.AddSingleton(new DashboardRefreshState());
            testContext.Services.AddSingleton<ILogger<ObservedImages>>(logger);

            var page = testContext.Render<ObservedImages>();

            await DetailPageAssert.AssertLoadErrorAsync(page, "Loading the observed images failed: unexpected error", "secret text").ConfigureAwait(false);
            DetailPageAssert.AssertLoadLoggedOnce(logger, "LoadObservedImages", exception);
        }
    }

    #endregion // Methods
}