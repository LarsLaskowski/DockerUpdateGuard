using DockerUpdateGuard.Docker;

namespace DockerUpdateGuard.Images.Interfaces;

/// <summary>
/// Detects derived base runtimes from local Docker image metadata
/// </summary>
public interface IDerivedBaseRuntimeDetector
{
    #region Methods

    /// <summary>
    /// Detect a derived base runtime from local Docker image metadata
    /// </summary>
    /// <param name="inspectData">Image inspect data</param>
    /// <param name="historyEntries">Image history entries</param>
    /// <returns>Derived base runtime descriptor or null</returns>
    DerivedBaseRuntimeDescriptor? Detect(DockerImageInspectData? inspectData, IReadOnlyList<DockerImageHistoryEntryData>? historyEntries);

    #endregion // Methods
}