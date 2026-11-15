namespace HoneyDrunk.Knowledge.Abstractions;

/// <summary>
/// Stores versions of sources with their embedded chunks, and ranks the chunks of current versions
/// against an embedded query. A source has at most one current version; storing a new one supersedes
/// the old one and deletes its chunks. A cancelled token is observed before any work.
/// </summary>
public interface IKnowledgeStore
{
    /// <summary>Gets the current version of a source.</summary>
    /// <param name="sourceId">The source id.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The current version, or <see langword="null"/> when the source has none.</returns>
    Task<KnowledgeSource?> GetSourceAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a version and its chunks in one atomic step. With no current version the new one is
    /// stored (<see cref="IngestOutcome.Added"/>). With a current version of equal
    /// <see cref="KnowledgeSource.Version"/> and <see cref="KnowledgeSource.EmbeddingModelId"/> nothing
    /// is written (<see cref="IngestOutcome.Unchanged"/>). Otherwise the current version is superseded at
    /// <see cref="KnowledgeSource.LastUpdatedAt"/>, its chunks are deleted and the new one is stored
    /// (<see cref="IngestOutcome.Superseded"/>). Two concurrent calls never leave two current versions.
    /// </summary>
    /// <param name="source">The new version.</param>
    /// <param name="chunks">
    /// Its chunks: positions 0 to n-1 in order, at least 1 and at most 1,024, all with vectors of one
    /// length, each with a model id equal to <see cref="KnowledgeSource.EmbeddingModelId"/>.
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>What the call did.</returns>
    /// <exception cref="ArgumentException">The chunks break a rule above (the parameter is <c>chunks</c>).</exception>
    Task<IngestOutcome> StoreVersionAsync(KnowledgeSource source, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ranks the chunks of the current versions of the named sources whose embedding model equals the
    /// query's and whose vector length equals the query vector's. Every other named source that exists is
    /// listed as mismatched and never scored (invariant 146); unknown ids are ignored.
    /// </summary>
    /// <param name="query">The embedded query, the sources and the limit.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>At most <see cref="SimilarityQuery.Limit"/> chunks, and the mismatched source ids.</returns>
    Task<RetrievalResult> SearchAsync(SimilarityQuery query, CancellationToken cancellationToken = default);

    /// <summary>Removes every version and chunk of a source.</summary>
    /// <param name="sourceId">The source id.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns><see langword="true"/> when anything existed.</returns>
    Task<bool> DeleteSourceAsync(string sourceId, CancellationToken cancellationToken = default);
}
