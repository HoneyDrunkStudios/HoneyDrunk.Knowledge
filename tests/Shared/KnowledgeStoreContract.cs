using HoneyDrunk.Knowledge.Abstractions;
using Xunit;
using static HoneyDrunk.Knowledge.Providers.Contract.TestSources;

namespace HoneyDrunk.Knowledge.Providers.Contract;

/// <summary>
/// The behavior every <see cref="IKnowledgeStore"/> provider must show. The in-memory provider is the
/// reference: a provider passes when it gives the same answers.
/// </summary>
public abstract class KnowledgeStoreContract
{
    /// <summary>The cosine of (1,1) against (1,0).</summary>
    private const double Diagonal = 0.7071067811865475;

    /// <summary>A first version is added and readable.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task First_version_is_added_and_readable()
    {
        var store = this.CreateStore();
        var source = Source();

        var outcome = await store.StoreVersionAsync(source, Chunks("a", ModelA, [1f, 0f], [0f, 1f]));

        Assert.Equal(IngestOutcome.Added, outcome);
        AssertSameSource(source, (await store.GetSourceAsync("docs:intro"))!);
        Assert.Null(await store.GetSourceAsync("docs:other"));
    }

    /// <summary>The same version under the same model writes nothing.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Same_version_and_model_is_unchanged_and_writes_nothing()
    {
        var store = this.CreateStore();
        var first = Source(updatedAfter: TimeSpan.Zero);
        await store.StoreVersionAsync(first, Chunks("first", ModelA, [1f, 0f]));

        var outcome = await store.StoreVersionAsync(Source(updatedAfter: TimeSpan.FromHours(1)), Chunks("second", ModelA, [1f, 0f], [0f, 1f]));

        Assert.Equal(IngestOutcome.Unchanged, outcome);
        AssertSameSource(first, (await store.GetSourceAsync("docs:intro"))!);
        var found = await store.SearchAsync(Query([1f, 0f], ["docs:intro"]));
        Assert.Equal(["first 0"], found.Chunks.Select(chunk => chunk.Text));
    }

    /// <summary>A new version replaces the old, and retrieval sees only the new one.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task New_version_supersedes_and_retrieval_sees_only_it()
    {
        var store = this.CreateStore();
        await store.StoreVersionAsync(Source(version: "v1"), Chunks("old", ModelA, [1f, 0f], [0f, 1f]));
        var next = Source(version: "v2", updatedAfter: TimeSpan.FromHours(1));

        var outcome = await store.StoreVersionAsync(next, Chunks("new", ModelA, [1f, 0f]));

        Assert.Equal(IngestOutcome.Superseded, outcome);
        AssertSameSource(next, (await store.GetSourceAsync("docs:intro"))!);
        var found = await store.SearchAsync(Query([1f, 0f], ["docs:intro"]));
        var chunk = Assert.Single(found.Chunks);
        Assert.Equal("new 0", chunk.Text);
        Assert.Equal("v2", chunk.Source.Version);
    }

    /// <summary>The same version under another model is a new version.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Same_version_under_another_model_supersedes()
    {
        var store = this.CreateStore();
        await store.StoreVersionAsync(Source(model: ModelA), Chunks("a", ModelA, [1f, 0f]));

        var outcome = await store.StoreVersionAsync(Source(model: ModelB), Chunks("b", ModelB, [1f, 0f]));

        Assert.Equal(IngestOutcome.Superseded, outcome);
        Assert.Equal(ModelB, (await store.GetSourceAsync("docs:intro"))!.EmbeddingModelId);
        var underA = await store.SearchAsync(Query([1f, 0f], ["docs:intro"], model: ModelA));
        Assert.Empty(underA.Chunks);
        Assert.Equal(["docs:intro"], underA.MismatchedSourceIds);
        var underB = await store.SearchAsync(Query([1f, 0f], ["docs:intro"], model: ModelB));
        Assert.Equal(["b 0"], underB.Chunks.Select(chunk => chunk.Text));
    }

