using HoneyDrunk.Knowledge.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using static HoneyDrunk.Knowledge.Providers.Contract.TestSources;

namespace HoneyDrunk.Knowledge.Providers.Data.SqlServerTests;

/// <summary>The shipped table scripts, deployed to SQL Server, against the mapping and the constraints they promise.</summary>
public sealed class SqlServerSchemaTests : IDisposable
{
    private const string VersionId = "0123456789abcdef0123456789abcdef";
    private const string OtherId = "fedcba9876543210fedcba9876543210";
    private const string Created = "'2026-11-15T09:00:00+00:00'";
    private const string Earlier = "'2026-11-15T08:59:59.9999999+00:00'";
    private const string Vector = "0x0000803F00000000";

    private static readonly string[] BinaryColumns =
    [
        "Chunk.SourceVersionId",
        "SourceVersion.EmbeddingModelId",
        "SourceVersion.Id",
        "SourceVersion.SourceId",
        "SourceVersion.Version",
    ];

    private static readonly string[] PinnedNames =
    [
        "CK_Chunk_Position",
        "CK_Chunk_Text_Length",
        "CK_Chunk_Vector_Length",
        "CK_SourceVersion_ChunkCount",
        "CK_SourceVersion_Dimensions",
        "CK_SourceVersion_Id_Format",
        "CK_SourceVersion_Origin_Length",
        "CK_SourceVersion_SourceId_Format",
        "CK_SourceVersion_SupersededAt",
        "FK_Chunk_SourceVersion",
        "IX_SourceVersion_SourceId",
        "PK_Chunk",
        "PK_SourceVersion",
        "UQ_SourceVersion_SourceId_Current",
    ];

    private readonly SqlServerDatabase database = new();

