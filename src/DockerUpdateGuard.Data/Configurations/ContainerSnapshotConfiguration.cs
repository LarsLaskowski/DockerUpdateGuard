using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DockerUpdateGuard.Data.Configurations;

/// <summary>
/// Mapping for <see cref="ContainerSnapshot"/>
/// </summary>
public class ContainerSnapshotConfiguration : IEntityTypeConfiguration<ContainerSnapshot>
{
    #region Methods

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ContainerSnapshot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ContainerSnapshots");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.ContainerId)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(entity => entity.Name)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(entity => entity.ComposeProject)
               .HasMaxLength(200);

        builder.Property(entity => entity.StackName)
               .HasMaxLength(200);

        builder.Property(entity => entity.ServiceName)
               .HasMaxLength(200);

        builder.Property(entity => entity.Status)
               .IsRequired();

        builder.Property(entity => entity.RecordedAtUtc)
               .IsRequired();

        builder.HasIndex(entity => new
                                   {
                                       entity.DockerInstanceId,
                                       entity.ContainerId,
                                       entity.RecordedAtUtc,
                                   });

        builder.HasOne(entity => entity.DockerInstance)
               .WithMany(entity => entity.ContainerSnapshots)
               .HasForeignKey(entity => entity.DockerInstanceId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(entity => entity.ImageVersion)
               .WithMany(entity => entity.ContainerSnapshots)
               .HasForeignKey(entity => entity.ImageVersionId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(entity => entity.ScanRun)
               .WithMany(entity => entity.ContainerSnapshots)
               .HasForeignKey(entity => entity.ScanRunId)
               .OnDelete(DeleteBehavior.SetNull);
    }

    #endregion // Methods
}