    /// <summary>Hits rank by cosine, then source id, then position, with known scores.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Search_ranks_by_cosine_then_source_then_position()
    {
        var store = this.CreateStore();
        await store.StoreVersionAsync(Source("docs:a"), Chunks("a", ModelA, [0f, 1f], [1f, 1f], [1f, 0f]));
        await store.StoreVersionAsync(Source("docs:b"), Chunks("b", ModelA, [2f, 0f], [1f, 1f], [-1f, 0f]));

        var found = await store.SearchAsync(Query([1f, 0f], ["docs:b", "docs:a"]));

        Assert.Equal(
            ["docs:a#2", "docs:b#0", "docs:a#1", "docs:b#1", "docs:a#0", "docs:b#2"],
            found.Chunks.Select(chunk => $"{chunk.Source.SourceId}#{chunk.Position}"));
        Assert.Equal(
            [1d, 1d, Diagonal, Diagonal, 0d, -1d],
            found.Chunks.Select(chunk => chunk.Score),
            new ScoreComparer());
        Assert.Empty(found.MismatchedSourceIds);
    }

    /// <summary>Equal scores in one source come back in position order.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Equal_scores_in_one_source_come_back_in_position_order()
    {
        var store = this.CreateStore();
        await store.StoreVersionAsync(Source(), Chunks("a", ModelA, [1f, 0f], [1f, 0f], [1f, 0f], [0f, 1f]));

        var found = await store.SearchAsync(Query([1f, 0f], ["docs:intro"]));

        Assert.Equal([0, 1, 2, 3], found.Chunks.Select(chunk => chunk.Position));
    }

    /// <summary>Identical vectors score one, never more, however the arithmetic rounds.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Identical_vectors_score_one_without_exceeding_it()
    {
        var store = this.CreateStore();
        var random = new Random(7);
        var vectors = Enumerable.Range(0, 60)
            .Select(_ => Enumerable.Range(0, 37).Select(_ => (float)((random.NextDouble() * 2) - 1)).ToArray())
            .ToArray();
        await store.StoreVersionAsync(Source(), Chunks("v", ModelA, vectors));

        foreach (var vector in vectors)
        {
            var found = await store.SearchAsync(Query(vector, ["docs:intro"], limit: 1));

            var top = Assert.Single(found.Chunks);
            Assert.InRange(top.Score, 1 - 1e-9, 1);
        }
    }

    /// <summary>The limit and the named sources bound the result.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Search_respects_the_limit_and_the_named_sources()
    {
        var store = this.CreateStore();
        await store.StoreVersionAsync(Source("docs:a"), Chunks("a", ModelA, [1f, 0f], [1f, 1f], [0f, 1f]));
        await store.StoreVersionAsync(Source("docs:b"), Chunks("b", ModelA, [1f, 0f]));

        var limited = await store.SearchAsync(Query([1f, 0f], ["docs:a", "docs:b"], limit: 2));
        var onlyB = await store.SearchAsync(Query([1f, 0f], ["docs:b"]));

        Assert.Equal(["a 0", "b 0"], limited.Chunks.Select(chunk => chunk.Text));
        Assert.Equal(["b 0"], onlyB.Chunks.Select(chunk => chunk.Text));
    }

    /// <summary>Every result carries its source version and position.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Every_result_carries_its_source_version_and_position()
    {
        var store = this.CreateStore();
        var source = Source(version: "v7", updatedAfter: TimeSpan.FromMinutes(5));
        await store.StoreVersionAsync(source, Chunks("a", ModelA, [0f, 1f], [1f, 0f]));

        var found = await store.SearchAsync(Query([1f, 0f], ["docs:intro"]));

        Assert.Equal([1, 0], found.Chunks.Select(chunk => chunk.Position));
        foreach (var chunk in found.Chunks)
        {
            AssertSameSource(source, chunk.Source);
            Assert.Equal($"a {chunk.Position}", chunk.Text);
        }
    }

    /// <summary>Sources of another model or another vector length are listed, never scored.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Mismatched_sources_are_listed_and_never_scored()
    {
        var store = this.CreateStore();
        await store.StoreVersionAsync(Source("docs:ok"), Chunks("ok", ModelA, [1f, 0f]));
        await store.StoreVersionAsync(Source("docs:z-model", model: ModelB), Chunks("m", ModelB, [1f, 0f]));
        await store.StoreVersionAsync(Source("docs:a-length"), Chunks("l", ModelA, [1f, 0f, 0f]));

        var found = await store.SearchAsync(Query([1f, 0f], ["docs:z-model", "docs:ok", "docs:a-length"]));

        Assert.Equal(["ok 0"], found.Chunks.Select(chunk => chunk.Text));
        Assert.Equal(["docs:a-length", "docs:z-model"], found.MismatchedSourceIds);
    }

