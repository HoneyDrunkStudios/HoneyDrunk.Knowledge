using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using static HoneyDrunk.Knowledge.Providers.Contract.TestSources;

namespace HoneyDrunk.Knowledge.Providers.Data.Tests;

/// <summary>The shape of the two tables, read from the model, and the vector encoding.</summary>
public sealed class MappingTests : IDisposable
{
    private static readonly DateTimeOffset Created = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabase database = new();

    /// <summary>There are exactly two tables, singular, in the knowledge schema, each with its named key.</summary>
    [Fact]
    public void Model_maps_two_singular_tables_in_the_knowledge_schema()
    {
        using var context = this.database.CreateContext();

        var tables = Design(context).GetEntityTypes().Select(type => $"{type.GetSchema()}.{type.GetTableName()}").Order(StringComparer.Ordinal);
        var keys = Design(context).GetEntityTypes()
            .Select(type => type.FindPrimaryKey()!)
            .Select(key => $"{key.GetName()} ({string.Join(",", key.Properties.Select(property => property.Name))})")
            .Order(StringComparer.Ordinal);

        Assert.Equal(["knowledge.Chunk", "knowledge.SourceVersion"], tables);
        Assert.Equal(["PK_Chunk (SourceVersionId,Position)", "PK_SourceVersion (Id)"], keys);
    }

