using Bunit;
using Bunit.TestDoubles;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

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
    public static Bunit.TestContext Create()
    {
        var testContext = new Bunit.TestContext();

        testContext.Services.AddMudServices();
        testContext.JSInterop.Mode = JSRuntimeMode.Loose;

        var persistentState = testContext.AddFakePersistentComponentState();

        testContext.Services.AddSingleton(persistentState);

        return testContext;
    }

    /// <summary>
    /// Resolve the fake persistent component state registered on the test context
    /// </summary>
    /// <param name="testContext">Test context</param>
    /// <returns>Fake persistent component state</returns>
    public static FakePersistentComponentState GetPersistentComponentState(this Bunit.TestContext testContext)
    {
        return testContext.Services.GetRequiredService<FakePersistentComponentState>();
    }

    #endregion // Methods
}