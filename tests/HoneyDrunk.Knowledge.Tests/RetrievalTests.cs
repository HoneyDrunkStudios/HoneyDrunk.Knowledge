using HoneyDrunk.AI.Abstractions.Embeddings;
using HoneyDrunk.Knowledge.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HoneyDrunk.Knowledge.Tests;

/// <summary>Retrieving ranked, attributed passages through the embedding generator and the store.</summary>
public sealed class RetrievalTests
{
    private const string Intro = "docs:intro";
    private const string Guide = "docs:guide";

    /// <summary>
    /// With the hash generator, identical text is the only reliable match: a query equal to a chunk's text ranks
    /// that chunk first with a score of 1, and every other chunk lower.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task The_exact_text_of_a_chunk_ranks_it_first_with_score_one()
    {
        using var rig = new RuntimeRig(new ScriptedEmbeddingGenerator(RuntimeRig.Model));
        await rig.IngestAsync(RuntimeRig.Document(Intro, "v1", 3));

        var result = await rig.RetrieveAsync(new RetrievalRequest(RuntimeRig.Passage(Intro, "v1", 1), [Intro], 3));

        Assert.Equal(3, result.Chunks.Count);
        var top = result.Chunks[0];
        Assert.Equal(1, top.Position);
        Assert.Equal(RuntimeRig.Passage(Intro, "v1", 1), top.Text);
        Assert.Equal(1d, top.Score, 12);
        Assert.All(result.Chunks.Skip(1), chunk => Assert.True(chunk.Score < top.Score));
        Assert.Empty(result.MismatchedSourceIds);
    }

    /// <summary>Every result carries its source version, as stored, its position and its text (invariant 145).</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Results_carry_source_version_and_position()
    {
        using var rig = new RuntimeRig(new ScriptedEmbeddingGenerator(RuntimeRig.Model));
        var ingested = await rig.IngestAsync(RuntimeRig.Document(Intro, "v1", 3));

        var result = await rig.RetrieveAsync(new RetrievalRequest("How do I begin?", [Intro], 50));

        Assert.Equal(new[] { 0, 1, 2 }, result.Chunks.Select(chunk => chunk.Position).Order());
        Assert.All(result.Chunks, chunk =>
        {
            Assert.Equal(Intro, chunk.Source.SourceId);
            Assert.Equal(RuntimeRig.Origin, chunk.Source.Origin);
            Assert.Equal("v1", chunk.Source.Version);
            Assert.Equal(RuntimeRig.Start, chunk.Source.LastUpdatedAt);
            Assert.Equal(RuntimeRig.Model, chunk.Source.EmbeddingModelId);
            Assert.Equal(ingested.Source.LastUpdatedAt, chunk.Source.LastUpdatedAt);
            Assert.Equal(RuntimeRig.Passage(Intro, "v1", chunk.Position), chunk.Text);
            Assert.InRange(chunk.Score, -1d, 1d);
        });
        Assert.Equal(
            result.Chunks.Select(chunk => chunk.Score).OrderDescending(),
            result.Chunks.Select(chunk => chunk.Score));
    }

    /// <summary>A query embedding from a model other than the configured one is refused, and the store is never searched.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_query_embedding_with_another_model_id_is_refused()
    {
        using var rig = new RuntimeRig(new ScriptedEmbeddingGenerator(RuntimeRig.Model)
        {
            Rewrite = (call, embeddings) => call == 2 ? [.. embeddings.Select(embedding => embedding with { ModelId = "model-b" })] : embeddings,
        });
        await rig.IngestAsync(RuntimeRig.Document(Intro, "v1", 1));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.RetrieveAsync(new RetrievalRequest("How do I begin?", [Intro])));

