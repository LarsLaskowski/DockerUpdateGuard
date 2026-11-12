using DockerUpdateGuard.DockerHub;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="RegistryMetadataService"/>
/// </summary>
[TestClass]
public class RegistryMetadataServiceTests
{
    #region Methods

    /// <summary>
    /// Verify a tag lookup for a registry without a client is reported as unsupported
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RegistryMetadataServiceGetTagAsyncWithoutClientReturnsUnsupportedAsync()
    {
        var service = new RegistryMetadataService([]);

        var result = await service.GetTagAsync(CreateReference(), cancellationToken: CancellationToken.None)
                                  .ConfigureAwait(false);

        AssertUnsupported(result.Status, result.Message, result.Data);
    }

    /// <summary>
    /// Verify a tag list lookup for a registry without a client is reported as unsupported
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RegistryMetadataServiceGetTagsAsyncWithoutClientReturnsUnsupportedAsync()
    {
        var service = new RegistryMetadataService([]);

        var result = await service.GetTagsAsync("registry.internal.test", "library/app", cancellationToken: CancellationToken.None)
                                  .ConfigureAwait(false);

        AssertUnsupported(result.Status, result.Message, result.Data);
    }

    /// <summary>
    /// Verify an image configuration lookup for a registry without a client is reported as unsupported
    /// </summary>
    /// <returns>Task</returns>
    [TestMethod]
    public async Task RegistryMetadataServiceGetImageConfigurationAsyncWithoutClientReturnsUnsupportedAsync()
    {
        var service = new RegistryMetadataService([]);

        var result = await service.GetImageConfigurationAsync(CreateReference(), CancellationToken.None)
                                  .ConfigureAwait(false);

        AssertUnsupported(result.Status, result.Message, result.Data);
    }

    #endregion // Methods

    #region Static methods

    /// <summary>
    /// Assert an unsupported result with a message and no data
    /// </summary>
    /// <param name="status">Result status</param>
    /// <param name="message">Result message</param>
    /// <param name="data">Result data</param>
    private static void AssertUnsupported(ExternalOperationStatus status, string? message, object? data)
    {
        Assert.AreEqual(ExternalOperationStatus.Unsupported, status, "A registry without a client must be unsupported");
        Assert.IsFalse(string.IsNullOrWhiteSpace(message), "The unsupported result must explain itself");
        Assert.IsNull(data, "An unsupported result must carry no data");
    }

    /// <summary>
    /// Create an image reference of a registry no client handles
    /// </summary>
    /// <returns>Image reference</returns>
    private static ImageReference CreateReference()
    {
        return new ImageReference
               {
                   Registry = "registry.internal.test",
                   Repository = "library/app",
                   Tag = "latest",
               };
    }

    #endregion // Static methods
}