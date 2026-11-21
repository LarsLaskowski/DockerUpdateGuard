using DockerUpdateGuard.Configuration;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DockerUpdateGuardOptionsPostConfigure"/>
/// </summary>
[TestClass]
public class DockerUpdateGuardOptionsPostConfigureTests
{
    #region Methods

    /// <summary>
    /// Verify the name of every Docker instance is trimmed of leading and trailing whitespace
    /// </summary>
    [TestMethod]
    [TestCategory("AC4")]
    public void DockerUpdateGuardOptionsPostConfigurePostConfigureTrimsEveryInstanceName()
    {
        var options = new DockerUpdateGuardOptions
                      {
                          DockerInstances = [
                                                new DockerInstanceOptions
                                                {
                                                    Name = " Production "
                                                },
                                                new DockerInstanceOptions
                                                {
                                                    Name = "\tStaging Host "
                                                },
                                            ],
                      };
        var postConfigure = new DockerUpdateGuardOptionsPostConfigure();

        postConfigure.PostConfigure(null, options);

        Assert.AreSequenceEqual(["Production", "Staging Host"],
                                options.DockerInstances.Select(instance => instance.Name).ToArray(),
                                "Leading and trailing whitespace must be removed while inner spaces stay");
    }

    /// <summary>
    /// Verify the case of a name is kept
    /// </summary>
    [TestMethod]
    [TestCategory("AC4")]
    public void DockerUpdateGuardOptionsPostConfigurePostConfigureKeepsCaseOfName()
    {
        var options = new DockerUpdateGuardOptions
                      {
                          DockerInstances = [
                                                new DockerInstanceOptions
                                                {
                                                    Name = "  PrOd  "
                                                }
                                            ],
                      };
        var postConfigure = new DockerUpdateGuardOptionsPostConfigure();

        postConfigure.PostConfigure(Microsoft.Extensions.Options.Options.DefaultName, options);

        Assert.AreEqual("PrOd", options.DockerInstances[0].Name, "Only the surrounding whitespace may be removed, not the case");
    }

    /// <summary>
    /// Verify an empty name stays empty so the validator reports it as missing
    /// </summary>
    [TestMethod]
    [TestCategory("AC4")]
    public void DockerUpdateGuardOptionsPostConfigurePostConfigureKeepsEmptyName()
    {
        var options = new DockerUpdateGuardOptions
                      {
                          DockerInstances = [
                                                new DockerInstanceOptions
                                                {
                                                    Name = string.Empty
                                                }
                                            ],
                      };
        var postConfigure = new DockerUpdateGuardOptionsPostConfigure();

        postConfigure.PostConfigure(null, options);

        Assert.AreEqual(string.Empty, options.DockerInstances[0].Name, "An empty name must stay empty");
    }

    /// <summary>
    /// Verify a whitespace-only name becomes empty
    /// </summary>
    [TestMethod]
    [TestCategory("AC5")]
    public void DockerUpdateGuardOptionsPostConfigurePostConfigureTurnsWhitespaceOnlyNameIntoEmptyName()
    {
        var options = new DockerUpdateGuardOptions
                      {
                          DockerInstances = [
                                                new DockerInstanceOptions
                                                {
                                                    Name = "   "
                                                }
                                            ],
                      };
        var postConfigure = new DockerUpdateGuardOptionsPostConfigure();

        postConfigure.PostConfigure(null, options);

        Assert.AreEqual(string.Empty, options.DockerInstances[0].Name, "A whitespace-only name must become empty so that the validator reports it as missing");
    }

    /// <summary>
    /// Verify options without Docker instances are accepted
    /// </summary>
    [TestMethod]
    [TestCategory("AC4")]
    public void DockerUpdateGuardOptionsPostConfigurePostConfigureWithoutInstancesKeepsListEmpty()
    {
        var options = new DockerUpdateGuardOptions();
        var postConfigure = new DockerUpdateGuardOptionsPostConfigure();

        postConfigure.PostConfigure(null, options);

        Assert.IsEmpty(options.DockerInstances, "Options without Docker instances must stay without instances");
    }

    /// <summary>
    /// Verify the Docker Hub registry is trimmed of leading and trailing whitespace
    /// </summary>
    [TestMethod]
    [TestCategory("AC4")]
    public void DockerUpdateGuardOptionsPostConfigurePostConfigureTrimsDockerHubRegistry()
    {
        var options = new DockerUpdateGuardOptions
                      {
                          DockerHub = new DockerHubOptions
                                      {
                                          Registry = " Docker.IO ",
                                      },
                      };
        var postConfigure = new DockerUpdateGuardOptionsPostConfigure();

        postConfigure.PostConfigure(null, options);

        Assert.AreEqual("Docker.IO", options.DockerHub.Registry, "Only the surrounding whitespace of the Docker Hub registry may be removed, not its case");
    }

    /// <summary>
    /// Verify missing options are rejected
    /// </summary>
    [TestMethod]
    public void DockerUpdateGuardOptionsPostConfigurePostConfigureWithNullOptionsThrows()
    {
        var postConfigure = new DockerUpdateGuardOptionsPostConfigure();

        Assert.Throws<ArgumentNullException>(() => postConfigure.PostConfigure(null, null!), "Missing options must be rejected");
    }

    #endregion // Methods
}