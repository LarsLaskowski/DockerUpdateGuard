using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data;
using DockerUpdateGuard.Images.Interfaces;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ApplicationInitializationExtensions"/>
/// </summary>
[TestClass]
public class ApplicationInitializationExtensionsTests
{
    #region Methods

    /// <summary>
    /// Verify the startup initialization migrates with the registered time provider and synchronizes the instances
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task ApplicationInitializationExtensionsInitializeDockerUpdateGuardMigratesAndSynchronizesAsync()
    {
        var instanceDiscoveryService = Substitute.For<IInstanceDiscoveryService>();
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddDbContext<DockerUpdateGuardDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.AddSingleton(instanceDiscoveryService);
        builder.Services.AddSingleton<ApplicationTelemetry>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(Options.Create(new DockerUpdateGuardOptions()));

        var application = builder.Build();

        await using (application.ConfigureAwait(false))
        {
            await application.InitializeDockerUpdateGuardAsync()
                             .ConfigureAwait(false);
        }

        await instanceDiscoveryService.Received(1)
                                      .SynchronizeConfiguredInstancesAsync(CancellationToken.None)
                                      .ConfigureAwait(false);
    }

    #endregion // Methods
}