using DockerUpdateGuard.DockerHub;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DockerHubHosts"/>
/// </summary>
[TestClass]
public class DockerHubHostsTests
{
    #region Methods

    /// <summary>
    /// Verify only an exact Docker Hub host name, in any case, is recognized as a Docker Hub host
    /// </summary>
    /// <param name="host">Host to check</param>
    /// <param name="expected">Expected result</param>
    [TestMethod]
    [TestCategory("AC5")]
    [DataRow("docker.io", true)]
    [DataRow("index.docker.io", true)]
    [DataRow("registry-1.docker.io", true)]
    [DataRow("hub.docker.com", true)]
    [DataRow("docker.com", true)]
    [DataRow("www.docker.com", true)]
    [DataRow("INDEX.DOCKER.IO", true)]
    [DataRow(null, false)]
    [DataRow("", false)]
    [DataRow(" ", false)]
    [DataRow(" docker.io", false)]
    [DataRow("https://docker.io", false)]
    [DataRow("docker.io:443", false)]
    [DataRow("docker.io/x", false)]
    [DataRow("ghcr.io", false)]
    public void DockerHubHostsIsDockerHubHostValueReturnsExpected(string? host, bool expected)
    {
        var isDockerHubHost = DockerHubHosts.IsDockerHubHost(host);

        Assert.AreEqual(expected, isDockerHubHost, $"Host '{host}' must be judged by an exact, case-insensitive match against the Docker Hub host names without trimming");
    }

    #endregion // Methods
}