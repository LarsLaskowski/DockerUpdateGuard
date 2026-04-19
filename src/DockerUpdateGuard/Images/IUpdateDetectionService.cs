using DockerUpdateGuard.DockerHub;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Update detection contract
/// </summary>
public interface IUpdateDetectionService
{
    #region Methods

    /// <summary>
    /// Evaluate update state for a concrete image reference
    /// </summary>
    /// <param name="currentImage">Current image reference</param>
    /// <param name="availableTags">Available tags</param>
    /// <returns>Evaluation result</returns>
    UpdateEvaluationResult Evaluate(ImageReference currentImage, IReadOnlyList<DockerHubTagData> availableTags);

    #endregion // Methods
}