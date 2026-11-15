using HoneyDrunk.AI.Abstractions.Embeddings;
using HoneyDrunk.AI.Providers.InMemory;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Knowledge.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HoneyDrunk.Knowledge.Tests;

/// <summary>Ingesting documents through the embedding generator into the store.</summary>
public sealed class IngestTests
{
    private const string Model = "model-a";
    private const string SourceId = "docs:intro";
    private const string Origin = "https://example.test/guide";
    private const string Sentinel = "CONTENT-SENTINEL-4b1e";

    private static readonly DateTimeOffset Start = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A new document is added under the configured model, at the clock's time, read once.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_new_document_is_added_with_its_model_and_time()
    {
        using var rig = new Rig(new ScriptedEmbeddingGenerator(Model));
        rig.Time.AutoAdvanceAmount = TimeSpan.FromMinutes(1);

        var result = await rig.IngestAsync(Document("v1", 3));

        Assert.Equal(IngestOutcome.Added, result.Outcome);
        Assert.Equal(3, result.ChunkCount);
        AssertSource(result.Source, "v1", Start);
        AssertSource(await rig.Store.GetSourceAsync(SourceId), "v1", Start);
        Assert.Equal(Start.AddMinutes(1), rig.Time.GetUtcNow());
        var stored = Assert.Single(rig.Store.StoreCalls);
        Assert.Equal(new[] { 0, 1, 2 }, stored.Chunks.Select(chunk => chunk.Position));
        Assert.Equal("added", Assert.Single(rig.Telemetry.Named("knowledge.ingest")).GetTagItem("knowledge.outcome"));
    }

    /// <summary>A version already current under the same model is unchanged, and the generator is never called.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task The_same_version_is_unchanged_with_no_embedding_call()
    {
        using var rig = new Rig(new ScriptedEmbeddingGenerator(Model));
        var first = await rig.IngestAsync(Document("v1", 2));
        rig.Time.Advance(TimeSpan.FromHours(1));

        var second = await rig.IngestAsync(Document("v1", 3));

        Assert.Equal(IngestOutcome.Unchanged, second.Outcome);
        Assert.Equal(0, second.ChunkCount);
        AssertSource(second.Source, "v1", first.Source.LastUpdatedAt);
        Assert.Equal(1, rig.Generator.Calls);
        Assert.Single(rig.Store.StoreCalls);
        Assert.Equal("unchanged", rig.Telemetry.Named("knowledge.ingest")[^1].GetTagItem("knowledge.outcome"));
    }

    /// <summary>The same version under another configured model is embedded again and replaces the current one.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task The_same_version_under_another_model_is_embedded_again()
    {
        var store = new CapturingKnowledgeStore();
        using var first = new Rig(new ScriptedEmbeddingGenerator(Model), store: store);
        using var second = new Rig(new ScriptedEmbeddingGenerator("model-b"), modelId: "model-b", store: store);
        await first.IngestAsync(Document("v1", 2));

        var result = await second.IngestAsync(Document("v1", 2));

        Assert.Equal(IngestOutcome.Superseded, result.Outcome);
        Assert.Equal(1, second.Generator.Calls);
        Assert.Equal("model-b", (await store.GetSourceAsync(SourceId))!.EmbeddingModelId);
    }

    /// <summary>A new version supersedes the current one, and only its chunks remain searchable.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_new_version_supersedes()
    {
        using var rig = new Rig(new ScriptedEmbeddingGenerator(Model));
        await rig.IngestAsync(Document("v1", 2));
        rig.Time.Advance(TimeSpan.FromDays(1));

        var result = await rig.IngestAsync(Document("v2", 3));

        Assert.Equal(IngestOutcome.Superseded, result.Outcome);
        Assert.Equal(3, result.ChunkCount);
        AssertSource(result.Source, "v2", Start.AddDays(1));
        AssertSource(await rig.Store.GetSourceAsync(SourceId), "v2", Start.AddDays(1));
        var query = (await new InMemoryEmbeddingGenerator(Model).GenerateAsync([Passage("v1", 0)]))[0];
        var found = await rig.Store.SearchAsync(new SimilarityQuery(query, [SourceId], 50));
        Assert.Equal(3, found.Chunks.Count);
        Assert.All(found.Chunks, chunk => Assert.Equal("v2", chunk.Source.Version));
        Assert.Equal("superseded", rig.Telemetry.Named("knowledge.ingest")[^1].GetTagItem("knowledge.outcome"));
    }

