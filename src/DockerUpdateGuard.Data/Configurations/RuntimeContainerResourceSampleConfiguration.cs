using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DockerUpdateGuard.Data.Configurations;

/// <summary>
/// Mapping for <see cref="RuntimeContainerResourceSample"/>
/// </summary>
public class RuntimeContainerResourceSampleConfiguration : IEntityTypeConfiguration<RuntimeContainerResourceSample>
{
    #region IEntityTypeConfiguration

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<RuntimeContainerResourceSample> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RuntimeContainerResourceSamples");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.ContainerId)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(entity => entity.ContainerName)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(entity => entity.CpuPercent)
               .HasPrecision(10, 4)
               .IsRequired();

        builder.Property(entity => entity.NetworkRxBytesPerSecond)
               .HasPrecision(20, 4)
               .IsRequired();

        builder.Property(entity => entity.NetworkTxBytesPerSecond)
               .HasPrecision(20, 4)
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
               .WithMany()
               .HasForeignKey(entity => entity.DockerInstanceId)
               .OnDelete(DeleteBehavior.Cascade);
    }

    #endregion // IEntityTypeConfiguration
}