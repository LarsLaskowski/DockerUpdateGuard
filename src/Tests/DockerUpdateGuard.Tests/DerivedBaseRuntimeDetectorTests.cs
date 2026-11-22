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
    #region Static methods

    /// <summary>
    /// Collect the string form of every public instance property value of an object
    /// </summary>
    /// <param name="instance">Object to inspect</param>
    /// <returns>String forms of the property values</returns>
    private static List<string> GetPublicPropertyTexts(object instance)
    {
        var texts = new List<string>();

        foreach (var property in instance.GetType().GetProperties())
        {
            texts.Add(property.GetValue(instance)?.ToString() ?? string.Empty);
        }

        return texts;
    }

    #endregion // Static methods

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
                                 }
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
                                 }
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
    /// Verify the descriptor carries no raw text of further variables of a history environment line
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void DerivedBaseRuntimeDetectorDetectHistoryEnvironmentWithFurtherVariablesKeepsNoRawText()
    {
        var detector = new DerivedBaseRuntimeDetector();
        var historyEntries = new[]
                             {
                                 new DockerImageHistoryEntryData
                                 {
                                     CreatedBy = "/bin/sh -c #(nop)  ENV DOTNET_VERSION=8.0.11 API_TOKEN=s3cr3t-value",
                                 }
                             };

        var result = detector.Detect(inspectData: null, historyEntries);

        Assert.IsNotNull(result, "The detector must recognize .NET metadata from the history line");
        Assert.AreEqual(DerivedBaseRuntimeDetectionSource.HistoryEnvironment,
                        result.Source,
                        "The history line must be reported as the detection source");
        Assert.AreEqual("8.0.11",
                        result.RuntimeVersion?.ToString(3),
                        "The detector must surface the version of the matched variable");

        var text = string.Join("|", GetPublicPropertyTexts(result));

        Assert.DoesNotContain("s3cr3t-value",
                              text,
                              "No public property of the descriptor may contain the value of a further variable");
        Assert.DoesNotContain("API_TOKEN",
                              text,
                              "No public property of the descriptor may contain the name of a further variable");
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
                                 }
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