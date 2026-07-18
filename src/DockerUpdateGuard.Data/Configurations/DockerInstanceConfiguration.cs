using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DockerUpdateGuard.Data.Configurations;

/// <summary>
/// Mapping for <see cref="DockerInstance"/>
/// </summary>
public class DockerInstanceConfiguration : IEntityTypeConfiguration<DockerInstance>
{
    #region IEntityTypeConfiguration

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<DockerInstance> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("DockerInstances");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Name)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(entity => entity.EndpointUri)
               .HasMaxLength(512)
               .IsRequired();

        builder.Property(entity => entity.ConnectionKind)
               .IsRequired();

        builder.Property(entity => entity.Source)
               .IsRequired();

        builder.Property(entity => entity.CreatedAtUtc)
               .IsRequired();

        builder.Property(entity => entity.UpdatedAtUtc)
               .IsRequired();

        builder.HasIndex(entity => entity.Name)
               .IsUnique();
    }

    #endregion // IEntityTypeConfiguration
}