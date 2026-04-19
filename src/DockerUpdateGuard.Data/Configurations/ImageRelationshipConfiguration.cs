using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DockerUpdateGuard.Data.Configurations;

/// <summary>
/// Mapping for <see cref="ImageRelationship"/>
/// </summary>
public class ImageRelationshipConfiguration : IEntityTypeConfiguration<ImageRelationship>
{
    #region Methods

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ImageRelationship> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ImageRelationships");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.RelationshipType)
               .IsRequired();

        builder.Property(entity => entity.SourceReference)
               .HasMaxLength(500);

        builder.Property(entity => entity.CreatedAtUtc)
               .IsRequired();

        builder.HasIndex(entity => entity.ChildImageVersionId);
        builder.HasIndex(entity => entity.BaseImageVersionId);

        builder.HasIndex(entity => new
                                   {
                                       entity.ChildImageVersionId,
                                       entity.BaseImageVersionId,
                                       entity.Depth,
                                       entity.RelationshipType,
                                   })
               .IsUnique();

        builder.ToTable(tableBuilder =>
                        {
                            tableBuilder.HasCheckConstraint("CK_ImageRelationships_ChildAndBaseDifferent", "\"ChildImageVersionId\" <> \"BaseImageVersionId\"");
                            tableBuilder.HasCheckConstraint("CK_ImageRelationships_DepthGreaterThanZero", "\"Depth\" > 0");
                        });

        builder.HasOne(entity => entity.ChildImageVersion)
               .WithMany(entity => entity.ChildRelationships)
               .HasForeignKey(entity => entity.ChildImageVersionId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(entity => entity.BaseImageVersion)
               .WithMany(entity => entity.BaseRelationships)
               .HasForeignKey(entity => entity.BaseImageVersionId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(entity => entity.ScanRun)
               .WithMany(entity => entity.ImageRelationships)
               .HasForeignKey(entity => entity.ScanRunId)
               .OnDelete(DeleteBehavior.SetNull);
    }

    #endregion // Methods
}