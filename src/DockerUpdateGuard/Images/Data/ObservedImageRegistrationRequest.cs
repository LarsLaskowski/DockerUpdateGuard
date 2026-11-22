using System.ComponentModel.DataAnnotations;

namespace DockerUpdateGuard.Images.Data;

/// <summary>
/// Request to register or update an observed image
/// </summary>
public class ObservedImageRegistrationRequest : IValidatableObject
{
    #region Properties

    /// <summary>
    /// Display name
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Docker image reference
    /// </summary>
    [Required]
    public string ImageReference { get; set; } = string.Empty;

    #endregion // Properties

    #region IValidatableObject

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // IValidatableObject
}