    /// <summary>
    /// Every mapped column is published with the type, length and nullability the mapping expects, and no other
    /// column exists. Instants keep seven digits of fractional seconds; the compared text columns use the binary
    /// collation, and the free text uses the database's own.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Published_columns_match_the_mapping()
    {
        await using var context = this.database.CreateContext();
        var mapped = context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .SelectMany(type => type.GetProperties().Select(property =>
                $"{type.GetSchema()}.{type.GetTableName()}.{property.GetColumnName()} {property.GetColumnType()} null={property.IsNullable}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var published = await this.database.QueryAsync(
            """
            SELECT TABLE_SCHEMA + '.' + TABLE_NAME + '.' + COLUMN_NAME + ' ' + DATA_TYPE
                + CASE WHEN CHARACTER_MAXIMUM_LENGTH = -1 THEN '(max)' WHEN CHARACTER_MAXIMUM_LENGTH IS NOT NULL THEN '(' + CAST(CHARACTER_MAXIMUM_LENGTH AS varchar(10)) + ')' ELSE '' END
                + ' null=' + CASE IS_NULLABLE WHEN 'YES' THEN 'True' ELSE 'False' END AS Value
            FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'knowledge'
            """);
        var precisions = await this.database.QueryAsync(
            "SELECT TABLE_NAME + '.' + COLUMN_NAME + '=' + DATA_TYPE + '(' + CAST(DATETIME_PRECISION AS varchar(3)) + ')' AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'knowledge' AND DATETIME_PRECISION IS NOT NULL");
        var collations = await this.database.QueryAsync(
            "SELECT TABLE_NAME + '.' + COLUMN_NAME + '=' + COLLATION_NAME AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'knowledge' AND COLLATION_NAME IS NOT NULL");
        var databaseCollation = Assert.Single(await this.database.QueryAsync("SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128)) AS Value"));

        Assert.Equal(14, mapped.Length);
        Assert.Equal(mapped, published.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Chunk.CreatedAt=datetimeoffset(7)", "SourceVersion.CreatedAt=datetimeoffset(7)", "SourceVersion.SupersededAt=datetimeoffset(7)"],
            precisions.Order(StringComparer.Ordinal));
        Assert.Equal(
            [.. BinaryColumns.Select(column => column + "=Latin1_General_100_BIN2"), "Chunk.Text=" + databaseCollation, "SourceVersion.Origin=" + databaseCollation],
            collations.OrderBy(column => column.EndsWith("BIN2", StringComparison.Ordinal) ? 0 : 1).ThenBy(column => column, StringComparer.Ordinal));
        Assert.NotEqual("Latin1_General_100_BIN2", databaseCollation);
    }

    /// <summary>Both tables and all fourteen columns carry a description that says more than the name.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Every_table_and_column_has_a_description()
    {
        var tables = await this.database.QueryAsync(
            """
            SELECT t.name + '=' + ISNULL(CAST(p.value AS nvarchar(max)), '') AS Value
            FROM sys.tables t
            LEFT JOIN sys.extended_properties p ON p.class = 1 AND p.major_id = t.object_id AND p.minor_id = 0 AND p.name = 'MS_Description'
            WHERE SCHEMA_NAME(t.schema_id) = 'knowledge'
            """);
        var columns = await this.database.QueryAsync(
            """
            SELECT t.name + '.' + c.name + '=' + ISNULL(CAST(p.value AS nvarchar(max)), '') AS Value
            FROM sys.tables t
            JOIN sys.columns c ON c.object_id = t.object_id
            LEFT JOIN sys.extended_properties p ON p.class = 1 AND p.major_id = t.object_id AND p.minor_id = c.column_id AND p.name = 'MS_Description'
            WHERE SCHEMA_NAME(t.schema_id) = 'knowledge'
            """);

        Assert.Equal(2, tables.Count);
        Assert.Equal(14, columns.Count);
        Assert.All(tables, described =>
        {
            Assert.Contains("Classification: Confidential.", described, StringComparison.Ordinal);
            Assert.Contains("Growth:", described, StringComparison.Ordinal);
        });
        Assert.All(tables.Concat(columns), described =>
        {
            var parts = described.Split('=', 2);
            Assert.True(parts[1].Length >= 20, $"'{parts[0]}' has no real description.");
            Assert.NotEqual(parts[0], parts[1]);
        });
    }

    /// <summary>
    /// Every constraint and index carries its pinned name, no other exists, and the mapping uses the same names.
    /// The one-current-version index is unique and filtered, and the chunks cascade with their version.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Every_constraint_and_index_is_named_as_pinned()
    {
        var published = await this.database.QueryAsync(
            """
            SELECT name AS Value FROM sys.objects
            WHERE schema_id = SCHEMA_ID('knowledge') AND type IN ('PK', 'UQ', 'C', 'F', 'D')
            UNION
            SELECT i.name FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            WHERE t.schema_id = SCHEMA_ID('knowledge') AND i.name IS NOT NULL
            """);
        var current = await this.database.QueryAsync(
            "SELECT CASE is_unique WHEN 1 THEN 'unique ' ELSE '' END + ISNULL(filter_definition, 'no filter') AS Value FROM sys.indexes WHERE name = 'UQ_SourceVersion_SourceId_Current'");
        var keys = await this.database.QueryAsync(
            """
            SELECT i.name + ':' + STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS Value
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE t.schema_id = SCHEMA_ID('knowledge')
            GROUP BY i.name
            """);
        var cascade = await this.database.QueryAsync("SELECT delete_referential_action_desc AS Value FROM sys.foreign_keys WHERE name = 'FK_Chunk_SourceVersion'");
        await using var context = this.database.CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var mapped = model.GetEntityTypes().SelectMany(type =>
            type.GetKeys().Select(key => key.GetName()!)
                .Concat(type.GetIndexes().Select(index => index.GetDatabaseName()!))
                .Concat(type.GetForeignKeys().Select(key => key.GetConstraintName()!)));

        Assert.Equal(PinnedNames, published.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["FK_Chunk_SourceVersion", "IX_SourceVersion_SourceId", "PK_Chunk", "PK_SourceVersion", "UQ_SourceVersion_SourceId_Current"],
            mapped.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal(["unique ([SupersededAt] IS NULL)"], current);
        Assert.Equal(
            ["IX_SourceVersion_SourceId:SourceId", "PK_Chunk:SourceVersionId,Position", "PK_SourceVersion:Id", "UQ_SourceVersion_SourceId_Current:SourceId"],
            keys.Order(StringComparer.Ordinal));
        Assert.Equal(["CASCADE"], cascade);
    }

    /// <summary>The database itself refuses rows the contract forbids, each by its named constraint.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task The_checks_refuse_bad_rows()
    {
        await this.InsertVersionAsync(VersionId, "docs:intro");
        await this.InsertChunkAsync(VersionId, 0);

        await this.AssertRefusedAsync("CK_SourceVersion_Id_Format", VersionRow(OtherId.ToUpperInvariant(), "docs:other"));
        await this.AssertRefusedAsync("CK_SourceVersion_Id_Format", VersionRow(OtherId[..31], "docs:other"));
        await this.AssertRefusedAsync("CK_SourceVersion_SourceId_Format", VersionRow(OtherId, "Docs:other"));
        await this.AssertRefusedAsync("CK_SourceVersion_SourceId_Format", VersionRow(OtherId, "docs:other "));
        await this.AssertRefusedAsync("CK_SourceVersion_SourceId_Format", VersionRow(OtherId, "-docs"));
        await this.AssertRefusedAsync("CK_SourceVersion_SourceId_Format", VersionRow(OtherId, string.Empty));
        await this.AssertRefusedAsync("CK_SourceVersion_Origin_Length", VersionRow(OtherId, "docs:other", origin: string.Empty));
        await this.AssertRefusedAsync("CK_SourceVersion_Dimensions", VersionRow(OtherId, "docs:other", dimensions: 0));
        await this.AssertRefusedAsync("CK_SourceVersion_Dimensions", VersionRow(OtherId, "docs:other", dimensions: 8193));
        await this.AssertRefusedAsync("CK_SourceVersion_ChunkCount", VersionRow(OtherId, "docs:other", chunkCount: 0));
        await this.AssertRefusedAsync("CK_SourceVersion_ChunkCount", VersionRow(OtherId, "docs:other", chunkCount: 1025));
        await this.AssertRefusedAsync("CK_SourceVersion_SupersededAt", VersionRow(OtherId, "docs:other", supersededAt: Earlier));
        await this.AssertRefusedAsync("UQ_SourceVersion_SourceId_Current", VersionRow(OtherId, "docs:intro"));
        await this.AssertRefusedAsync("PK_SourceVersion", VersionRow(VersionId, "docs:other"));
        await this.AssertRefusedAsync("CK_Chunk_Vector_Length", ChunkRow(VersionId, 1, vector: "0x000000000000"));
        await this.AssertRefusedAsync("CK_Chunk_Vector_Length", ChunkRow(VersionId, 1, vector: "0x"));
        await this.AssertRefusedAsync("CK_Chunk_Vector_Length", ChunkRow(VersionId, 1, vector: "CAST(REPLICATE(CAST(0x00 AS varbinary(max)), 32772) AS varbinary(max))"));
        await this.AssertRefusedAsync("CK_Chunk_Position", ChunkRow(VersionId, -1));
        await this.AssertRefusedAsync("CK_Chunk_Position", ChunkRow(VersionId, 1024));
        await this.AssertRefusedAsync("CK_Chunk_Text_Length", ChunkRow(VersionId, 1, text: string.Empty));
        await this.AssertRefusedAsync("PK_Chunk", ChunkRow(VersionId, 0));
        await this.AssertRefusedAsync("FK_Chunk_SourceVersion", ChunkRow(OtherId, 0));

        Assert.Equal(["1", "1"], await this.database.QueryAsync("SELECT CAST(COUNT(*) AS varchar(10)) AS Value FROM knowledge.SourceVersion UNION ALL SELECT CAST(COUNT(*) AS varchar(10)) FROM knowledge.Chunk"));
    }

    /// <summary>
    /// The checks accept the edges of every rule: a superseded row next to the current one, a superseded time equal
    /// to the creation time, the largest vector and the last position.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task The_checks_accept_the_edges_of_each_rule()
    {
        await this.InsertVersionAsync(VersionId, "docs:intro", supersededAt: Created);
        await this.database.ExecuteAsync(VersionRow(OtherId, "docs:intro", dimensions: 8192, chunkCount: 1024));
        await this.database.ExecuteAsync(ChunkRow(OtherId, 1023, vector: "CAST(REPLICATE(CAST(0x00 AS varbinary(max)), 32768) AS varbinary(max))", text: new string('x', 1200)));
        await this.database.ExecuteAsync(ChunkRow(OtherId, 0, text: " "));

        Assert.Equal(["2"], await this.database.QueryAsync("SELECT CAST(COUNT(*) AS varchar(10)) AS Value FROM knowledge.SourceVersion"));
    }

    /// <summary>
    /// A version dated before the current one, as a host whose clock runs behind another's would store it, still
    /// supersedes it: the superseded time is held at the old version's creation, within its check.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_version_dated_before_the_current_one_still_supersedes_it()
    {
        var store = this.database.CreateStore();
        await store.StoreVersionAsync(Source(version: "v1", updatedAfter: TimeSpan.FromHours(1)), Chunks("ahead", ModelA, [1f, 0f]));

        var outcome = await store.StoreVersionAsync(Source(version: "v2"), Chunks("behind", ModelA, [1f, 0f]));

        var lineage = await this.database.QueryAsync(
            "SELECT [Version] + CASE WHEN SupersededAt IS NULL THEN ' current' WHEN SupersededAt = CreatedAt THEN ' superseded at its creation' ELSE ' superseded later' END AS Value FROM knowledge.SourceVersion ORDER BY [Version]");
        Assert.Equal(IngestOutcome.Superseded, outcome);
        Assert.Equal("v2", (await store.GetSourceAsync("docs:intro"))!.Version);
        Assert.Equal(["v1 superseded at its creation", "v2 current"], lineage);
    }

    /// <summary>Superseding a version deletes its chunks; deleting a source removes every version and chunk.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Chunks_leave_with_their_version()
    {
        var store = this.database.CreateStore();
        await store.StoreVersionAsync(Source(version: "v1"), Chunks("a", ModelA, [1f, 0f], [0f, 1f]));
        await store.StoreVersionAsync(Source(version: "v2", updatedAfter: TimeSpan.FromMinutes(1)), Chunks("b", ModelA, [1f, 0f]));
        var afterSupersede = await this.RowCountsAsync();

        Assert.True(await store.DeleteSourceAsync("docs:intro"));

        Assert.Equal("2 versions, 1 chunks", afterSupersede);
        Assert.Equal("0 versions, 0 chunks", await this.RowCountsAsync());
    }

    /// <inheritdoc />
    public void Dispose() => this.database.Dispose();

    private static string VersionRow(string id, string sourceId, string origin = "https://example.test/guide", int dimensions = 2, int chunkCount = 1, string? supersededAt = null) =>
        "INSERT INTO knowledge.SourceVersion (Id, SourceId, Origin, [Version], EmbeddingModelId, Dimensions, ChunkCount, CreatedAt, SupersededAt) VALUES "
        + $"('{id}', '{sourceId}', N'{origin}', 'v1', N'model-a', {dimensions}, {chunkCount}, {Created}, {supersededAt ?? "NULL"})";

    private static string ChunkRow(string versionId, int position, string vector = Vector, string text = "passage") =>
        $"INSERT INTO knowledge.Chunk (SourceVersionId, Position, [Text], Vector, CreatedAt) VALUES ('{versionId}', {position}, N'{text}', {vector}, {Created})";

    private Task InsertVersionAsync(string id, string sourceId, string? supersededAt = null) =>
        this.database.ExecuteAsync(VersionRow(id, sourceId, supersededAt: supersededAt));

    private Task InsertChunkAsync(string versionId, int position) => this.database.ExecuteAsync(ChunkRow(versionId, position));

    private async Task AssertRefusedAsync(string constraint, string sql)
    {
        var refused = await Assert.ThrowsAsync<SqlException>(() => this.database.ExecuteAsync(sql));
        Assert.Contains(constraint, refused.Message, StringComparison.Ordinal);
    }

    private async Task<string> RowCountsAsync() =>
        Assert.Single(await this.database.QueryAsync(
            "SELECT CAST((SELECT COUNT(*) FROM knowledge.SourceVersion) AS varchar(10)) + ' versions, ' + CAST((SELECT COUNT(*) FROM knowledge.Chunk) AS varchar(10)) + ' chunks' AS Value"));
}
