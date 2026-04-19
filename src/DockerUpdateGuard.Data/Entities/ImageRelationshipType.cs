namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Relationship type between two image versions
/// </summary>
public enum ImageRelationshipType
{
    /// <summary>
    /// No relationship type set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Base image relationship
    /// </summary>
    BaseImage = 1,

    /// <summary>
    /// Build stage relationship
    /// </summary>
    BuildStage = 2
}