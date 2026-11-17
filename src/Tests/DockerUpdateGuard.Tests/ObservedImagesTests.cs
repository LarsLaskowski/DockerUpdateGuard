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

    #endregion // Methods
}