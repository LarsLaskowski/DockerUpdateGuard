using System.Globalization;

using Bunit;
using Bunit.TestDoubles;

using DockerUpdateGuard.Portainer.Data;
using DockerUpdateGuard.Portainer.Interfaces;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

using NSubstitute;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Factory that creates bUnit test contexts wired up for MudBlazor components
/// </summary>
internal static class BlazorTestContextFactory
{
    #region Methods

    /// <summary>
    /// Create a bUnit test context with MudBlazor services, loose JS interop, and fake persistent component state
    /// </summary>
    /// <returns>Configured test context</returns>
    public static Bunit.BunitContext Create()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        var testContext = new Bunit.BunitContext();

        testContext.Services.AddMudServices();
        testContext.JSInterop.Mode = JSRuntimeMode.Loose;

        var persistentState = testContext.AddBunitPersistentComponentState();

        testContext.Services.AddSingleton(persistentState);
        testContext.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        testContext.Services.AddScoped<ProtectedLocalStorage>();

        return testContext;
    }

    /// <summary>
    /// Register a container action service that shows no container actions and render interactively, as a page that
    /// reads its renderer and injects the container action service needs
    /// </summary>
    /// <param name="testContext">Test context</param>
    /// <returns>Container action service substitute</returns>
    public static IContainerActionService AddHiddenContainerActions(this Bunit.BunitContext testContext)
    {
        var containerActionService = Substitute.For<IContainerActionService>();

        containerActionService.GetAvailabilityAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                              .Returns(new ContainerActionAvailabilityData());

        testContext.Services.AddSingleton(containerActionService);
        testContext.SetRendererInfo(new RendererInfo("Server", true));

        return containerActionService;
    }

    /// <summary>
    /// Resolve the fake persistent component state registered on the test context
    /// </summary>
    /// <param name="testContext">Test context</param>
    /// <returns>Fake persistent component state</returns>
    public static BunitPersistentComponentState GetPersistentComponentState(this Bunit.BunitContext testContext)
    {
        return testContext.Services.GetRequiredService<BunitPersistentComponentState>();
    }

    #endregion // Methods
}