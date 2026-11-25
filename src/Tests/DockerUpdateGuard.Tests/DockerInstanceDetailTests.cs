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

    /// <summary>
    /// Verify a pending load shows the skeleton only, and the loaded detail replaces it
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task DockerInstanceDetailPendingLoadShowsSkeletonOnly()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var id = Guid.NewGuid();
            var pending = new TaskCompletionSource<DockerInstanceDetailViewData?>();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetDockerInstanceDetailAsync(id, Arg.Any<CancellationToken>())
                       .Returns(pending.Task);

            testContext.Services.AddSingleton(viewService);

            var page = testContext.Render<DockerInstanceDetail>(parameters => parameters.Add(component => component.DockerInstanceId, id));

            DetailPageAssert.AssertLoading(page);

            pending.SetResult(new DockerInstanceDetailViewData
                              {
                                  Id = id,
                                  Name = "Prod Host"
                              });

            await DetailPageAssert.AssertLoadedAsync(page, "Prod Host", "This Docker instance does not exist.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify a route change while the new load is pending shows the skeleton only and none of the previous record
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC1")]
    public async Task DockerInstanceDetailRouteChangeWhileLoadPendingShowsSkeleton()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            var pending = new TaskCompletionSource<DockerInstanceDetailViewData?>();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetDockerInstanceDetailAsync(firstId, Arg.Any<CancellationToken>())
                       .Returns(CreateDetail(firstId, "Prod Host A"));
            viewService.GetDockerInstanceDetailAsync(secondId, Arg.Any<CancellationToken>())
                       .Returns(pending.Task);

            testContext.Services.AddSingleton(viewService);

            var page = testContext.Render<DockerInstanceDetail>(parameters => parameters.Add(component => component.DockerInstanceId, firstId));

            await DetailPageAssert.AssertLoadedAsync(page, "Prod Host A", "This Docker instance does not exist.").ConfigureAwait(false);

            page.Render(parameters => parameters.Add(component => component.DockerInstanceId, secondId));

            DetailPageAssert.AssertLoading(page);
            Assert.DoesNotContain("Prod Host A", page.Markup, "The previous record must not be shown while the new load is pending");
        }
    }

    /// <summary>
    /// Verify a record of the earlier route that arrives after the route change is dropped
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC2")]
    public async Task DockerInstanceDetailLateDetailOfPreviousRouteIsDropped()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            var pendingFirst = new TaskCompletionSource<DockerInstanceDetailViewData?>();
            var pendingSecond = new TaskCompletionSource<DockerInstanceDetailViewData?>();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetDockerInstanceDetailAsync(firstId, Arg.Any<CancellationToken>())
                       .Returns(pendingFirst.Task);
            viewService.GetDockerInstanceDetailAsync(secondId, Arg.Any<CancellationToken>())
                       .Returns(pendingSecond.Task);

            testContext.Services.AddSingleton(viewService);

            var page = testContext.Render<DockerInstanceDetail>(parameters => parameters.Add(component => component.DockerInstanceId, firstId));

            page.Render(parameters => parameters.Add(component => component.DockerInstanceId, secondId));
            pendingFirst.SetResult(CreateDetail(firstId, "Prod Host A"));

            await DetailPageAssert.SettleAsync(page).ConfigureAwait(false);

            DetailPageAssert.AssertLoading(page);
            Assert.DoesNotContain("Prod Host A", page.Markup, "The late record of the earlier route must not be shown");

            pendingSecond.SetResult(CreateDetail(secondId, "Prod Host B"));

            await DetailPageAssert.AssertLoadedAsync(page, "Prod Host B", "This Docker instance does not exist.").ConfigureAwait(false);

            Assert.DoesNotContain("Prod Host A", page.Markup, "The shown record must stay the one of the current route");
        }
    }

    /// <summary>
    /// Verify a failed load of the earlier route that ends after the route change keeps the current detail, shows no alert and is still logged once
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC3")]
    public async Task DockerInstanceDetailLateLoadFailureOfPreviousRouteKeepsCurrentDetail()
    {
        var exception = new InvalidOperationException("secret text");
        var logger = new TestLogger<DockerInstanceDetail>();
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            var pendingFirst = new TaskCompletionSource<DockerInstanceDetailViewData?>();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetDockerInstanceDetailAsync(firstId, Arg.Any<CancellationToken>())
                       .Returns(pendingFirst.Task);
            viewService.GetDockerInstanceDetailAsync(secondId, Arg.Any<CancellationToken>())
                       .Returns(CreateDetail(secondId, "Prod Host B"));

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton<ILogger<DockerInstanceDetail>>(logger);

            var page = testContext.Render<DockerInstanceDetail>(parameters => parameters.Add(component => component.DockerInstanceId, firstId));

            page.Render(parameters => parameters.Add(component => component.DockerInstanceId, secondId));

            await DetailPageAssert.AssertLoadedAsync(page, "Prod Host B", "This Docker instance does not exist.").ConfigureAwait(false);

            pendingFirst.SetException(exception);

            await DetailPageAssert.WaitForLoadLoggedAsync(page, logger, "LoadDockerInstanceDetail").ConfigureAwait(false);
            await DetailPageAssert.SettleAsync(page).ConfigureAwait(false);

            await DetailPageAssert.AssertLoadedAsync(page, "Prod Host B", "This Docker instance does not exist.").ConfigureAwait(false);
            DetailPageAssert.AssertLoadLoggedOnce(logger, "LoadDockerInstanceDetail", exception);
        }
    }

    /// <summary>
    /// Verify a record of the earlier route that arrives after the current load failed keeps the alert of the current load
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC4")]
    public async Task DockerInstanceDetailLateDetailAfterCurrentLoadFailureKeepsAlert()
    {
        var logger = new TestLogger<DockerInstanceDetail>();
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            var pendingFirst = new TaskCompletionSource<DockerInstanceDetailViewData?>();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetDockerInstanceDetailAsync(firstId, Arg.Any<CancellationToken>())
                       .Returns(pendingFirst.Task);
            viewService.GetDockerInstanceDetailAsync(secondId, Arg.Any<CancellationToken>())
                       .Returns(Task.FromException<DockerInstanceDetailViewData?>(new InvalidOperationException("secret text")));

            testContext.Services.AddSingleton(viewService);
            testContext.Services.AddSingleton<ILogger<DockerInstanceDetail>>(logger);

            var page = testContext.Render<DockerInstanceDetail>(parameters => parameters.Add(component => component.DockerInstanceId, firstId));

            page.Render(parameters => parameters.Add(component => component.DockerInstanceId, secondId));

            await DetailPageAssert.AssertLoadErrorAsync(page, "Loading the Docker instance failed: unexpected error", "secret text").ConfigureAwait(false);

            pendingFirst.SetResult(CreateDetail(firstId, "Prod Host A"));

            await DetailPageAssert.SettleAsync(page).ConfigureAwait(false);

            await DetailPageAssert.AssertLoadErrorAsync(page, "Loading the Docker instance failed: unexpected error", "secret text", "Prod Host A").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verify a re-render with the same identifier keeps the shown detail until the reload completes
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    [TestCategory("AC5")]
    public async Task DockerInstanceDetailSameRouteReloadKeepsDetailShown()
    {
        var testContext = BlazorTestContextFactory.Create();

        await using (testContext)
        {
            var id = Guid.NewGuid();
            var pending = new TaskCompletionSource<DockerInstanceDetailViewData?>();
            var viewService = Substitute.For<IApplicationViewService>();

            viewService.GetDockerInstanceDetailAsync(id, Arg.Any<CancellationToken>())
                       .Returns(CreateDetail(id, "Prod Host A"));

            testContext.Services.AddSingleton(viewService);

            var page = testContext.Render<DockerInstanceDetail>(parameters => parameters.Add(component => component.DockerInstanceId, id));

            await DetailPageAssert.AssertLoadedAsync(page, "Prod Host A", "This Docker instance does not exist.").ConfigureAwait(false);

            viewService.GetDockerInstanceDetailAsync(id, Arg.Any<CancellationToken>())
                       .Returns(pending.Task);

            page.Render(parameters => parameters.Add(component => component.DockerInstanceId, id));

            Assert.Contains("Prod Host A", page.Markup, "The shown detail must stay while the same route reloads");

            pending.SetResult(CreateDetail(id, "Prod Host A"));

            await DetailPageAssert.AssertLoadedAsync(page, "Prod Host A", "This Docker instance does not exist.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Create a detail for the load tests
    /// </summary>
    /// <param name="id">Identifier</param>
    /// <param name="name">Name</param>
    /// <returns>Detail</returns>
    private static DockerInstanceDetailViewData CreateDetail(Guid id, string name)
    {
        return new DockerInstanceDetailViewData
               {
                   Id = id,
                   Name = name,
               };
    }

    #endregion // Methods
}