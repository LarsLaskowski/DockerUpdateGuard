using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DockerUpdateGuard.Data.Configurations;

/// <summary>
/// Mapping for <see cref="ObservedImage"/>
/// </summary>
public class ObservedImageConfiguration : IEntityTypeConfiguration<ObservedImage>
{
    #region IEntityTypeConfiguration

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ObservedImage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ObservedImages");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Name)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(entity => entity.Description)
               .HasColumnType("text");

        builder.Property(entity => entity.Source)
               .IsRequired();

        builder.Property(entity => entity.CreatedAtUtc)
               .IsRequired();

        builder.Property(entity => entity.UpdatedAtUtc)
               .IsRequired();

        builder.HasIndex(entity => entity.CurrentImageVersionId);

        builder.HasOne(entity => entity.CurrentImageVersion)
               .WithMany(entity => entity.ObservedImages)
               .HasForeignKey(entity => entity.CurrentImageVersionId)
               .OnDelete(DeleteBehavior.Restrict);
    }

    #endregion // IEntityTypeConfiguration
}