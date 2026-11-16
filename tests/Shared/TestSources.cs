using HoneyDrunk.AI.Abstractions.Embeddings;
using HoneyDrunk.Knowledge.Abstractions;

namespace HoneyDrunk.Knowledge.Providers.Contract;

/// <summary>Neutral sources, chunks and queries with small hand-written vectors for the store contract.</summary>
public static class TestSources
{
    /// <summary>The embedding model the test chunks carry unless a test says otherwise.</summary>
    public const string ModelA = "model-a";

    /// <summary>Another embedding model.</summary>
    public const string ModelB = "model-b";

    /// <summary>The instant the test sources are dated.</summary>
    public static readonly DateTimeOffset Start = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Makes a source version.</summary>
    /// <param name="sourceId">The source id.</param>
    /// <param name="version">The version.</param>
    /// <param name="model">The embedding model id.</param>
    /// <param name="updatedAfter">How long after <see cref="Start"/> the version was ingested.</param>
    /// <returns>The source.</returns>
    public static KnowledgeSource Source(string sourceId = "docs:intro", string version = "v1", string model = ModelA, TimeSpan updatedAfter = default) =>
        new(sourceId, "https://example.test/guide", version, Start + updatedAfter, model);

    /// <summary>Makes one chunk.</summary>
    /// <param name="position">The position.</param>
    /// <param name="text">The text.</param>
    /// <param name="vector">The vector.</param>
    /// <param name="model">The embedding model id.</param>
    /// <returns>The chunk.</returns>
    public static KnowledgeChunk Chunk(int position, string text, float[] vector, string model = ModelA) =>
        new(position, new Embedding(text, vector, model));

    /// <summary>Makes chunks in order from vectors, with the text <c>{prefix} {position}</c>.</summary>
    /// <param name="prefix">The text prefix.</param>
    /// <param name="model">The embedding model id.</param>
    /// <param name="vectors">One vector per chunk.</param>
    /// <returns>The chunks.</returns>
    public static KnowledgeChunk[] Chunks(string prefix, string model, params float[][] vectors) =>
        [.. vectors.Select((vector, position) => Chunk(position, $"{prefix} {position}", vector, model))];

    /// <summary>Makes a query.</summary>
    /// <param name="vector">The query vector.</param>
    /// <param name="sourceIds">The sources to search.</param>
    /// <param name="limit">The limit.</param>
    /// <param name="model">The embedding model id.</param>
    /// <returns>The query.</returns>
    public static SimilarityQuery Query(float[] vector, string[] sourceIds, int limit = 10, string model = ModelA) =>
        new(new Embedding("query text", vector, model), sourceIds, limit);
}
