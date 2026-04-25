using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DockerUpdateGuard.Data.Configurations;

/// <summary>
/// Mapping for <see cref="RuntimeContainerTagSelection"/>
/// </summary>
public class RuntimeContainerTagSelectionConfiguration : IEntityTypeConfiguration<RuntimeContainerTagSelection>
{
    #region Methods

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<RuntimeContainerTagSelection> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var digestConverter = new ValueConverter<string?, string>(value => value ?? string.Empty, value => string.IsNullOrEmpty(value) ? null : value);

        builder.ToTable("RuntimeContainerTagSelections");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.ContainerId)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(entity => entity.Tag)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(entity => entity.Digest)
               .HasMaxLength(255)
               .HasConversion(digestConverter)
               .IsRequired();

        builder.Property(entity => entity.SelectedAtUtc)
               .IsRequired();

        builder.HasIndex(entity => new
                                   {
                                       entity.DockerInstanceId,
                                       entity.ContainerId,
                                   })
               .IsUnique();

        builder.HasOne(entity => entity.DockerInstance)
               .WithMany()
               .HasForeignKey(entity => entity.DockerInstanceId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(entity => entity.RegistryRepository)
               .WithMany()
               .HasForeignKey(entity => entity.RegistryRepositoryId)
               .OnDelete(DeleteBehavior.Cascade);
    }

    #endregion // Methods
}