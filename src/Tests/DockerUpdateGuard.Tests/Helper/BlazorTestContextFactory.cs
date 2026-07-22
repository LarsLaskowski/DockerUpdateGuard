using System.Globalization;

using Bunit;
using Bunit.TestDoubles;

using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
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