    /// <summary>130 passages with a batch size of 64 take three calls: 64, 64 and 2 texts.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Embeddings_are_requested_in_batches_of_the_configured_size()
    {
        using var rig = new Rig(new ScriptedEmbeddingGenerator(Model), batchSize: 64);

        var result = await rig.IngestAsync(Document("v1", 130));

        Assert.Equal(130, result.ChunkCount);
        Assert.Equal(new[] { 64, 64, 2 }, rig.Generator.BatchSizes);
        Assert.Equal(130, Assert.Single(rig.Store.StoreCalls).Chunks.Count);
    }

    /// <summary>One batch answered by another model fails the ingest; the current version stays.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_batch_with_another_model_id_stores_nothing()
    {
        using var rig = new Rig(
            new ScriptedEmbeddingGenerator(Model)
            {
                Rewrite = (call, embeddings) => call == 3 ? [.. embeddings.Select(embedding => embedding with { ModelId = "model-b" })] : embeddings,
            },
            batchSize: 2);

        var error = await IngestSecondVersionAsync(rig);

        Assert.Contains("model", error.Message, StringComparison.OrdinalIgnoreCase);
        await AssertFirstVersionStaysAsync(rig, error);
    }

    /// <summary>A batch with one embedding too few or too many fails the ingest; the current version stays.</summary>
    /// <param name="kind">What the batch gets wrong.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("empty")]
    public async Task A_batch_with_a_missing_or_extra_embedding_stores_nothing(string kind)
    {
        using var rig = new Rig(
            new ScriptedEmbeddingGenerator(Model) { Rewrite = (call, embeddings) => call == 3 ? WrongCount(kind, embeddings) : embeddings },
            batchSize: 2);

        var error = await IngestSecondVersionAsync(rig);

        await AssertFirstVersionStaysAsync(rig, error);
    }

    /// <summary>An embedding for text other than the passage sent at its place fails the ingest.</summary>
    /// <param name="kind">How the text differs.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData("swapped")]
    [InlineData("changed")]
    public async Task An_embedding_for_other_text_stores_nothing(string kind)
    {
        using var rig = new Rig(
            new ScriptedEmbeddingGenerator(Model) { Rewrite = (call, embeddings) => call == 3 ? OtherText(kind, embeddings) : embeddings },
            batchSize: 2);

        var error = await IngestSecondVersionAsync(rig);

        await AssertFirstVersionStaysAsync(rig, error);
    }

    /// <summary>Each stored chunk is the generator's embedding at its place: its text, vector and model id.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Each_stored_chunk_is_the_embedding_the_generator_returned()
    {
        using var rig = new Rig(new ScriptedEmbeddingGenerator(Model), batchSize: 2);

        await rig.IngestAsync(Document("v1", 5));

        var chunks = Assert.Single(rig.Store.StoreCalls).Chunks;
        var returned = rig.Generator.Returned;
        Assert.Equal(5, chunks.Count);
        Assert.Equal(5, returned.Count);
        for (var position = 0; position < chunks.Count; position++)
        {
            Assert.Equal(position, chunks[position].Position);
            Assert.Equal(Passage("v1", position), chunks[position].Embedding.Value);
            Assert.Equal(returned[position].Value, chunks[position].Embedding.Value);
            Assert.Equal(returned[position].Vector, chunks[position].Embedding.Vector);
            Assert.Equal(returned[position].ModelId, chunks[position].Embedding.ModelId);
        }
    }

    /// <summary>Vectors of mixed length, or outside 1 to 8,192 finite values, fail the ingest.</summary>
    /// <param name="kind">What the vectors get wrong.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData("across-batches")]
    [InlineData("within-a-batch")]
    [InlineData("not-finite")]
    [InlineData("no-values")]
    [InlineData("too-many-values")]
    public async Task Vectors_of_mixed_length_store_nothing(string kind)
    {
        using var rig = new Rig(
            new ScriptedEmbeddingGenerator(Model) { Rewrite = (call, embeddings) => call == 3 ? WrongVectors(kind, embeddings) : embeddings },
            batchSize: 2);

        var error = await IngestSecondVersionAsync(rig);

        await AssertFirstVersionStaysAsync(rig, error);
    }

