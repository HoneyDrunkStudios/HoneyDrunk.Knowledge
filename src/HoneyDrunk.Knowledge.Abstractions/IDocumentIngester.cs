using HoneyDrunk.AI.Abstractions.Embeddings;

namespace HoneyDrunk.Knowledge.Abstractions;

/// <summary>
/// Splits a document into passages, embeds each through the host's <see cref="IEmbeddingGenerator"/> and
/// stores the result as the source's current version (ADR-0021 D5). Knowledge does not wrap the generator.
/// </summary>
public interface IDocumentIngester
{
    /// <summary>
    /// Ingests one version of a source. A document whose version and embedding model equal the current
    /// version's is <see cref="IngestOutcome.Unchanged"/>, with no embedding call.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The source as it now stands, what storing did and the number of chunks stored.</returns>
    Task<IngestResult> IngestAsync(KnowledgeDocument document, CancellationToken cancellationToken = default);
}
