using HoneyDrunk.AI.Abstractions.Embeddings;
using HoneyDrunk.AI.Providers.InMemory;
using HoneyDrunk.Knowledge.Abstractions;
using HoneyDrunk.Knowledge.Providers.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Telemetry = HoneyDrunk.Kernel.Abstractions.Telemetry;

namespace HoneyDrunk.Knowledge.Canary;

/// <summary>
/// Checks Knowledge against the real HoneyDrunk.AI embedding contract and generator: a query is only ranked
/// against passages made by the same model, and the generator keeps the shape the ingester relies on.
/// </summary>
public sealed class AiCanaryTests
{
    private const string Origin = "https://example.test/guide";

    /// <summary>
    /// Two hosts over one store, one configured for <c>model-a</c> and one for <c>model-b</c>, each with a real
    /// in-memory generator of its own model: the second host gets no chunk from a source the first ingested, and
    /// the result names that source as mismatched instead of scoring it.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Retrieval_against_another_model_returns_no_chunk_from_it()
    {
        var store = new InMemoryKnowledgeStore();
        using var hostA = Compose("model-a", store);
        using var hostB = Compose("model-b", store);
        var document = new KnowledgeDocument("docs:intro", Origin, "v1", "Knowledge keeps passages of one model apart.\n\nA second passage about the same guide.");

        var ingested = await Run(hostA, services => services.GetRequiredService<IDocumentIngester>().IngestAsync(document));
        var fromA = await Run(hostA, services => services.GetRequiredService<IRetrievalPipeline>().RetrieveAsync(new RetrievalRequest("Knowledge keeps passages of one model apart.", ["docs:intro"])));
        var fromB = await Run(hostB, services => services.GetRequiredService<IRetrievalPipeline>().RetrieveAsync(new RetrievalRequest("Knowledge keeps passages of one model apart.", ["docs:intro"])));

        Assert.Equal("model-a", ingested.Source.EmbeddingModelId);
        Assert.NotEmpty(fromA.Chunks);
        Assert.Empty(fromA.MismatchedSourceIds);
        Assert.Empty(fromB.Chunks);
        Assert.Equal(["docs:intro"], fromB.MismatchedSourceIds);
    }

    /// <summary>
    /// The real generator returns one embedding for each value sent, in the order sent, holding the value as
    /// sent, the model it serves and a vector. The ingester relies on exactly this shape.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task The_generator_contract_keeps_one_embedding_per_value_in_order()
    {
        IEmbeddingGenerator generator = new InMemoryEmbeddingGenerator("model-a");
        string[] values = ["first passage", "second passage", "third passage", "first passage"];

        var embeddings = await generator.GenerateAsync(values);

        Assert.Equal(values.Length, embeddings.Count);
        for (var index = 0; index < values.Length; index++)
        {
            Assert.Equal(values[index], embeddings[index].Value);
            Assert.Equal("model-a", embeddings[index].ModelId);
            Assert.NotEmpty(embeddings[index].Vector);
        }

        Assert.Equal(embeddings[0].Vector, embeddings[3].Vector);
        Assert.NotEqual(embeddings[0].Vector, embeddings[1].Vector);
    }

    private static ServiceProvider Compose(string modelId, InMemoryKnowledgeStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<Telemetry.ITelemetryActivityFactory>(new NullTelemetryActivityFactory());
        services.AddSingleton<IEmbeddingGenerator>(new InMemoryEmbeddingGenerator(modelId));
        services.AddSingleton(store);
        services.AddHoneyDrunkKnowledge<InMemoryKnowledgeStore>(options => options.EmbeddingModelId = modelId);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static async Task<T> Run<T>(ServiceProvider host, Func<IServiceProvider, Task<T>> call)
    {
        await using var scope = host.CreateAsyncScope();
        return await call(scope.ServiceProvider);
    }
}