    /// <summary>Unknown source ids are ignored, and are not mismatches.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Unknown_source_ids_are_ignored()
    {
        var store = this.CreateStore();
        await store.StoreVersionAsync(Source(), Chunks("a", ModelA, [1f, 0f]));

        var found = await store.SearchAsync(Query([1f, 0f], ["docs:missing", "docs:intro"]));

        Assert.Equal(["a 0"], found.Chunks.Select(chunk => chunk.Text));
        Assert.Empty(found.MismatchedSourceIds);
        var none = await store.SearchAsync(Query([1f, 0f], ["docs:missing"]));
        Assert.Empty(none.Chunks);
        Assert.Empty(none.MismatchedSourceIds);
    }

    /// <summary>A vector whose norm is zero scores zero, on either side.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_zero_vector_scores_zero()
    {
        var store = this.CreateStore();
        await store.StoreVersionAsync(Source(), Chunks("a", ModelA, [0f, 0f], [1f, 0f]));

        var stored = await store.SearchAsync(Query([1f, 0f], ["docs:intro"]));
        var queried = await store.SearchAsync(Query([0f, 0f], ["docs:intro"]));

        Assert.Equal(["a 1=1", "a 0=0"], stored.Chunks.Select(chunk => $"{chunk.Text}={chunk.Score}"));
        Assert.Equal(["a 0=0", "a 1=0"], queried.Chunks.Select(chunk => $"{chunk.Text}={chunk.Score}"));
    }

    /// <summary>A chunk set that breaks a rule is refused, naming <c>chunks</c>, and nothing is stored.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Chunks_out_of_order_of_mixed_length_or_of_another_model_are_refused()
    {
        var store = this.CreateStore();
        var bad = new Dictionary<string, KnowledgeChunk[]>
        {
            ["empty"] = [],
            ["not from zero"] = [Chunk(1, "x", [1f, 0f])],
            ["out of order"] = [Chunk(1, "x", [1f, 0f]), Chunk(0, "y", [1f, 0f])],
            ["gap"] = [Chunk(0, "x", [1f, 0f]), Chunk(2, "y", [1f, 0f])],
            ["repeat"] = [Chunk(0, "x", [1f, 0f]), Chunk(0, "y", [1f, 0f])],
            ["mixed length"] = [Chunk(0, "x", [1f, 0f]), Chunk(1, "y", [1f, 0f, 0f])],
            ["another model"] = [Chunk(0, "x", [1f, 0f]), Chunk(1, "y", [1f, 0f], ModelB)],
            ["too many"] = [.. Enumerable.Range(0, 1025).Select(position => position < 1024 ? Chunk(position, "x", [1f]) : Chunk(0, "x", [1f]))],
        };

        foreach (var (name, chunks) in bad)
        {
            var thrown = await Assert.ThrowsAsync<ArgumentException>(() => store.StoreVersionAsync(Source(), chunks));
            Assert.True(thrown.ParamName == "chunks", name);
        }

        Assert.Null(await store.GetSourceAsync("docs:intro"));
        var kept = await store.StoreVersionAsync(Source(version: "v1"), Chunks("keep", ModelA, [1f, 0f]));
        Assert.Equal(IngestOutcome.Added, kept);
        var refused = await Assert.ThrowsAsync<ArgumentException>(() => store.StoreVersionAsync(Source(version: "v2"), bad["mixed length"]));
        Assert.Equal("chunks", refused.ParamName);
        Assert.Equal("v1", (await store.GetSourceAsync("docs:intro"))!.Version);
        var found = await store.SearchAsync(Query([1f, 0f], ["docs:intro"]));
        Assert.Equal(["keep 0"], found.Chunks.Select(chunk => chunk.Text));
    }

    /// <summary>A delete removes every version and says whether the source existed.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Delete_removes_every_version_and_reports_whether_it_existed()
    {
        var store = this.CreateStore();
        await store.StoreVersionAsync(Source(version: "v1"), Chunks("a", ModelA, [1f, 0f]));
        await store.StoreVersionAsync(Source(version: "v2"), Chunks("b", ModelA, [1f, 0f]));

        Assert.True(await store.DeleteSourceAsync("docs:intro"));
        Assert.False(await store.DeleteSourceAsync("docs:intro"));
        Assert.False(await store.DeleteSourceAsync("docs:never"));
        Assert.Null(await store.GetSourceAsync("docs:intro"));
        Assert.Empty((await store.SearchAsync(Query([1f, 0f], ["docs:intro"]))).Chunks);
        Assert.Equal(IngestOutcome.Added, await store.StoreVersionAsync(Source(version: "v1"), Chunks("c", ModelA, [1f, 0f])));
    }

