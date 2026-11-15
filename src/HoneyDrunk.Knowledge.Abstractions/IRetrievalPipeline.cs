using HoneyDrunk.AI.Abstractions.Embeddings;

namespace HoneyDrunk.Knowledge.Abstractions;

/// <summary>
/// Embeds the query through the host's <see cref="IEmbeddingGenerator"/>, then ranks the stored passages
/// of the named sources that were embedded by the same model (ADR-0021 D5, D6). No score is ever computed
/// across models (invariant 146).
/// </summary>
public interface IRetrievalPipeline
{
    /// <summary>Retrieves the passages closest to a query.</summary>
    /// <param name="request">The query, the sources and the limit.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The ranked passages, each with its source and position, and the sources that could not be scored.</returns>
    Task<RetrievalResult> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default);
}
