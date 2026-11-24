using Bunit;

using DockerUpdateGuard.Components.Pages;
using DockerUpdateGuard.Tests.Data;
using DockerUpdateGuard.Tests.Helper;
using DockerUpdateGuard.UI;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DockerInstanceDetail"/>
/// </summary>
[TestClass]
public class DockerInstanceDetailTests
{
    #region Methods

    /// <summary>
    /// Verify an unknown identifier shows the not-found panel with a link back to the list instead of the skeleton
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task DockerInstanceDetailUnknownIdShowsNotFoundPanel()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var id = Guid.NewGuid();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetDockerInstanceDetailAsync(id, Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<DockerInstanceDetailViewData?>(null));

            testContext.Services.AddSingleton(viewService);

            var page = testContext.Render<DockerInstanceDetail>(parameters => parameters.Add(component => component.DockerInstanceId, id));

            await DetailPageAssert.AssertNotFoundAsync(page, "This Docker instance does not exist.", "Back to Docker Instances", "/docker-instances").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify a failed load shows the fixed reason instead of the exception text, shows no skeleton and logs the exception once
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task DockerInstanceDetailLoadFailureShowsFixedErrorAndLogs()
    {
        var exception = new InvalidOperationException("secret text");
        var logger = new TestLogger<DockerInstanceDetail>();
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var id = Guid.NewGuid();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetDockerInstanceDetailAsync(id, Arg.Any<CancellationToken>())
                       .Returns(Task.FromException<DockerInstanceDetailViewData?>(exception));

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton<ILogger<DockerInstanceDetail>>(logger);

            var page = testContext.Render<DockerInstanceDetail>(parameters => parameters.Add(component => component.DockerInstanceId, id));

            await DetailPageAssert.AssertLoadErrorAsync(page, "Loading the Docker instance failed: unexpected error", "secret text").ConfigureAwait(false);
            DetailPageAssert.AssertLoadLoggedOnce(logger, "LoadDockerInstanceDetail", exception);
        }
    }

    #endregion // Methods
}