    /// <summary>Twenty writers of twenty versions leave exactly one current version whole.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Concurrent_versions_leave_exactly_one_current()
    {
        var stores = Enumerable.Range(0, 20).Select(_ => this.CreateStore()).ToArray();
        var versions = Enumerable.Range(0, 20).Select(index => $"v{index}").ToArray();

        var results = await Task.WhenAll(stores.Select((store, index) => Task.Run(async () =>
        {
            var outcomes = new List<IngestOutcome>();
            foreach (var version in versions.Skip(index).Concat(versions.Take(index)))
            {
                var chunks = Chunks($"text-{version}", ModelA, [1f, 0f], [1f, 1f], [0f, 1f]);
                outcomes.Add(await store.StoreVersionAsync(Source(version: version), chunks));
            }

            return outcomes;
        })));

        Assert.Equal(400, results.Sum(list => list.Count));
        Assert.Equal(1, results.SelectMany(list => list).Count(outcome => outcome == IngestOutcome.Added));
        var current = (await stores[0].GetSourceAsync("docs:intro"))!;
        Assert.Contains(current.Version, versions);
        var found = await stores[1].SearchAsync(Query([1f, 0f], ["docs:intro"]));
        Assert.Equal(3, found.Chunks.Count);
        Assert.All(found.Chunks, chunk =>
        {
            Assert.Equal(current.Version, chunk.Source.Version);
            Assert.StartsWith($"text-{current.Version} ", chunk.Text, StringComparison.Ordinal);
        });
        Assert.Equal([0, 1, 2], found.Chunks.Select(chunk => chunk.Position).Order());
    }

    /// <summary>Text comes back exactly as stored.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Text_round_trips_verbatim()
    {
        var store = this.CreateStore();
        const string Tail = "\r\nline two \u00A0 \U0001F600 \u00E9  \r\n  ";
        var text = new string('x', 1200 - Tail.Length) + Tail;
        Assert.Equal(1200, text.Length);
        await store.StoreVersionAsync(Source(), [Chunk(0, text, [1f, 0f])]);

        var found = await store.SearchAsync(Query([1f, 0f], ["docs:intro"]));

        Assert.Equal(text, Assert.Single(found.Chunks).Text);
    }

    /// <summary>A cancelled token is observed before any work.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Cancellation_is_observed_before_any_work()
    {
        var store = this.CreateStore();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var token = cancelled.Token;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetSourceAsync("docs:intro", token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.StoreVersionAsync(Source(), Chunks("a", ModelA, [1f, 0f]), token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SearchAsync(Query([1f, 0f], ["docs:intro"]), token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.DeleteSourceAsync("docs:intro", token));
        Assert.Null(await store.GetSourceAsync("docs:intro"));
    }

    /// <summary>Null arguments are refused.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Null_arguments_are_refused()
    {
        var store = this.CreateStore();
        var chunks = Chunks("a", ModelA, [1f, 0f]);

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.GetSourceAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.StoreVersionAsync(null!, chunks));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.StoreVersionAsync(Source(), null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SearchAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.DeleteSourceAsync(null!));
    }

    /// <summary>Compares two source versions field by field. Records are never compared as instances.</summary>
    /// <param name="expected">The expected source.</param>
    /// <param name="actual">The actual source.</param>
    protected static void AssertSameSource(KnowledgeSource expected, KnowledgeSource actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected.SourceId, actual.SourceId);
        Assert.Equal(expected.Origin, actual.Origin);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.LastUpdatedAt, actual.LastUpdatedAt);
        Assert.Equal(expected.EmbeddingModelId, actual.EmbeddingModelId);
    }

    /// <summary>Creates a store. A second call returns a second store over the same storage.</summary>
    /// <returns>The store.</returns>
    protected abstract IKnowledgeStore CreateStore();

    private sealed class ScoreComparer : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 1e-12;

        public int GetHashCode(double obj) => 0;
    }
}
