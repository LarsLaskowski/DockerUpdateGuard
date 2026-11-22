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
    /// Assert the detected runtime
    /// </summary>
    /// <param name="result">Detection result</param>
    /// <param name="kind">Expected kind</param>
    /// <param name="source">Expected source</param>
    /// <param name="version">Expected version</param>
    private static void AssertDetected(DerivedBaseRuntimeDescriptor? result,
                                       DerivedBaseRuntimeKind kind,
                                       DerivedBaseRuntimeDetectionSource source,
                                       string version)
    {
        Assert.IsNotNull(result, "The detector must recognize the runtime marker");
        Assert.AreEqual(kind, result.Kind, "The detector must classify the marker by runtime kind");
        Assert.AreEqual(source, result.Source, "The detector must report the matching detection source");
        Assert.AreEqual(version, result.RuntimeVersion?.ToString(3), "The detector must surface the marker version");
    }

    /// <summary>
    /// Detect a runtime from one history line without inspect data
    /// </summary>
    /// <param name="createdBy">History line</param>
    /// <returns>Detection result</returns>
    private static DerivedBaseRuntimeDescriptor? DetectFromHistory(string createdBy)
    {
        var detector = new DerivedBaseRuntimeDetector();
        var historyEntries = new[]
                             {
                                 new DockerImageHistoryEntryData
                                 {
                                     CreatedBy = createdBy,
                                 }
                             };

        return detector.Detect(inspectData: null, historyEntries);
    }

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
    /// Verify the ASP.NET inspect environment variable is detected as a .NET runtime
    /// </summary>
    [TestMethod]
    public void DerivedBaseRuntimeDetectorDetectRecognizesAspNetVersionFromInspectEnvironment()
    {
        var detector = new DerivedBaseRuntimeDetector();
        var inspectData = new DockerImageInspectData
                          {
                              EnvironmentVariables = ["ASPNET_VERSION=8.0.4"],
                          };

        var result = detector.Detect(inspectData, historyEntries: null);

        AssertDetected(result, DerivedBaseRuntimeKind.DotNet, DerivedBaseRuntimeDetectionSource.InspectAspNetEnvironment, "8.0.4");
    }

    /// <summary>
    /// Verify the ASP.NET history marker is detected as a .NET runtime
    /// </summary>
    [TestMethod]
    public void DerivedBaseRuntimeDetectorDetectRecognizesAspNetVersionFromHistoryEnvironment()
    {
        var result = DetectFromHistory("/bin/sh -c #(nop)  ENV ASPNET_VERSION=8.0.5");

        AssertDetected(result, DerivedBaseRuntimeKind.DotNet, DerivedBaseRuntimeDetectionSource.HistoryAspNetEnvironment, "8.0.5");
    }

    /// <summary>
    /// Verify an explicit .NET image reference in the history is detected
    /// </summary>
    [TestMethod]
    public void DerivedBaseRuntimeDetectorDetectRecognizesDotNetImageReferenceFromHistoryCommand()
    {
        var result = DetectFromHistory("COPY --from=mcr.microsoft.com/dotnet/aspnet:9.0.2 /app /app");

        AssertDetected(result, DerivedBaseRuntimeKind.DotNet, DerivedBaseRuntimeDetectionSource.HistoryCommand, "9.0.2");
    }

    /// <summary>
    /// Verify the NGINX history marker is detected as an NGINX runtime
    /// </summary>
    [TestMethod]
    public void DerivedBaseRuntimeDetectorDetectRecognizesNginxVersionFromHistoryEnvironment()
    {
        var result = DetectFromHistory("/bin/sh -c #(nop)  ENV NGINX_VERSION=1.27.3");

        AssertDetected(result, DerivedBaseRuntimeKind.Nginx, DerivedBaseRuntimeDetectionSource.HistoryEnvironment, "1.27.3");
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