    /// <summary>A generator that throws stores nothing, and the error that reaches the caller holds no text.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_failing_generator_stores_nothing_and_the_error_holds_no_text()
    {
        using var rig = new Rig(new ScriptedEmbeddingGenerator(Model) { FailOnCall = 3 }, batchSize: 2);

        var error = await IngestSecondVersionAsync(rig);

        Assert.Equal("The scripted generator failed.", error.Message);
        await AssertFirstVersionStaysAsync(rig, error);
        Assert.Equal("error", rig.Telemetry.Named("knowledge.ingest")[^1].GetTagItem("knowledge.outcome"));
    }

    /// <summary>A version stored by another writer between the check and the store is reported as it stands.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_version_stored_meanwhile_is_reported_unchanged()
    {
        var store = new CapturingKnowledgeStore();
        var racer = new KnowledgeSource(SourceId, Origin, "v1", Start.AddMinutes(-5), Model);
        var racerChunks = new[] { new KnowledgeChunk(0, new Embedding("racer", [1f, 0f], Model)) };
        using var rig = new Rig(
            new ScriptedEmbeddingGenerator(Model) { BeforeCall = _ => store.StoreVersionAsync(racer, racerChunks) },
            store: store);

        var result = await rig.IngestAsync(Document("v1", 2));

        Assert.Equal(IngestOutcome.Unchanged, result.Outcome);
        Assert.Equal(0, result.ChunkCount);
        AssertSource(result.Source, "v1", racer.LastUpdatedAt);
    }

    /// <summary>A missing, blank, overlong or control-character model id is refused when the ingester is first used.</summary>
    /// <param name="kind">What the option gets wrong.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("overlong")]
    [InlineData("control")]
    public async Task An_empty_model_option_is_refused_on_first_use(string kind)
    {
        var modelId = kind switch
        {
            "empty" => string.Empty,
            "blank" => "   ",
            "overlong" => new string('m', KnowledgeLimits.MaxEmbeddingModelIdLength + 1),
            _ => "model\u0001a",
        };
        using var rig = new Rig(new ScriptedEmbeddingGenerator(Model), modelId: modelId);
        using var scope = rig.Services.CreateScope();
        var ingester = scope.ServiceProvider.GetRequiredService<IDocumentIngester>();

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => ingester.IngestAsync(Document("v1", 1)));

        Assert.Contains("EmbeddingModelId", error.Message, StringComparison.Ordinal);
        if (modelId.Trim().Length > 0)
        {
            Assert.DoesNotContain(modelId, error.Message, StringComparison.Ordinal);
        }

