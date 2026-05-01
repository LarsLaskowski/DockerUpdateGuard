using DockerUpdateGuard.Docker;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Enums;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="DerivedBaseRuntimeDetector"/>
/// </summary>
[TestClass]
public class DerivedBaseRuntimeDetectorTests
{
    #region Methods

    /// <summary>
    /// Verify inspect environment variables win over history markers
    /// </summary>
    [TestMethod]
    public void DerivedBaseRuntimeDetectorDetectPrefersInspectEnvironmentVersion()
    {
        var detector = new DerivedBaseRuntimeDetector();
        var inspectData = new DockerImageInspectData
                          {
                              EnvironmentVariables = ["DOTNET_VERSION=9.0.13"],
                          };
        var historyEntries = new[]
                             {
                                 new DockerImageHistoryEntryData
                                 {
                                     CreatedBy = "/bin/sh -c #(nop)  ENV DOTNET_VERSION=9.0.11",
                                 },
                             };

        var result = detector.Detect(inspectData, historyEntries);

        Assert.IsNotNull(result, "The detector must recognize .NET metadata from inspect environment variables");
        Assert.AreEqual(DerivedBaseRuntimeKind.DotNet,
                        result.Kind,
                        "The detector must classify DOTNET_VERSION markers as .NET runtimes");
        Assert.AreEqual(DerivedBaseRuntimeDetectionSource.InspectEnvironment,
                        result.Source,
                        "Inspect environment variables must take precedence over history markers");
        Assert.AreEqual("9.0.13",
                        result.RuntimeVersion?.ToString(3),
                        "The detector must surface the inspect environment runtime version");
    }

    /// <summary>
    /// Verify history markers are used when inspect environment variables are missing
    /// </summary>
    [TestMethod]
    public void DerivedBaseRuntimeDetectorDetectUsesHistoryEnvironmentMarkerWhenInspectDataIsMissing()
    {
        var detector = new DerivedBaseRuntimeDetector();
        var historyEntries = new[]
                             {
                                 new DockerImageHistoryEntryData
                                 {
                                     CreatedBy = "/bin/sh -c #(nop)  ENV DOTNET_VERSION=8.0.15",
                                 },
                             };

        var result = detector.Detect(inspectData: null, historyEntries);

        Assert.IsNotNull(result, "The detector must recognize .NET metadata from Docker history markers");
        Assert.AreEqual(DerivedBaseRuntimeKind.DotNet,
                        result.Kind,
                        "The detector must classify DOTNET_VERSION markers as .NET runtimes");
        Assert.AreEqual(DerivedBaseRuntimeDetectionSource.HistoryEnvironment,
                        result.Source,
                        "Docker history markers must be used when inspect data is unavailable");
        Assert.AreEqual("8.0.15",
                        result.RuntimeVersion?.ToString(3),
                        "The detector must surface the history environment runtime version");
    }

    /// <summary>
    /// Verify images without .NET markers are ignored
    /// </summary>
    [TestMethod]
    public void DerivedBaseRuntimeDetectorDetectWithoutDotNetMarkersReturnsNull()
    {
        var detector = new DerivedBaseRuntimeDetector();
        var inspectData = new DockerImageInspectData
                          {
                              EnvironmentVariables = ["PATH=/usr/local/bin"],
                          };
        var historyEntries = new[]
                             {
                                 new DockerImageHistoryEntryData
                                 {
                                     CreatedBy = "/bin/sh -c apk add curl",
                                 },
                             };

        var result = detector.Detect(inspectData, historyEntries);

        Assert.IsNull(result, "The detector must ignore images that do not expose .NET runtime markers");
    }

    /// <summary>
    /// Verify NGINX environment variables are detected as NGINX base runtimes
    /// </summary>
    [TestMethod]
    public void DerivedBaseRuntimeDetectorDetectRecognizesNginxVersionFromInspectEnvironment()
    {
        var detector = new DerivedBaseRuntimeDetector();
        var inspectData = new DockerImageInspectData
                          {
                              EnvironmentVariables = ["NGINX_VERSION=1.29.1"],
                          };

        var result = detector.Detect(inspectData, historyEntries: []);

        Assert.IsNotNull(result, "The detector must recognize NGINX metadata from inspect environment variables");
        Assert.AreEqual(DerivedBaseRuntimeKind.Nginx,
                        result.Kind,
                        "The detector must classify NGINX_VERSION markers as NGINX runtimes");
        Assert.AreEqual("1.29.1",
                        result.RuntimeVersion?.ToString(3),
                        "The detector must surface the NGINX version from inspect environment variables");
    }

    #endregion // Methods
}