    /// <summary>Every column has the pinned type, length, width, character set and nullability.</summary>
    [Fact]
    public void Columns_have_the_pinned_lengths()
    {
        using var context = this.database.CreateContext();

        var columns = Design(context).GetEntityTypes()
            .SelectMany(type => type.GetProperties().Select(property => Describe(type, property)))
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "Chunk.CreatedAt DateTimeOffset length= fixed=False unicode= null=False",
                "Chunk.Position Int32 length= fixed=False unicode= null=False",
                "Chunk.SourceVersionId String length=32 fixed=True unicode=False null=False",
                "Chunk.Text String length=1200 fixed=False unicode=True null=False",
                "Chunk.Vector Byte[] length= fixed=False unicode= null=False",
                "SourceVersion.ChunkCount Int32 length= fixed=False unicode= null=False",
                "SourceVersion.CreatedAt DateTimeOffset length= fixed=False unicode= null=False",
                "SourceVersion.Dimensions Int32 length= fixed=False unicode= null=False",
                "SourceVersion.EmbeddingModelId String length=200 fixed=False unicode=True null=False",
                "SourceVersion.Id String length=32 fixed=True unicode=False null=False",
                "SourceVersion.Origin String length=2048 fixed=False unicode=True null=False",
                "SourceVersion.SourceId String length=128 fixed=False unicode=False null=False",
                "SourceVersion.SupersededAt DateTimeOffset length= fixed=False unicode= null=True",
                "SourceVersion.Version String length=128 fixed=False unicode=False null=False",
            ],
            columns);
    }

    /// <summary>
    /// A unique index filtered to rows not superseded allows one current version per source, and a plain index
    /// on the source id serves deletes. Superseded rows of the same source sit beside the current one.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task One_current_version_per_source_is_a_filtered_unique_index()
    {
        await using (var context = this.database.CreateContext())
        {
            var indexes = Entity(context, "SourceVersion").GetIndexes()
                .Select(index => $"{index.GetDatabaseName()} ({string.Join(",", index.Properties.Select(property => property.Name))}) unique={index.IsUnique} filter={index.GetFilter()}")
                .Order(StringComparer.Ordinal);

            Assert.Equal(
                [
                    "IX_SourceVersion_SourceId (SourceId) unique=False filter=",
                    "UQ_SourceVersion_SourceId_Current (SourceId) unique=True filter=[SupersededAt] IS NULL",
                ],
                indexes);
            Assert.Empty(Entity(context, "Chunk").GetIndexes());
        }

        await using (var write = this.database.CreateContext())
        {
            write.SourceVersions.Add(Version("00000000000000000000000000000001", "docs:intro", supersededAt: Created));
            write.SourceVersions.Add(Version("00000000000000000000000000000002", "docs:intro", supersededAt: Created));
            write.SourceVersions.Add(Version("00000000000000000000000000000003", "docs:intro", supersededAt: null));
            write.SourceVersions.Add(Version("00000000000000000000000000000004", "docs:other", supersededAt: null));
            await write.SaveChangesAsync();
        }

        await using var second = this.database.CreateContext();
        second.SourceVersions.Add(Version("00000000000000000000000000000005", "docs:intro", supersededAt: null));
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    /// <summary>A version's chunks are deleted with it by the database, through the named foreign key.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Chunks_are_deleted_with_their_version()
    {
        await using (var context = this.database.CreateContext())
        {
            var key = Assert.Single(Entity(context, "Chunk").GetForeignKeys());

            Assert.Equal("FK_Chunk_SourceVersion", key.GetConstraintName());
            Assert.Equal(["SourceVersionId"], key.Properties.Select(property => property.Name));
            Assert.Equal("SourceVersion", key.PrincipalEntityType.GetTableName());
            Assert.Equal(DeleteBehavior.Cascade, key.DeleteBehavior);
            Assert.Empty(Entity(context, "SourceVersion").GetForeignKeys());
        }

        await using (var write = this.database.CreateContext())
        {
            var kept = Version("00000000000000000000000000000001", "docs:kept", supersededAt: null);
            var gone = Version("00000000000000000000000000000002", "docs:gone", supersededAt: null);
            kept.Chunks.Add(ChunkRow(kept.Id, 0));
            gone.Chunks.Add(ChunkRow(gone.Id, 0));
            gone.Chunks.Add(ChunkRow(gone.Id, 1));
            write.SourceVersions.AddRange(kept, gone);
            await write.SaveChangesAsync();
        }

        await using var delete = this.database.CreateContext();
        await delete.SourceVersions.Where(row => row.SourceId == "docs:gone").ExecuteDeleteAsync();

        Assert.Equal(["00000000000000000000000000000001"], await delete.Chunks.Select(row => row.SourceVersionId).ToListAsync());
    }

    /// <summary>
    /// Vectors are packed little-endian IEEE 754 singles and come back with every bit, negative zero and subnormals
    /// included, through the codec and through the database; a read rebuilds the embedding with the version's model.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Vectors_round_trip_bit_for_bit()
    {
        float[] values =
        [
            -0f,
            0f,
            float.Epsilon,
            -float.Epsilon,
            BitConverter.Int32BitsToSingle(0x007F_FFFF),
            BitConverter.Int32BitsToSingle(unchecked((int)0x8000_1234)),
            1e-40f,
            1f,
            -1.5f,
            float.MaxValue,
            float.MinValue,
            3.14159274f,
        ];

        var bytes = VectorCodec.Encode(values);

        Assert.Equal(values.Length * sizeof(float), bytes.Length);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x80 }, bytes[..4]);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x80, 0x3F }, bytes[28..32]);
        Assert.Equal(Bits(values), Bits(VectorCodec.Decode(bytes)));
        Assert.Throws<InvalidOperationException>(() => VectorCodec.Decode(new byte[6]));
        Assert.Throws<InvalidOperationException>(() => VectorCodec.Decode([]));

        await this.database.CreateStore().StoreVersionAsync(Source(), [Chunk(0, "bits", values)]);

        await using var context = this.database.CreateContext();
        var version = await context.SourceVersions.AsNoTracking().SingleAsync();
        var row = await context.Chunks.AsNoTracking().SingleAsync();
        var rebuilt = row.ToChunk(version.EmbeddingModelId);
        Assert.Equal(bytes, row.Vector);
        Assert.Equal(values.Length, version.Dimensions);
        Assert.Equal(Bits(values), Bits(rebuilt.Embedding.Vector));
        Assert.Equal(0, rebuilt.Position);
        Assert.Equal("bits", rebuilt.Embedding.Value);
        Assert.Equal(ModelA, rebuilt.Embedding.ModelId);
    }

    /// <summary>A version row records the source, its model, its size and its ingest instant, in UTC.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_stored_version_row_records_its_lineage()
    {
        var at = new DateTimeOffset(2026, 11, 15, 4, 0, 0, TimeSpan.FromHours(-5)).AddTicks(1_234_567);
        var store = this.database.CreateStore();
        await store.StoreVersionAsync(new("docs:intro", "https://example.test/guide", "v3", at, ModelA), Chunks("a", ModelA, [1f, 0f, 0f], [0f, 1f, 0f]));

        await using var context = this.database.CreateContext();
        var version = await context.SourceVersions.AsNoTracking().SingleAsync();
        var chunks = await context.Chunks.AsNoTracking().OrderBy(row => row.Position).ToListAsync();

        Assert.Equal(Guid.CreateVersion7(at).ToString("N")[..12], version.Id[..12]);
        Assert.Matches("^[0-9a-f]{32}$", version.Id);
        Assert.Equal(
            "docs:intro|https://example.test/guide|v3|model-a|3|2",
            $"{version.SourceId}|{version.Origin}|{version.Version}|{version.EmbeddingModelId}|{version.Dimensions}|{version.ChunkCount}");
        Assert.Equal(at.UtcTicks, version.CreatedAt.UtcTicks);
        Assert.Equal(TimeSpan.Zero, version.CreatedAt.Offset);
        Assert.Null(version.SupersededAt);
        Assert.Equal([0, 1], chunks.Select(row => row.Position));
        Assert.All(chunks, row =>
        {
            Assert.Equal(version.Id, row.SourceVersionId);
            Assert.Equal(at.UtcTicks, row.CreatedAt.UtcTicks);
        });
    }

    /// <inheritdoc />
    public void Dispose() => this.database.Dispose();

    private static int[] Bits(IEnumerable<float> values) => [.. values.Select(BitConverter.SingleToInt32Bits)];

    private static KnowledgeSourceVersionEntity Version(string id, string sourceId, DateTimeOffset? supersededAt) => new()
    {
        Id = id,
        SourceId = sourceId,
        Origin = "https://example.test/guide",
        Version = "v1",
        EmbeddingModelId = ModelA,
        Dimensions = 2,
        ChunkCount = 1,
        CreatedAt = Created,
        SupersededAt = supersededAt,
    };

    private static KnowledgeChunkEntity ChunkRow(string versionId, int position) => new()
    {
        SourceVersionId = versionId,
        Position = position,
        Text = "passage",
        Vector = VectorCodec.Encode([1f, 0f]),
        CreatedAt = Created,
    };

    private static IModel Design(KnowledgeDbContext context) => context.GetService<IDesignTimeModel>().Model;

    private static IEntityType Entity(KnowledgeDbContext context, string table) =>
        Design(context).GetEntityTypes().Single(type => type.GetTableName() == table);

    private static string Describe(IEntityType type, IProperty property) =>
        $"{type.GetTableName()}.{property.GetColumnName()} {(Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType).Name} length={property.GetMaxLength()} fixed={property.IsFixedLength() == true} unicode={property.IsUnicode()} null={property.IsNullable}";
}
