using HoneyDrunk.Knowledge.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HoneyDrunk.Knowledge.Providers.Data;

/// <summary>
/// Maps <see cref="KnowledgeChunkEntity"/> to <c>knowledge.Chunk</c>. The primary key serves the foreign key
/// and every chunk read, so the table has no other index.
/// </summary>
internal sealed class KnowledgeChunkEntityConfiguration : IEntityTypeConfiguration<KnowledgeChunkEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<KnowledgeChunkEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Chunk");
        builder.HasKey(row => new { row.SourceVersionId, row.Position }).HasName("PK_Chunk");
        builder.Property(row => row.SourceVersionId).HasMaxLength(KnowledgeSourceVersionEntityConfiguration.IdLength).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(row => row.Position).IsRequired();
        builder.Property(row => row.Text).HasMaxLength(KnowledgeLimits.MaxChunkTextLength).IsUnicode().IsRequired();
        builder.Property(row => row.Vector).IsRequired();
        builder.Property(row => row.CreatedAt).IsRequired();
    }
}
