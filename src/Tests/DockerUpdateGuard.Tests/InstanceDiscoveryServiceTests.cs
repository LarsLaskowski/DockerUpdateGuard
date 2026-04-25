using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="InstanceDiscoveryService"/>
/// </summary>
[TestClass]
public class InstanceDiscoveryServiceTests
{
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
                                                                    .SingleAsync()
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
                                                                    .SingleAsync()
                                                                    .ConfigureAwait(false);
                var portainerEndpointCount = await dbContext.PortainerEndpoints.CountAsync().ConfigureAwait(false);

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
                                                                     .ToListAsync()
                                                                     .ConfigureAwait(false);
                var scanRunCount = await dbContext.ScanRuns.CountAsync().ConfigureAwait(false);
                var portainerEndpointCount = await dbContext.PortainerEndpoints.CountAsync().ConfigureAwait(false);

                Assert.AreEqual(1,
                                dockerInstances.Count,
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