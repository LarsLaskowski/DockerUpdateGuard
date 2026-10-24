using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Tests.Data;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="InstanceDiscoveryService"/>
/// </summary>
[TestClass]
public class InstanceDiscoveryServiceTests
{
    #region Properties

    /// <summary>
    /// Context for the tests
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// Verify configured Portainer settings create a linked endpoint for new Docker instances
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task InstanceDiscoveryServiceSynchronizeConfiguredInstancesAsyncWithNewPortainerConfigurationPersistsEndpointAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(CreateOptions(portainerEnabled: true));
                var service = new InstanceDiscoveryService(dbContext,
                                                           new TestLogger<InstanceDiscoveryService>(),
                                                           optionsMonitor);

                await service.SynchronizeConfiguredInstancesAsync(CancellationToken.None)
                             .ConfigureAwait(false);

                var dockerInstance = await dbContext.DockerInstances.Include(entity => entity.PortainerEndpoint)
                                                                    .SingleAsync(TestContext.CancellationToken)
                                                                    .ConfigureAwait(false);

                Assert.IsNotNull(dockerInstance.PortainerEndpoint, "A Portainer-enabled Docker instance must persist its related endpoint");
                Assert.AreEqual(dockerInstance.Id,
                                dockerInstance.PortainerEndpoint.DockerInstanceId,
                                "The Portainer endpoint must be linked to the synchronized Docker instance");
                Assert.AreEqual("Production Portainer",
                                dockerInstance.PortainerEndpoint.Name,
                                "The Portainer endpoint must use the derived display name");
            }
        }
    }

    /// <summary>
    /// Verify enabling Portainer for an existing Docker instance adds a single linked endpoint
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task InstanceDiscoveryServiceSynchronizeConfiguredInstancesAsyncWithExistingInstanceAddsPortainerEndpointAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                dbContext.DockerInstances.Add(new DockerInstance
                                              {
                                                  ConnectionKind = DockerConnectionKind.UnixSocket,
                                                  EndpointUri = "unix:///var/run/docker.sock",
                                                  Name = "Production",
                                                  Source = RegistrationSource.ConfigurationFile,
                                              });

                await dbContext.SaveChangesAsync(CancellationToken.None)
                               .ConfigureAwait(false);

                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(CreateOptions(portainerEnabled: true));
                var service = new InstanceDiscoveryService(dbContext,
                                                           new TestLogger<InstanceDiscoveryService>(),
                                                           optionsMonitor);

                await service.SynchronizeConfiguredInstancesAsync(CancellationToken.None)
                             .ConfigureAwait(false);

                var dockerInstance = await dbContext.DockerInstances.Include(entity => entity.PortainerEndpoint)
                                                                    .SingleAsync(TestContext.CancellationToken)
                                                                    .ConfigureAwait(false);
                var portainerEndpointCount = await dbContext.PortainerEndpoints.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.AreEqual(1,
                                portainerEndpointCount,
                                "Synchronizing an existing Docker instance must create exactly one Portainer endpoint");
                Assert.IsNotNull(dockerInstance.PortainerEndpoint, "Existing Docker instances must receive a Portainer endpoint when the integration becomes enabled");
                Assert.AreEqual(dockerInstance.Id,
                                dockerInstance.PortainerEndpoint.DockerInstanceId,
                                "The created Portainer endpoint must reference the existing Docker instance");
            }
        }
    }

    /// <summary>
    /// Verify instances no longer present in configuration are deleted together with related data
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task InstanceDiscoveryServiceSynchronizeConfiguredInstancesAsyncRemovesObsoleteInstancesAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var obsoleteInstance = new DockerInstance
                                       {
                                           ConnectionKind = DockerConnectionKind.UnixSocket,
                                           EndpointUri = "unix:///var/run/docker-old.sock",
                                           Name = "Obsolete",
                                           Source = RegistrationSource.ConfigurationFile,
                                           PortainerEndpoint = new PortainerEndpoint
                                                               {
                                                                   Name = "Obsolete Portainer",
                                                                   BaseUrl = "https://portainer.example.local",
                                                                   IsEnabled = true,
                                                               },
                                       };
                var obsoleteScanRun = new ScanRun
                                      {
                                          DockerInstance = obsoleteInstance,
                                          Type = ScanRunType.RuntimeContainer,
                                          Status = ScanRunStatus.Succeeded,
                                          TriggerSource = ScanTriggerSource.Manual,
                                          StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                                          CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-4),
                                      };

                dbContext.DockerInstances.Add(obsoleteInstance);
                dbContext.ScanRuns.Add(obsoleteScanRun);

                await dbContext.SaveChangesAsync(CancellationToken.None)
                               .ConfigureAwait(false);

                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(CreateOptions(portainerEnabled: false));
                var service = new InstanceDiscoveryService(dbContext,
                                                           new TestLogger<InstanceDiscoveryService>(),
                                                           optionsMonitor);

                await service.SynchronizeConfiguredInstancesAsync(CancellationToken.None)
                             .ConfigureAwait(false);

                var dockerInstances = await dbContext.DockerInstances.OrderBy(entity => entity.Name)
                                                                     .ToListAsync(TestContext.CancellationToken)
                                                                     .ConfigureAwait(false);
                var scanRunCount = await dbContext.ScanRuns.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);
                var portainerEndpointCount = await dbContext.PortainerEndpoints.CountAsync(TestContext.CancellationToken).ConfigureAwait(false);

                Assert.HasCount(1,
                                dockerInstances,
                                "Synchronizing configured instances must leave only the instances from configuration in persistence");
                Assert.AreEqual("Production",
                                dockerInstances[0].Name,
                                "Synchronizing configured instances must keep the configured Docker instance");
                Assert.AreEqual(0,
                                scanRunCount,
                                "Removing obsolete Docker instances must also remove their persisted scan runs");
                Assert.AreEqual(0,
                                portainerEndpointCount,
                                "Removing obsolete Docker instances must also remove their linked Portainer endpoints");
            }
        }
    }

    /// <summary>
    /// Verify the stored endpoint URI of a configured instance carries its user info masked
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task InstanceDiscoveryServiceSynchronizeConfiguredInstancesAsyncStoresEndpointWithUserInfoMaskedAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var options = CreateOptions("https://user:secret@docker.example.test:2376", false, null);
                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(options);
                var service = new InstanceDiscoveryService(dbContext,
                                                           new TestLogger<InstanceDiscoveryService>(),
                                                           optionsMonitor);

                await service.SynchronizeConfiguredInstancesAsync(CancellationToken.None)
                             .ConfigureAwait(false);

                var dockerInstance = await dbContext.DockerInstances.SingleAsync(TestContext.CancellationToken)
                                                                    .ConfigureAwait(false);

                Assert.AreEqual("https://***@docker.example.test:2376/",
                                dockerInstance.EndpointUri,
                                "The stored endpoint URI must carry the user info masked");
                Assert.AreEqual(DockerConnectionKind.Https,
                                dockerInstance.ConnectionKind,
                                "Masking the stored endpoint must not change the connection kind derived from the configuration");
                Assert.AreEqual("https://user:secret@docker.example.test:2376",
                                options.DockerInstances[0].BaseUrl,
                                "Masking the stored endpoint must leave the configured options untouched");
            }
        }
    }

    /// <summary>
    /// Verify the stored Portainer base URL of a configured instance carries its user info masked
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task InstanceDiscoveryServiceSynchronizeConfiguredInstancesAsyncStoresPortainerBaseUrlMaskedAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var options = CreateOptions("unix:///var/run/docker.sock", true, "https://admin:S3cret@portainer.example.test:9443");
                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(options);
                var service = new InstanceDiscoveryService(dbContext,
                                                           new TestLogger<InstanceDiscoveryService>(),
                                                           optionsMonitor);

                await service.SynchronizeConfiguredInstancesAsync(CancellationToken.None)
                             .ConfigureAwait(false);

                var portainerEndpoint = await dbContext.PortainerEndpoints.SingleAsync(TestContext.CancellationToken)
                                                                          .ConfigureAwait(false);

                Assert.AreEqual("https://***@portainer.example.test:9443/",
                                portainerEndpoint.BaseUrl,
                                "The stored Portainer base URL must carry the user info masked");
            }
        }
    }

    /// <summary>
    /// Verify the unsupported connection settings log names the endpoint with its user info masked
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task InstanceDiscoveryServiceSynchronizeConfiguredInstancesAsyncUnsupportedEndpointLogMasksUserInfoAsync()
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                var logger = new TestLogger<InstanceDiscoveryService>();
                var options = CreateOptions("ssh://user:secret@docker.example.test", false, null);
                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(options);
                var service = new InstanceDiscoveryService(dbContext, logger, optionsMonitor);

                await service.SynchronizeConfiguredInstancesAsync(CancellationToken.None)
                             .ConfigureAwait(false);

                var warning = logger.Entries.Single(entry => entry.EventId.Id == 2091);

                Assert.Contains("ssh://***@docker.example.test/",
                                warning.Message,
                                "The unsupported connection settings log must name the endpoint with its user info masked");
                Assert.DoesNotContain("secret", warning.Message, "The unsupported connection settings log must not contain the password");
            }
        }
    }

    /// <summary>
    /// Verify a stored Portainer base URL is rewritten masked when Portainer is disabled and an already masked one stays
    /// </summary>
    /// <param name="storedBaseUrl">Base URL stored before the synchronization</param>
    /// <returns>Task</returns>
    [TestMethod]
    [DataRow("https://admin:S3cret@portainer.example.test:9443")]
    [DataRow("https://***@portainer.example.test:9443/")]
    public async Task InstanceDiscoveryServiceSynchronizeConfiguredInstancesAsyncDisabledPortainerMasksStoredBaseUrlAsync(string storedBaseUrl)
    {
        using (var database = new SqliteTestDatabase())
        {
            var dbContext = database.CreateDbContext();

            await using (dbContext.ConfigureAwait(false))
            {
                dbContext.DockerInstances.Add(new DockerInstance
                                              {
                                                  ConnectionKind = DockerConnectionKind.UnixSocket,
                                                  EndpointUri = "unix:///var/run/docker.sock",
                                                  Name = "Production",
                                                  Source = RegistrationSource.ConfigurationFile,
                                                  PortainerEndpoint = new PortainerEndpoint
                                                                      {
                                                                          Name = "Production Portainer",
                                                                          BaseUrl = storedBaseUrl,
                                                                          IsEnabled = true,
                                                                      },
                                              });

                await dbContext.SaveChangesAsync(CancellationToken.None)
                               .ConfigureAwait(false);

                var optionsMonitor = new TestOptionsMonitor<DockerUpdateGuardOptions>(CreateOptions("unix:///var/run/docker.sock", false, null));
                var service = new InstanceDiscoveryService(dbContext,
                                                           new TestLogger<InstanceDiscoveryService>(),
                                                           optionsMonitor);

                await service.SynchronizeConfiguredInstancesAsync(CancellationToken.None)
                             .ConfigureAwait(false);

                var portainerEndpoint = await dbContext.PortainerEndpoints.SingleAsync(TestContext.CancellationToken)
                                                                          .ConfigureAwait(false);

                Assert.AreEqual("https://***@portainer.example.test:9443/",
                                portainerEndpoint.BaseUrl,
                                "A stored Portainer base URL must carry its user info masked after synchronization, also when Portainer is disabled");
                Assert.IsFalse(portainerEndpoint.IsEnabled, "A disabled Portainer configuration must disable the stored endpoint");
            }
        }
    }

    /// <summary>
    /// Create Docker instance options with a given endpoint and Portainer base URL
    /// </summary>
    /// <param name="baseUrl">Docker endpoint URL</param>
    /// <param name="portainerEnabled">Whether Portainer should be enabled</param>
    /// <param name="portainerBaseUrl">Portainer base URL</param>
    /// <returns>Options</returns>
    private static DockerUpdateGuardOptions CreateOptions(string baseUrl, bool portainerEnabled, string? portainerBaseUrl)
    {
        return new DockerUpdateGuardOptions
               {
                   DockerInstances = [
                                         new DockerInstanceOptions
                                         {
                                             BaseUrl = baseUrl,
                                             Enabled = true,
                                             Name = "Production",
                                             Portainer = new PortainerOptions
                                                         {
                                                             ApiToken = "pat-value",
                                                             BaseUrl = portainerBaseUrl,
                                                             Enabled = portainerEnabled,
                                                             EndpointId = "1",
                                                             RequestTimeoutSeconds = 15
                                                         }
                                         },
                                     ],
               };
    }

    /// <summary>
    /// Create representative Docker instance options for synchronization tests
    /// </summary>
    /// <param name="portainerEnabled">Whether Portainer should be enabled</param>
    /// <returns>Options</returns>
    private static DockerUpdateGuardOptions CreateOptions(bool portainerEnabled)
    {
        return new DockerUpdateGuardOptions
               {
                   DockerInstances = [
                                         new DockerInstanceOptions
                                         {
                                             BaseUrl = "unix:///var/run/docker.sock",
                                             Enabled = true,
                                             Name = "Production",
                                             Portainer = new PortainerOptions
                                                         {
                                                             ApiToken = "pat-value",
                                                             BaseUrl = "https://portainer.example.local",
                                                             Enabled = portainerEnabled,
                                                             EndpointId = "1",
                                                             RequestTimeoutSeconds = 15
                                                         }
                                         },
                                     ],
               };
    }

    #endregion // Methods
}