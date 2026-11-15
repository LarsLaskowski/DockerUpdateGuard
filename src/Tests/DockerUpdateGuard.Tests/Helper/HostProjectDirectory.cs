namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Resolves the host project directory for tests that read its shipped files
/// </summary>
internal static class HostProjectDirectory
{
    #region Methods

    /// <summary>
    /// Resolve the host project directory from the current test output
    /// </summary>
    /// <returns>Host project directory path</returns>
    internal static string GetPath()
    {
        var hostProjectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                                                            "..",
                                                            "..",
                                                            "..",
                                                            "..",
                                                            "..",
                                                            "DockerUpdateGuard"));

        Assert.IsTrue(Directory.Exists(hostProjectPath), $"The host project directory '{hostProjectPath}' must exist for configuration verification");

        return hostProjectPath;
    }

    #endregion // Methods
}