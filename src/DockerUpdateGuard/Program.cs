using DockerUpdateGuard.Components;

using MudBlazor.Services;

namespace DockerUpdateGuard;

/// <summary>
/// Configures and runs the DockerUpdateGuard web application, which provides a user interface for monitoring and managing Docker updates. The application is built using ASP.NET Core and Razor Components, and it includes features such as error handling, status code pages, HTTPS redirection, antiforgery protection, and static asset mapping. The main entry point of the application initializes the necessary services and middleware before starting the web server to listen for incoming requests
/// </summary>
public static partial class Program
{
    #region Main entry point

    /// <summary>
    /// Configures and runs the DockerUpdateGuard web application using the specified command-line arguments
    /// </summary>
    /// <param name="args">An array of command-line arguments used to configure the application at startup</param>
    /// <returns>A task that represents the asynchronous operation of running the web application</returns>
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddDockerUpdateGuardHost(builder.Configuration);
        builder.Services.AddMudServices();
        builder.Services.AddRazorComponents()
                        .AddInteractiveServerComponents();

        var app = builder.Build();

        if (app.Environment.IsDevelopment() == false)
        {
            app.UseExceptionHandler("/error", createScopeForErrors: true);
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
           .AddInteractiveServerRenderMode();

        await app.InitializeDockerUpdateGuardAsync()
                 .ConfigureAwait(false);

        await app.RunAsync().ConfigureAwait(false);
    }

    #endregion // Main entry point
}