using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DockerUpdateGuard.Data.Configurations;

/// <summary>
/// Mapping for <see cref="UpdateFinding"/>
/// </summary>
public class UpdateFindingConfiguration : IEntityTypeConfiguration<UpdateFinding>
{
    #region IEntityTypeConfiguration

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<UpdateFinding> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UpdateFindings");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Type)
               .IsRequired();

        builder.Property(entity => entity.Summary)
               .HasMaxLength(500)
               .IsRequired();

        builder.Property(entity => entity.Details)
               .HasMaxLength(2000);

        builder.Property(entity => entity.DetectedAtUtc)
               .IsRequired();

        builder.HasIndex(entity => new
                                   {
                                       entity.ObservedImageId,
                                       entity.IsActive,
                                   });

        builder.HasIndex(entity => new
                                   {
                                       entity.ContainerSnapshotId,
                                       entity.IsActive,
                                   });

        builder.HasIndex(entity => new
                                   {
                                       entity.SubjectImageVersionId,
                                       entity.IsActive,
                                   });

        builder.HasOne(entity => entity.ScanRun)
               .WithMany(entity => entity.UpdateFindings)
               .HasForeignKey(entity => entity.ScanRunId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(entity => entity.SubjectImageVersion)
               .WithMany(entity => entity.SubjectUpdateFindings)
               .HasForeignKey(entity => entity.SubjectImageVersionId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(entity => entity.RecommendedImageVersion)
               .WithMany(entity => entity.RecommendedByUpdateFindings)
               .HasForeignKey(entity => entity.RecommendedImageVersionId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(entity => entity.ObservedImage)
               .WithMany(entity => entity.UpdateFindings)
               .HasForeignKey(entity => entity.ObservedImageId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(entity => entity.ContainerSnapshot)
               .WithMany(entity => entity.UpdateFindings)
               .HasForeignKey(entity => entity.ContainerSnapshotId)
               .OnDelete(DeleteBehavior.SetNull);
    }

    #endregion // IEntityTypeConfiguration
}