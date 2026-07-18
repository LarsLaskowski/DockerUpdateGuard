using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DockerUpdateGuard.Data.Configurations;

/// <summary>
/// Mapping for <see cref="ImageVersion"/>
/// </summary>
public class ImageVersionConfiguration : IEntityTypeConfiguration<ImageVersion>
{
    #region IEntityTypeConfiguration

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ImageVersion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var digestConverter = new ValueConverter<string?, string>(value => value ?? string.Empty, value => string.IsNullOrEmpty(value) ? null : value);

        builder.ToTable("ImageVersions");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Tag)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(entity => entity.Digest)
               .HasMaxLength(255)
               .HasConversion(digestConverter)
               .IsRequired();

        builder.Property(entity => entity.Source)
               .IsRequired();

        builder.Property(entity => entity.MetadataJson)
               .HasMaxLength(4000);

        builder.Property(entity => entity.VulnerabilityAssessmentStatus)
               .IsRequired();

        builder.Property(entity => entity.VulnerabilityAssessmentSource)
               .IsRequired();

        builder.Property(entity => entity.VulnerabilityAssessmentMessage)
               .HasMaxLength(2000);

        builder.Property(entity => entity.CreatedAtUtc)
               .IsRequired();

        builder.Property(entity => entity.UpdatedAtUtc)
               .IsRequired();

        builder.HasIndex(entity => new
                                   {
                                       entity.RegistryRepositoryId,
                                       entity.Tag,
                                       entity.Digest,
                                   })
               .IsUnique();

        builder.HasOne(entity => entity.RegistryRepository)
               .WithMany(entity => entity.ImageVersions)
               .HasForeignKey(entity => entity.RegistryRepositoryId)
               .OnDelete(DeleteBehavior.Cascade);
    }

    #endregion // IEntityTypeConfiguration
}