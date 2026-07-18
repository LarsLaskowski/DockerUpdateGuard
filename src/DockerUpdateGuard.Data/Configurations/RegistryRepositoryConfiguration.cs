using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DockerUpdateGuard.Data.Configurations;

/// <summary>
/// Mapping for <see cref="RegistryRepository"/>
/// </summary>
public class RegistryRepositoryConfiguration : IEntityTypeConfiguration<RegistryRepository>
{
    #region IEntityTypeConfiguration

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<RegistryRepository> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RegistryRepositories");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Registry)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(entity => entity.Repository)
               .HasMaxLength(300)
               .IsRequired();

        builder.Property(entity => entity.CreatedAtUtc)
               .IsRequired();

        builder.Property(entity => entity.UpdatedAtUtc)
               .IsRequired();

        builder.HasIndex(entity => new
                                   {
                                       entity.Registry,
                                       entity.Repository,
                                   })
               .IsUnique();
    }

    #endregion // IEntityTypeConfiguration
}