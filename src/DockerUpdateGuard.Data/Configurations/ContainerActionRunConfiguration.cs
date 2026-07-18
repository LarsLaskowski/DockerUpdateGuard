using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DockerUpdateGuard.Data.Configurations;

/// <summary>
/// Mapping for <see cref="ContainerActionRun"/>
/// </summary>
public class ContainerActionRunConfiguration : IEntityTypeConfiguration<ContainerActionRun>
{
    #region IEntityTypeConfiguration

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ContainerActionRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ContainerActionRuns");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.ActionType)
               .IsRequired();

        builder.Property(entity => entity.ResourceType)
               .IsRequired();

        builder.Property(entity => entity.Status)
               .IsRequired();

        builder.Property(entity => entity.ResourceName)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(entity => entity.RequestedBy)
               .HasMaxLength(100);

        builder.Property(entity => entity.PortainerTaskId)
               .HasMaxLength(100);

        builder.Property(entity => entity.ErrorMessage)
               .HasMaxLength(1000);

        builder.Property(entity => entity.RequestedAtUtc)
               .IsRequired();

        builder.HasIndex(entity => new
                                   {
                                       entity.DockerInstanceId,
                                       entity.RequestedAtUtc,
                                   });

        builder.HasIndex(entity => new
                                   {
                                       entity.PortainerEndpointId,
                                       entity.RequestedAtUtc,
                                   });

        builder.HasOne(entity => entity.DockerInstance)
               .WithMany(entity => entity.ContainerActionRuns)
               .HasForeignKey(entity => entity.DockerInstanceId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(entity => entity.PortainerEndpoint)
               .WithMany(entity => entity.ContainerActionRuns)
               .HasForeignKey(entity => entity.PortainerEndpointId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(entity => entity.ContainerSnapshot)
               .WithMany(entity => entity.ContainerActionRuns)
               .HasForeignKey(entity => entity.ContainerSnapshotId)
               .OnDelete(DeleteBehavior.SetNull);
    }

    #endregion // IEntityTypeConfiguration
}