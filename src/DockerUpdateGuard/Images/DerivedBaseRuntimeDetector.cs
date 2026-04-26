using System.Text.RegularExpressions;

using DockerUpdateGuard.Docker;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Detects derived base runtimes from local Docker image metadata
/// </summary>
public partial class DerivedBaseRuntimeDetector : IDerivedBaseRuntimeDetector
{
    #region Methods

    /// <inheritdoc/>
    public DerivedBaseRuntimeDescriptor? Detect(DockerImageInspectData? inspectData, IReadOnlyList<DockerImageHistoryEntryData>? historyEntries)
    {
        var candidates = new List<RuntimeCandidate>();

        CollectInspectCandidates(inspectData, candidates);
        CollectHistoryCandidates(historyEntries, candidates);

        var bestCandidate = candidates.OrderBy(candidate => candidate.HasExactVersion == false)
                                      .ThenBy(candidate => candidate.SourcePriority)
                                      .ThenBy(candidate => candidate.Order)
                                      .FirstOrDefault();

        if (bestCandidate is null)
        {
            return null;
        }

        return new DerivedBaseRuntimeDescriptor
               {
                   Kind = bestCandidate.Kind,
                   RuntimeVersion = bestCandidate.Version,
                   ChannelVersion = bestCandidate.ChannelVersion,
                   Source = bestCandidate.Source,
                   Evidence = bestCandidate.Evidence,
               };
    }

    /// <summary>
    /// Collect detection candidates from image inspect metadata
    /// </summary>
    /// <param name="inspectData">Inspect payload</param>
    /// <param name="candidates">Target candidate list</param>
    private static void CollectInspectCandidates(DockerImageInspectData? inspectData, IList<RuntimeCandidate> candidates)
    {
        if (inspectData?.EnvironmentVariables is null)
        {
            return;
        }

        foreach (var environmentVariable in inspectData.EnvironmentVariables)
        {
            if (TryParseEnvironmentVariable(environmentVariable, "DOTNET_VERSION", out var dotNetVersion))
            {
                AddVersionCandidate(candidates,
                                    DerivedBaseRuntimeKind.DotNet,
                                    dotNetVersion,
                                    DerivedBaseRuntimeDetectionSource.InspectEnvironment,
                                    sourcePriority: 0,
                                    order: 0,
                                    environmentVariable);
            }

            if (TryParseEnvironmentVariable(environmentVariable, "ASPNET_VERSION", out var aspNetVersion))
            {
                AddVersionCandidate(candidates,
                                    DerivedBaseRuntimeKind.DotNet,
                                    aspNetVersion,
                                    DerivedBaseRuntimeDetectionSource.InspectAspNetEnvironment,
                                    sourcePriority: 2,
                                    order: 0,
                                    environmentVariable);
            }

            if (TryParseEnvironmentVariable(environmentVariable, "NGINX_VERSION", out var nginxVersion))
            {
                AddVersionCandidate(candidates,
                                    DerivedBaseRuntimeKind.Nginx,
                                    nginxVersion,
                                    DerivedBaseRuntimeDetectionSource.InspectEnvironment,
                                    sourcePriority: 1,
                                    order: 0,
                                    environmentVariable);
            }
        }
    }

    /// <summary>
    /// Collect detection candidates from image history metadata
    /// </summary>
    /// <param name="historyEntries">History entries</param>
    /// <param name="candidates">Target candidate list</param>
    private static void CollectHistoryCandidates(IReadOnlyList<DockerImageHistoryEntryData>? historyEntries, IList<RuntimeCandidate> candidates)
    {
        if (historyEntries is null)
        {
            return;
        }

        for (var index = 0; index < historyEntries.Count; index++)
        {
            var historyEntry = historyEntries[index];
            var entryText = $"{historyEntry.CreatedBy} {historyEntry.Comment}";

            if (TryExtractRuntimeVersion(entryText, "DOTNET_VERSION", out var dotNetVersion))
            {
                AddVersionCandidate(candidates,
                                    DerivedBaseRuntimeKind.DotNet,
                                    dotNetVersion,
                                    DerivedBaseRuntimeDetectionSource.HistoryEnvironment,
                                    sourcePriority: 1,
                                    order: index,
                                    entryText);
            }

            if (TryExtractRuntimeVersion(entryText, "ASPNET_VERSION", out var aspNetVersion))
            {
                AddVersionCandidate(candidates,
                                    DerivedBaseRuntimeKind.DotNet,
                                    aspNetVersion,
                                    DerivedBaseRuntimeDetectionSource.HistoryAspNetEnvironment,
                                    sourcePriority: 3,
                                    order: index,
                                    entryText);
            }

            var repositoryMatch = HistoryRepositoryVersionRegex().Match(entryText);

            if (repositoryMatch.Success)
            {
                AddVersionCandidate(candidates,
                                    DerivedBaseRuntimeKind.DotNet,
                                    repositoryMatch.Groups["version"].Value,
                                    DerivedBaseRuntimeDetectionSource.HistoryCommand,
                                    sourcePriority: 4,
                                    order: index,
                                    entryText);
            }

            if (TryExtractRuntimeVersion(entryText, "NGINX_VERSION", out var nginxVersion))
            {
                AddVersionCandidate(candidates,
                                    DerivedBaseRuntimeKind.Nginx,
                                    nginxVersion,
                                    DerivedBaseRuntimeDetectionSource.HistoryEnvironment,
                                    sourcePriority: 2,
                                    order: index,
                                    entryText);
            }
        }
    }