        Assert.Equal(0, rig.Generator.Calls);
        Assert.Empty(rig.Store.StoreCalls);
    }

    /// <summary>A batch size outside 1 to 256 is refused when the ingester is first used.</summary>
    /// <param name="batchSize">The batch size.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(257)]
    public async Task A_batch_size_outside_1_to_256_is_refused_on_first_use(int batchSize)
    {
        using var rig = new Rig(new ScriptedEmbeddingGenerator(Model), batchSize: batchSize);

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => rig.IngestAsync(Document("v1", 1)));

        Assert.Contains("EmbeddingBatchSize", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, rig.Generator.Calls);
    }

    /// <summary>The largest batch size is accepted and sends every passage in one call.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_batch_size_of_256_is_accepted()
    {
        using var rig = new Rig(new ScriptedEmbeddingGenerator(Model), batchSize: 256);

        await rig.IngestAsync(Document("v1", 3));

        Assert.Equal(new[] { 3 }, rig.Generator.BatchSizes);
    }

    private static async Task<Exception> IngestSecondVersionAsync(Rig rig)
    {
        await rig.IngestAsync(Document("v1", 1));
        Assert.Equal(1, rig.Generator.Calls);
        rig.Time.Advance(TimeSpan.FromDays(1));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => rig.IngestAsync(Document("v2", 4)));
        Assert.Equal(3, rig.Generator.Calls);
        return error;
    }

    private static async Task AssertFirstVersionStaysAsync(Rig rig, Exception error)
    {
        Assert.DoesNotContain(Sentinel, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Passage", error.Message, StringComparison.Ordinal);
        Assert.Single(rig.Store.StoreCalls);
        AssertSource(await rig.Store.GetSourceAsync(SourceId), "v1", Start);
        Assert.IsType<InvalidOperationException>(error);
    }

    private static void AssertSource(KnowledgeSource? source, string version, DateTimeOffset lastUpdatedAt)
    {
        Assert.NotNull(source);
        Assert.Equal(SourceId, source.SourceId);
        Assert.Equal(Origin, source.Origin);
        Assert.Equal(version, source.Version);
        Assert.Equal(lastUpdatedAt, source.LastUpdatedAt);
        Assert.Equal(Model, source.EmbeddingModelId);
    }

    private static KnowledgeDocument Document(string version, int passages) =>
        new(SourceId, Origin, version, string.Join("\n\n", Enumerable.Range(0, passages).Select(index => Passage(version, index))));

    // About 730 characters: two never fit in one 1,200-character chunk, so each passage is one chunk.
    private static string Passage(string version, int index) => $"Passage {index} of {version}. {Sentinel} {new string('x', 700)}";

    private static float[] Values(int count, float value) => [.. Enumerable.Repeat(value, count)];

    private static Embedding[] WrongCount(string kind, IReadOnlyList<Embedding> embeddings) => kind switch
    {
        "missing" => [.. embeddings.Take(embeddings.Count - 1)],
        "extra" => [.. embeddings, embeddings[0]],
        _ => [],
    };

    private static Embedding[] OtherText(string kind, IReadOnlyList<Embedding> embeddings) => kind switch
    {
        "swapped" => [.. embeddings.Reverse()],
        _ => [embeddings[0] with { Value = "other text" }, .. embeddings.Skip(1)],
    };

    private static Embedding[] WrongVectors(string kind, IReadOnlyList<Embedding> embeddings) => kind switch
    {
        "across-batches" => [.. embeddings.Select(embedding => embedding with { Vector = Values(4, 0.5f) })],
        "within-a-batch" => [embeddings[0] with { Vector = Values(4, 0.5f) }, .. embeddings.Skip(1)],
        "not-finite" => [embeddings[0] with { Vector = [float.NaN, .. embeddings[0].Vector.Skip(1)] }, .. embeddings.Skip(1)],
        "no-values" => [.. embeddings.Select(embedding => embedding with { Vector = [] })],
        _ => [.. embeddings.Select(embedding => embedding with { Vector = Values(KnowledgeLimits.MaxVectorDimensions + 1, 0.5f) })],
    };

    /// <summary>One composed runtime over a capturing store, a fake clock and a recording telemetry factory.</summary>
    private sealed class Rig : IDisposable
    {
        public Rig(ScriptedEmbeddingGenerator generator, string modelId = Model, int batchSize = 64, CapturingKnowledgeStore? store = null)
        {
            this.Generator = generator;
            var services = new ServiceCollection();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton<TimeProvider>(this.Time);
            services.AddSingleton<ITelemetryActivityFactory>(this.Telemetry);
            services.AddSingleton<IEmbeddingGenerator>(generator);
            if (store is not null)
            {
                services.AddSingleton(store);
            }

            services.AddHoneyDrunkKnowledge<CapturingKnowledgeStore>(options =>
            {
                options.EmbeddingModelId = modelId;
                options.EmbeddingBatchSize = batchSize;
            });
            this.Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            this.Store = this.Services.GetRequiredService<CapturingKnowledgeStore>();
        }

        public FakeTimeProvider Time { get; } = new(Start);

        public RecordingTelemetryActivityFactory Telemetry { get; } = new();

        public ScriptedEmbeddingGenerator Generator { get; }

        public ServiceProvider Services { get; }

        public CapturingKnowledgeStore Store { get; }

        public async Task<IngestResult> IngestAsync(KnowledgeDocument document)
        {
            using var scope = this.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IDocumentIngester>().IngestAsync(document);
        }

        public void Dispose() => this.Services.Dispose();
    }
}
