using HoneyDrunk.AI.Abstractions.Embeddings;
using System.Globalization;

namespace HoneyDrunk.Knowledge.Abstractions;

/// <summary>
/// An embedded query for <see cref="IKnowledgeStore.SearchAsync"/>. <see cref="Embedding.Value"/> is the
/// query text, and <see cref="Embedding.ModelId"/> decides which sources may be scored (invariant 146).
/// </summary>
/// <param name="Query">
/// The query's embedding: text of 1 to 4,000 characters, not white space only, a vector of 1 to 8,192
/// finite values and a model id of 1 to 200 characters with no control character. The record keeps a
/// read-only copy of the vector.
/// </param>
/// <param name="SourceIds">The sources to search: 1 to 100 valid source ids with no repeat (ordinal). The record keeps a read-only copy.</param>
/// <param name="Limit">The most chunks to return: 1 to 50.</param>
public sealed record SimilarityQuery(Embedding Query, IReadOnlyList<string> SourceIds, int Limit)
{
    /// <summary>Gets the checked query embedding, holding a read-only copy of the vector.</summary>
    public Embedding Query { get; } = KnowledgeValidation.QueryEmbedding(Query, nameof(Query));

    /// <summary>Gets a read-only copy of the checked source ids.</summary>
    public IReadOnlyList<string> SourceIds { get; } = KnowledgeValidation.SourceIds(SourceIds, nameof(SourceIds));

    /// <summary>Gets the checked limit.</summary>
    public int Limit { get; } = KnowledgeValidation.Limit(Limit, nameof(Limit));

    /// <summary>
    /// Returns the query length, dimensions, model id, source count and limit, never the query or vector
    /// (invariant 147).
    /// </summary>
    /// <returns>The metadata text.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"SimilarityQuery {{ QueryLength = {this.Query.Value.Length}, Dimensions = {this.Query.Vector.Count}, EmbeddingModelId = {this.Query.ModelId}, SourceCount = {this.SourceIds.Count}, Limit = {this.Limit} }}");
}