    /// <summary>
    /// Add a version candidate when the version can be parsed
    /// </summary>
    /// <param name="candidates">Candidate list</param>
    /// <param name="kind">Runtime kind</param>
    /// <param name="rawVersion">Raw version string</param>
    /// <param name="source">Detection source</param>
    /// <param name="sourcePriority">Source priority</param>
    /// <param name="order">Encounter order</param>
    /// <param name="evidence">Evidence string</param>
    private static void AddVersionCandidate(IList<RuntimeCandidate> candidates,
                                            DerivedBaseRuntimeKind kind,
                                            string rawVersion,
                                            DerivedBaseRuntimeDetectionSource source,
                                            int sourcePriority,
                                            int order,
                                            string? evidence)
    {
        if (TryParseRuntimeVersion(rawVersion, out var version) == false || version is null)
        {
            return;
        }

        candidates.Add(new RuntimeCandidate
                       {
                           Kind = kind,
                           Version = version,
                           ChannelVersion = $"{version.Major}.{version.Minor}",
                           Source = source,
                           SourcePriority = sourcePriority,
                           Order = order,
                           HasExactVersion = true,
                           Evidence = evidence,
                       });
    }

    /// <summary>
    /// Parse an environment variable value
    /// </summary>
    /// <param name="environmentVariable">Environment variable text</param>
    /// <param name="name">Variable name</param>
    /// <param name="value">Parsed value</param>
    /// <returns>True when the variable was found</returns>
    private static bool TryParseEnvironmentVariable(string? environmentVariable, string name, out string value)
    {
        value = string.Empty;

        if (string.IsNullOrWhiteSpace(environmentVariable))
        {
            return false;
        }

        var separatorIndex = environmentVariable.IndexOf('=');

        if (separatorIndex <= 0)
        {
            return false;
        }

        var variableName = environmentVariable[..separatorIndex].Trim();

        if (string.Equals(variableName, name, StringComparison.OrdinalIgnoreCase) == false)
        {
            return false;
        }

        value = environmentVariable[(separatorIndex + 1)..].Trim();

        return string.IsNullOrWhiteSpace(value) == false;
    }

    /// <summary>
    /// Extract a runtime version from a history string
    /// </summary>
    /// <param name="value">History text</param>
    /// <param name="variableName">Variable name</param>
    /// <param name="version">Extracted version string</param>
    /// <returns>True when a version was found</returns>
    private static bool TryExtractRuntimeVersion(string? value, string variableName, out string version)
    {
        version = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var pattern = $@"{Regex.Escape(variableName)}[=\s:]+(?<version>\d+\.\d+\.\d+)";
        var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (match.Success == false)
        {
            return false;
        }

        version = match.Groups["version"].Value;

        return string.IsNullOrWhiteSpace(version) == false;
    }

    /// <summary>
    /// Parse a .NET runtime version
    /// </summary>
    /// <param name="rawVersion">Raw version text</param>
    /// <param name="version">Parsed version</param>
    /// <returns>True when parsing succeeded</returns>
    private static bool TryParseRuntimeVersion(string rawVersion, out Version? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return false;
        }

        var match = RuntimeVersionRegex().Match(rawVersion);

        if (match.Success == false)
        {
            return false;
        }

        var versionText = match.Value;

        if (Version.TryParse(versionText, out var parsedVersion) == false || parsedVersion is null)
        {
            return false;
        }

        version = new Version(parsedVersion.Major, parsedVersion.Minor, parsedVersion.Build);

        return true;
    }

    /// <summary>
    /// Regex for runtime versions
    /// </summary>
    /// <returns>Compiled regex</returns>
    [GeneratedRegex(@"\d+\.\d+\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeVersionRegex();

    /// <summary>
    /// Regex for explicit .NET image references in history text
    /// </summary>
    /// <returns>Compiled regex</returns>
    [GeneratedRegex(@"mcr\.microsoft\.com/dotnet/(?:aspnet|runtime(?:-deps)?|sdk):(?<version>\d+\.\d+\.\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HistoryRepositoryVersionRegex();

    #endregion // Methods

    #region Helper types

    /// <summary>
    /// Internal runtime candidate
    /// </summary>
    private sealed class RuntimeCandidate
    {
        #region Properties

        /// <summary>
        /// Runtime kind
        /// </summary>
        public DerivedBaseRuntimeKind Kind { get; set; }

        /// <summary>
        /// Runtime version
        /// </summary>
        public Version? Version { get; set; }

        /// <summary>
        /// Channel version
        /// </summary>
        public string? ChannelVersion { get; set; }

        /// <summary>
        /// Detection source
        /// </summary>
        public DerivedBaseRuntimeDetectionSource Source { get; set; }

        /// <summary>
        /// Source priority
        /// </summary>
        public int SourcePriority { get; set; }

        /// <summary>
        /// Encounter order
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Whether the candidate has an exact version
        /// </summary>
        public bool HasExactVersion { get; set; }

        /// <summary>
        /// Evidence string
        /// </summary>
        public string? Evidence { get; set; }

        #endregion // Properties
    }

    #endregion // Helper types
}