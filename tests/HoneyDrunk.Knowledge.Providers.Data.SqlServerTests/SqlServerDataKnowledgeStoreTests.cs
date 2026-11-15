using HoneyDrunk.Knowledge.Abstractions;
using HoneyDrunk.Knowledge.Providers.Contract;
using Xunit;
using static HoneyDrunk.Knowledge.Providers.Contract.TestSources;

namespace HoneyDrunk.Knowledge.Providers.Data.SqlServerTests;

/// <summary>
/// The store contract against the Data provider on SQL Server, over the shipped table scripts. Each
/// <see cref="CreateStore"/> is a new store over the same database, so the contract's twenty writers race on
/// SQL Server's own locks and its filtered unique index.
/// </summary>
public sealed class SqlServerDataKnowledgeStoreTests : KnowledgeStoreContract, IDisposable
{
    private readonly SqlServerDatabase database = new();

    /// <summary>
    /// Versions and model ids compare by exact value: <c>V1</c> after <c>v1</c> is a new version, a model id that
    /// differs only in case or in Unicode composition is another model, and the database finds neither by the other.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Text_compares_by_exact_value()
    {
        const string Composed = "model-\u00e9";
        const string Decomposed = "model-e\u0301";
        var store = this.CreateStore();
        await store.StoreVersionAsync(Source(version: "v1"), Chunks("lower", ModelA, [1f, 0f]));

        var upper = await store.StoreVersionAsync(Source(version: "V1", updatedAfter: TimeSpan.FromMinutes(1)), Chunks("upper", ModelA, [1f, 0f]));
        await store.StoreVersionAsync(Source("docs:b", model: Composed), Chunks("b", Composed, [1f, 0f]));
        var byCase = await store.SearchAsync(Query([1f, 0f], ["docs:intro", "docs:b"], model: "Model-A"));
        var byComposition = await store.SearchAsync(Query([1f, 0f], ["docs:b"], model: Decomposed));

        Assert.Equal(IngestOutcome.Superseded, upper);
        Assert.Equal("V1", (await store.GetSourceAsync("docs:intro"))!.Version);
        Assert.Empty(byCase.Chunks);
        Assert.Equal(["docs:b", "docs:intro"], byCase.MismatchedSourceIds);
        Assert.Empty(byComposition.Chunks);
        Assert.Equal(["docs:b"], byComposition.MismatchedSourceIds);
        Assert.Equal(["1"], await this.CountAsync("[SourceId] = 'docs:intro' AND [Version] = 'V1'"));
        Assert.Equal(["1"], await this.CountAsync("[SourceId] = 'docs:intro' AND [Version] = 'v1'"));
        Assert.Equal(["0"], await this.CountAsync("[EmbeddingModelId] = N'MODEL-A'"));
        Assert.Equal(["0"], await this.CountAsync($"[EmbeddingModelId] = N'{Decomposed}'"));
        Assert.Equal(["1"], await this.CountAsync($"[EmbeddingModelId] = N'{Composed}'"));
        Assert.Equal(["0"], await this.CountAsync("[SourceId] = 'DOCS:INTRO'"));
    }

    /// <inheritdoc />
    public void Dispose() => this.database.Dispose();

    /// <inheritdoc />
    protected override IKnowledgeStore CreateStore() => this.database.CreateStore();

    private Task<IReadOnlyList<string>> CountAsync(string condition) =>
        this.database.QueryAsync("SELECT CAST(COUNT(*) AS varchar(10)) AS Value FROM knowledge.SourceVersion WHERE " + condition);
}
