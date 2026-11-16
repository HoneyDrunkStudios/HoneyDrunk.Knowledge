using HoneyDrunk.Knowledge.Abstractions;
using HoneyDrunk.Knowledge.Providers.InMemory;

namespace HoneyDrunk.Knowledge.Tests;

/// <summary>An in-memory store that also keeps every set of chunks it was asked to store.</summary>
internal sealed class CapturingKnowledgeStore : IKnowledgeStore
{
    private readonly InMemoryKnowledgeStore inner = new();
    private readonly List<StoreCall> calls = [];

    /// <summary>Gets each store call, in order.</summary>
    public IReadOnlyList<StoreCall> StoreCalls
    {
        get
        {
            lock (this.calls)
            {
                return [.. this.calls];
            }
        }
    }

    /// <inheritdoc />
    public Task<KnowledgeSource?> GetSourceAsync(string sourceId, CancellationToken cancellationToken = default) =>
        this.inner.GetSourceAsync(sourceId, cancellationToken);

    /// <inheritdoc />
    public Task<IngestOutcome> StoreVersionAsync(KnowledgeSource source, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken = default)
    {
        lock (this.calls)
        {
            this.calls.Add(new StoreCall(source, [.. chunks]));
        }

        return this.inner.StoreVersionAsync(source, chunks, cancellationToken);
    }

    /// <inheritdoc />
    public Task<RetrievalResult> SearchAsync(SimilarityQuery query, CancellationToken cancellationToken = default) =>
        this.inner.SearchAsync(query, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteSourceAsync(string sourceId, CancellationToken cancellationToken = default) =>
        this.inner.DeleteSourceAsync(sourceId, cancellationToken);

    /// <summary>One call to <see cref="StoreVersionAsync"/>.</summary>
    /// <param name="Source">The source.</param>
    /// <param name="Chunks">The chunks.</param>
    internal sealed record StoreCall(KnowledgeSource Source, IReadOnlyList<KnowledgeChunk> Chunks);
}
