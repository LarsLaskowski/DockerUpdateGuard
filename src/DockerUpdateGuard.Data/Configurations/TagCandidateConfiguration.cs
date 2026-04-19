using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DockerUpdateGuard.Data.Configurations;

/// <summary>
/// Mapping for <see cref="TagCandidate"/>
/// </summary>
public class TagCandidateConfiguration : IEntityTypeConfiguration<TagCandidate>
{
    #region Methods

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<TagCandidate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var digestConverter = new ValueConverter<string?, string>(value => value ?? string.Empty, value => string.IsNullOrEmpty(value) ? null : value);

        builder.ToTable("TagCandidates");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Tag)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(entity => entity.Digest)
               .HasMaxLength(255)
               .HasConversion(digestConverter)
               .IsRequired();

        builder.Property(entity => entity.Reason)
               .HasMaxLength(500);

        builder.HasIndex(entity => new
                                   {
                                       entity.UpdateFindingId,
                                       entity.Tag,
                                       entity.Digest,
                                   })
               .IsUnique();

        builder.HasOne(entity => entity.UpdateFinding)
               .WithMany(entity => entity.TagCandidates)
               .HasForeignKey(entity => entity.UpdateFindingId)
               .OnDelete(DeleteBehavior.Cascade);
    }

    #endregion // Methods
}