namespace DockerUpdateGuard.Data.Entities;

/// <summary>
/// Relationship type between two image versions, stored as its integer number;
/// number 2 (build stage) is retired and never reused
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
    BaseImage = 1
}