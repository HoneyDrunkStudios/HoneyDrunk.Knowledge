using HoneyDrunk.Knowledge.Abstractions;

namespace HoneyDrunk.Knowledge.Providers.InMemory;

/// <summary>
/// A deterministic, in-memory <see cref="IKnowledgeStore"/> for tests and local development. One lock guards
/// every operation, so a store is atomic and never holds two current versions of a source. Only the current
/// version is kept. Vectors are copied in, and a search returns text and scores, never a stored vector. Not for
/// production composition.
/// </summary>
public sealed class InMemoryKnowledgeStore : IKnowledgeStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, Stored> sources = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<KnowledgeSource?> GetSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (this.gate)
        {
            return Task.FromResult(this.sources.TryGetValue(sourceId, out var stored) ? stored.Source : null);
        }
    }

    /// <inheritdoc />
    public Task<IngestOutcome> StoreVersionAsync(KnowledgeSource source, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(chunks);
        cancellationToken.ThrowIfCancellationRequested();

        // Check and copy before taking the lock: a refused set stores nothing.
        KnowledgeStoreRules.ValidateChunks(source, chunks);
        var texts = new string[chunks.Count];
        var vectors = new float[chunks.Count][];
        for (var index = 0; index < chunks.Count; index++)
        {
            texts[index] = chunks[index].Embedding.Value;
            vectors[index] = [.. chunks[index].Embedding.Vector];
        }

        lock (this.gate)
        {
            this.sources.TryGetValue(source.SourceId, out var current);
            var outcome = KnowledgeStoreRules.Decide(current?.Source, source);
            if (outcome != IngestOutcome.Unchanged)
            {
                this.sources[source.SourceId] = new Stored(source, texts, vectors);
            }

            return Task.FromResult(outcome);
        }
    }

    /// <inheritdoc />
    public Task<RetrievalResult> SearchAsync(SimilarityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var queryVector = query.Query.Vector.ToArray();
        var hits = new List<Hit>();
        var mismatched = new List<string>();

        lock (this.gate)
        {
            var named = query.SourceIds
                .Where(sourceId => this.sources.ContainsKey(sourceId))
                .Select(sourceId => (sourceId: sourceId, stored: this.sources[sourceId]))
                .ToList();
            mismatched.AddRange(named
                .Where(entry => !KnowledgeStoreRules.CanScore(entry.stored.Source, entry.stored.Vectors[0].Length, query))
                .Select(entry => entry.sourceId));
            var scorable = named
                .Where(entry => KnowledgeStoreRules.CanScore(entry.stored.Source, entry.stored.Vectors[0].Length, query))
                .Select(entry => entry.stored);
            foreach (var stored in scorable)
            {
                for (var position = 0; position < stored.Vectors.Length; position++)
                {
                    var score = KnowledgeStoreRules.Cosine(queryVector, stored.Vectors[position]);
                    hits.Add(new Hit(stored.Source, position, stored.Texts[position], score));
                }
            }
        }

        hits.Sort((a, b) => KnowledgeStoreRules.CompareRank(a.Score, a.Source.SourceId, a.Position, b.Score, b.Source.SourceId, b.Position));
        var chunks = hits
            .Take(query.Limit)
            .Select(hit => new RetrievedChunk(hit.Source, hit.Position, hit.Text, hit.Score))
            .ToArray();
        return Task.FromResult(new RetrievalResult(chunks, KnowledgeStoreRules.OrderMismatched(mismatched)));
    }

    /// <inheritdoc />
    public Task<bool> DeleteSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (this.gate)
        {
            return Task.FromResult(this.sources.Remove(sourceId));
        }
    }

    private readonly record struct Hit(KnowledgeSource Source, int Position, string Text, double Score);

    private sealed record Stored(KnowledgeSource Source, string[] Texts, float[][] Vectors);
}
