using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DockerUpdateGuard.Data.Configurations;

/// <summary>
/// Mapping for <see cref="PortainerEndpoint"/>
/// </summary>
public class PortainerEndpointConfiguration : IEntityTypeConfiguration<PortainerEndpoint>
{
    #region IEntityTypeConfiguration

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<PortainerEndpoint> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PortainerEndpoints");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Name)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(entity => entity.BaseUrl)
               .HasMaxLength(512)
               .IsRequired();

        builder.Property(entity => entity.ExternalEndpointId)
               .HasMaxLength(100);

        builder.Property(entity => entity.CreatedAtUtc)
               .IsRequired();

        builder.Property(entity => entity.UpdatedAtUtc)
               .IsRequired();

        builder.HasIndex(entity => entity.DockerInstanceId)
               .IsUnique();

        builder.HasOne(entity => entity.DockerInstance)
               .WithOne(entity => entity.PortainerEndpoint)
               .HasForeignKey<PortainerEndpoint>(entity => entity.DockerInstanceId)
               .OnDelete(DeleteBehavior.Cascade);
    }

    #endregion // IEntityTypeConfiguration
}