        Assert.Contains("model", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Telemetry.Named("knowledge.store"), activity => Equals(activity.GetTagItem("knowledge.operation"), "search"));
        Assert.Equal("error", Assert.Single(rig.Telemetry.Named("knowledge.retrieve")).GetTagItem("knowledge.outcome"));
    }

    /// <summary>
    /// A query answered with no embedding, more than one, one for other text, or a vector outside 1 to 8,192
    /// finite values is refused before the store is searched.
    /// </summary>
    /// <param name="kind">What the generator gets wrong.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData("none")]
    [InlineData("two")]
    [InlineData("other-text")]
    [InlineData("not-finite")]
    [InlineData("no-values")]
    public async Task A_query_embedding_that_is_not_one_for_the_query_is_refused(string kind)
    {
        using var rig = new RuntimeRig(new ScriptedEmbeddingGenerator(RuntimeRig.Model)
        {
            Rewrite = (call, embeddings) => call == 2 ? WrongQuery(kind, embeddings) : embeddings,
        });
        await rig.IngestAsync(RuntimeRig.Document(Intro, "v1", 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.RetrieveAsync(new RetrievalRequest("How do I begin?", [Intro])));

        Assert.DoesNotContain(rig.Telemetry.Named("knowledge.store"), activity => Equals(activity.GetTagItem("knowledge.operation"), "search"));
    }

    /// <summary>
    /// Two runtimes configured with two models share one store. A source ingested under the other model is listed
    /// as mismatched and none of its passages is scored (invariant 146).
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Sources_ingested_under_another_model_are_listed_not_scored()
    {
        using var first = new RuntimeRig(new ScriptedEmbeddingGenerator(RuntimeRig.Model));
        using var second = new RuntimeRig(new ScriptedEmbeddingGenerator("model-b"), "model-b", first.Store);
        await first.IngestAsync(RuntimeRig.Document(Intro, "v1", 2));
        await second.IngestAsync(RuntimeRig.Document(Guide, "v1", 2));

        var fromFirst = await first.RetrieveAsync(new RetrievalRequest("How do I begin?", [Intro, Guide, "docs:missing"], 50));
        var fromSecond = await second.RetrieveAsync(new RetrievalRequest("How do I begin?", [Intro, Guide], 50));

        Assert.Equal(2, fromFirst.Chunks.Count);
        Assert.All(fromFirst.Chunks, chunk => Assert.Equal(Intro, chunk.Source.SourceId));
        Assert.Equal(new[] { Guide }, fromFirst.MismatchedSourceIds);
        Assert.Equal(2, fromSecond.Chunks.Count);
        Assert.All(fromSecond.Chunks, chunk => Assert.Equal(Guide, chunk.Source.SourceId));
        Assert.Equal(new[] { Intro }, fromSecond.MismatchedSourceIds);
        Assert.Equal(1, Assert.Single(first.Telemetry.Named("knowledge.retrieve")).GetTagItem("knowledge.mismatched_count"));
    }

    /// <summary>After a new version, a query matching an old passage exactly finds only the new version's chunks.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Retrieval_after_a_new_version_returns_only_its_chunks()
    {
        using var rig = new RuntimeRig(new ScriptedEmbeddingGenerator(RuntimeRig.Model));
        await rig.IngestAsync(RuntimeRig.Document(Intro, "v1", 2));
        rig.Time.Advance(TimeSpan.FromDays(1));
        await rig.IngestAsync(RuntimeRig.Document(Intro, "v2", 3));

        var result = await rig.RetrieveAsync(new RetrievalRequest(RuntimeRig.Passage(Intro, "v1", 0), [Intro], 50));

        Assert.Equal(3, result.Chunks.Count);
        Assert.All(result.Chunks, chunk =>
        {
            Assert.Equal("v2", chunk.Source.Version);
            Assert.Equal(RuntimeRig.Start.AddDays(1), chunk.Source.LastUpdatedAt);
            Assert.True(chunk.Score < 1d);
        });
    }

    /// <summary>The request's limit bounds the result, and a null request is refused.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task The_limit_bounds_the_result_and_a_null_request_is_refused()
    {
        using var rig = new RuntimeRig(new ScriptedEmbeddingGenerator(RuntimeRig.Model));
        await rig.IngestAsync(RuntimeRig.Document(Intro, "v1", 4));

        var result = await rig.RetrieveAsync(new RetrievalRequest(RuntimeRig.Passage(Intro, "v1", 2), [Intro], 2));

        Assert.Equal(2, result.Chunks.Count);
        Assert.Equal(2, result.Chunks[0].Position);
        await Assert.ThrowsAsync<ArgumentNullException>(() => rig.RetrieveAsync(null!));
    }

    /// <summary>An option the runtime refuses is refused on the first retrieval, before the generator is called.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task An_empty_model_option_is_refused_on_first_retrieval()
    {
        using var rig = new RuntimeRig(new ScriptedEmbeddingGenerator(RuntimeRig.Model), modelId: string.Empty);

        var error = await Assert.ThrowsAsync<OptionsValidationException>(
            () => rig.RetrieveAsync(new RetrievalRequest("How do I begin?", [Intro])));

        Assert.Contains("EmbeddingModelId", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, rig.Generator.Calls);
    }

    private static Embedding[] WrongQuery(string kind, IReadOnlyList<Embedding> embeddings) => kind switch
    {
        "none" => [],
        "two" => [embeddings[0], embeddings[0]],
        "other-text" => [embeddings[0] with { Value = "Another question." }],
        "not-finite" => [embeddings[0] with { Vector = [float.PositiveInfinity, .. embeddings[0].Vector.Skip(1)] }],
        _ => [embeddings[0] with { Vector = [] }],
    };
}
