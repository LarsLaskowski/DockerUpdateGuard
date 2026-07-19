using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DockerUpdateGuard.Data.Configurations;

/// <summary>
/// Mapping for <see cref="ScanRun"/>
/// </summary>
public class ScanRunConfiguration : IEntityTypeConfiguration<ScanRun>
{
    #region IEntityTypeConfiguration

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ScanRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ScanRuns");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Type)
               .IsRequired();

        builder.Property(entity => entity.Status)
               .IsRequired();

        builder.Property(entity => entity.TriggerSource)
               .IsRequired();

        builder.Property(entity => entity.CorrelationId)
               .HasMaxLength(100);

        builder.Property(entity => entity.ErrorMessage)
               .HasColumnType("text");

        builder.Property(entity => entity.DiagnosticJson)
               .HasMaxLength(4000);

        builder.Property(entity => entity.StartedAtUtc)
               .IsRequired();

        builder.HasIndex(entity => new
                                   {
                                       entity.Type,
                                       entity.Status,
                                       entity.StartedAtUtc,
                                   });

        builder.HasOne(entity => entity.ObservedImage)
               .WithMany(entity => entity.ScanRuns)
               .HasForeignKey(entity => entity.ObservedImageId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(entity => entity.DockerInstance)
               .WithMany(entity => entity.ScanRuns)
               .HasForeignKey(entity => entity.DockerInstanceId)
               .OnDelete(DeleteBehavior.SetNull);
    }

    #endregion // IEntityTypeConfiguration
}