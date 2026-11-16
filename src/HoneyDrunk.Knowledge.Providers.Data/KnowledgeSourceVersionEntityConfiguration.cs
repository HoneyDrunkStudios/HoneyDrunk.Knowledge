using HoneyDrunk.Knowledge.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HoneyDrunk.Knowledge.Providers.Data;

/// <summary>
/// Maps <see cref="KnowledgeSourceVersionEntity"/> to <c>knowledge.SourceVersion</c>, with provider-neutral calls
/// only. The collations and checks are in the shipped SQL Server scripts.
/// </summary>
internal sealed class KnowledgeSourceVersionEntityConfiguration : IEntityTypeConfiguration<KnowledgeSourceVersionEntity>
{
    /// <summary>The length of a version id: a GUID as 32 hexadecimal digits.</summary>
    internal const int IdLength = 32;

    /// <summary>The filter of the index that allows one current version per source, valid on SQL Server and SQLite.</summary>
    internal const string CurrentFilter = "[SupersededAt] IS NULL";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<KnowledgeSourceVersionEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("SourceVersion");
        builder.HasKey(row => row.Id).HasName("PK_SourceVersion");
        builder.Property(row => row.Id).HasMaxLength(IdLength).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(row => row.SourceId).HasMaxLength(KnowledgeLimits.MaxSourceIdLength).IsUnicode(false).IsRequired();
        builder.Property(row => row.Origin).HasMaxLength(KnowledgeLimits.MaxOriginLength).IsUnicode().IsRequired();
        builder.Property(row => row.Version).HasMaxLength(KnowledgeLimits.MaxVersionLength).IsUnicode(false).IsRequired();
        builder.Property(row => row.EmbeddingModelId).HasMaxLength(KnowledgeLimits.MaxEmbeddingModelIdLength).IsUnicode().IsRequired();
        builder.Property(row => row.Dimensions).IsRequired();
        builder.Property(row => row.ChunkCount).IsRequired();
        builder.Property(row => row.CreatedAt).IsRequired();
        builder.Property(row => row.SupersededAt);

        // One current version per source; serves get, search and the ingest check.
        builder.HasIndex(row => row.SourceId, "UQ_SourceVersion_SourceId_Current").IsUnique().HasFilter(CurrentFilter);

        // Serves deleting a source with every version.
        builder.HasIndex(row => row.SourceId, "IX_SourceVersion_SourceId");

        builder.HasMany(row => row.Chunks)
            .WithOne()
            .HasForeignKey(chunk => chunk.SourceVersionId)
            .HasConstraintName("FK_Chunk_SourceVersion")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
