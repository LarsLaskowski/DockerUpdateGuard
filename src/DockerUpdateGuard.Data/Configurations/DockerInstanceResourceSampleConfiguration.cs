using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DockerUpdateGuard.Data.Configurations;

/// <summary>
/// Mapping for <see cref="DockerInstanceResourceSample"/>
/// </summary>
public class DockerInstanceResourceSampleConfiguration : IEntityTypeConfiguration<DockerInstanceResourceSample>
{
    #region Methods

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<DockerInstanceResourceSample> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("DockerInstanceResourceSamples");

        builder.HasKey(entity => entity.Id);

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
                                       entity.RecordedAtUtc,
                                   });

        builder.HasOne(entity => entity.DockerInstance)
               .WithMany()
               .HasForeignKey(entity => entity.DockerInstanceId)
               .OnDelete(DeleteBehavior.Cascade);
    }

    #endregion // Methods
}