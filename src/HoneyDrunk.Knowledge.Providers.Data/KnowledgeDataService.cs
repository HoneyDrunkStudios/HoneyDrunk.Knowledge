using HoneyDrunk.Knowledge.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HoneyDrunk.Knowledge.Providers.Data;

/// <summary>
/// Reads and writes source versions and their chunks. Each call uses its own context from the factory, so the
/// service is safe as a singleton. Every read of a current version goes through the filtered unique index
/// (<c>SupersededAt IS NULL</c>). No exception that leaves this class carries an entity, a passage or a vector:
/// every write runs through <see cref="KnowledgeUnitOfWork"/>, which rebuilds a <see cref="DbUpdateException"/> found
/// at any depth of what it throws (<see cref="KnowledgeExceptions.Sanitize"/>), and a refusal this class rethrows
/// itself goes through <see cref="KnowledgeWriteAttempts.Sanitized"/>. Reads track no entity.
/// </summary>
/// <param name="contexts">Creates a context per call.</param>
internal sealed class KnowledgeDataService(IDbContextFactory<KnowledgeDbContext> contexts)
{
    private readonly IDbContextFactory<KnowledgeDbContext> contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));

    /// <summary>Reads the current version of a source.</summary>
    /// <param name="sourceId">The source id.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The current version, or <see langword="null"/> when the source has none.</returns>
    public async Task<KnowledgeSource?> GetCurrentAsync(string sourceId, CancellationToken cancellationToken)
    {
        await using var context = await this.contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await Current(context, sourceId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row?.ToSource();
    }

    /// <summary>
    /// Stores a version in one transaction: read the current version, then write nothing (<c>Unchanged</c>), or
    /// supersede it with a conditional update, delete its chunks and insert the new version with its chunks. A
    /// writer whose read was overtaken by another writer's commit loses at the database, either at the conditional
    /// update or at the filtered unique index; it then re-reads and decides again as the second writer, within the
    /// bounds of <see cref="KnowledgeWriteAttempts"/>. A call that gives up stores nothing.
    /// </summary>
    /// <param name="source">The version to store.</param>
    /// <param name="chunks">The checked chunks.</param>
    /// <param name="dimensions">The vector length every chunk shares.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The outcome.</returns>
    public async Task<IngestOutcome> StoreVersionAsync(KnowledgeSource source, IReadOnlyList<KnowledgeChunk> chunks, int dimensions, CancellationToken cancellationToken)
    {
        var write = new VersionWrite(source, [.. chunks.Select(chunk => chunk.Embedding.Value)], [.. chunks.Select(chunk => VectorCodec.Encode(chunk.Embedding.Vector))], dimensions);
        await using var context = await this.contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var attempts = new KnowledgeWriteAttempts();
        while (true)
        {
            try
            {
                return await KnowledgeUnitOfWork.RunAsync(context, token => WriteAsync(context, write, token), cancellationToken).ConfigureAwait(false);
            }
            catch (SupersedeLostException)
            {
                attempts.LostAtSupersede();
            }
            catch (DbUpdateException error)
            {
                // The insert failed: the attempts decide from what this writer read and what a re-read finds now.
                var rereadCurrentId = await RereadCurrentIdAsync(context, write, cancellationToken).ConfigureAwait(false);
                attempts.RefusedInsert(error, write.ReadCurrentId, rereadCurrentId);
            }
        }
    }

    /// <summary>
    /// Ranks the chunks of the current versions of the named sources. Sources of another embedding model or vector
    /// length are listed, never scored. Only each chunk's version, position and vector are streamed; a heap keeps
    /// the best <see cref="SimilarityQuery.Limit"/>, and text is then loaded for those chunks alone.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The ranked chunks and the mismatched sources.</returns>
    public async Task<RetrievalResult> SearchAsync(SimilarityQuery query, CancellationToken cancellationToken)
    {
        string[] sourceIds = [.. query.SourceIds];
        await using var context = await this.contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var named = await context.SourceVersions.AsNoTracking()
            .Where(row => sourceIds.Contains(row.SourceId) && row.SupersededAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var mismatched = KnowledgeStoreRules.OrderMismatched(named
            .Where(row => !KnowledgeStoreRules.CanScore(row.ToSource(), row.Dimensions, query))
            .Select(row => row.SourceId));
        var scorable = named
            .Where(row => KnowledgeStoreRules.CanScore(row.ToSource(), row.Dimensions, query))
            .ToDictionary(row => row.Id, row => row.ToSource(), StringComparer.Ordinal);
        if (scorable.Count == 0)
        {
            return new RetrievalResult([], mismatched);
        }

        var winners = await RankAsync(context, scorable, query, cancellationToken).ConfigureAwait(false);
        var chunks = await LoadTextAsync(context, winners, cancellationToken).ConfigureAwait(false);
        return new RetrievalResult(chunks, mismatched);
    }

    /// <summary>Deletes every version of a source; the database deletes their chunks.</summary>
    /// <param name="sourceId">The source id.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns><see langword="true"/> when any version was deleted.</returns>
    public async Task<bool> DeleteSourceAsync(string sourceId, CancellationToken cancellationToken)
    {
        await using var context = await this.contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var deleted = await KnowledgeUnitOfWork.RunStatementAsync(
            context,
            token => context.SourceVersions.Where(row => row.SourceId == sourceId).ExecuteDeleteAsync(token),
            cancellationToken).ConfigureAwait(false);
        return deleted > 0;
    }

    private static IQueryable<KnowledgeSourceVersionEntity> Current(KnowledgeDbContext context, string sourceId) =>
        context.SourceVersions.AsNoTracking().Where(row => row.SourceId == sourceId && row.SupersededAt == null);

    private static async Task<IngestOutcome> WriteAsync(KnowledgeDbContext context, VersionWrite write, CancellationToken cancellationToken)
    {
        var current = await Current(context, write.Source.SourceId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        write.ReadCurrentId = current?.Id;
        var outcome = KnowledgeStoreRules.Decide(current?.ToSource(), write.Source);
        if (outcome == IngestOutcome.Unchanged)
        {
            return outcome;
        }

        var createdAt = write.Source.LastUpdatedAt.ToUniversalTime();
        if (current is not null)
        {
            // Never before the superseded version's own creation, which a clock behind another host's could give.
            DateTimeOffset? supersededAt = createdAt < current.CreatedAt ? current.CreatedAt : createdAt;
            var claimed = await context.SourceVersions
                .Where(row => row.Id == current.Id && row.SupersededAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SupersededAt, supersededAt), cancellationToken)
                .ConfigureAwait(false);
            if (claimed != 1)
            {
                throw new SupersedeLostException();
            }

            await context.Chunks.Where(row => row.SourceVersionId == current.Id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        context.SourceVersions.Add(write.ToEntity(createdAt));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    /// <summary>
    /// Re-reads the id of the source's current version after a refused insert, outside the rolled-back transaction.
    /// Under SQL Server's default locking read committed it can miss the current row while another writer replaces
    /// it, which <see cref="KnowledgeWriteAttempts"/> takes into account.
    /// </summary>
    private static async Task<string?> RereadCurrentIdAsync(KnowledgeDbContext context, VersionWrite write, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        return await Current(context, write.Source.SourceId).Select(row => row.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Hit[]> RankAsync(KnowledgeDbContext context, Dictionary<string, KnowledgeSource> scorable, SimilarityQuery query, CancellationToken cancellationToken)
    {
        string[] versionIds = [.. scorable.Keys];
        float[] queryVector = [.. query.Query.Vector];
        var values = new float[queryVector.Length];
        var kept = new PriorityQueue<Hit, Hit>(query.Limit + 1, Hit.WorstFirst);
        var rows = context.Chunks.AsNoTracking()
            .Where(row => versionIds.Contains(row.SourceVersionId))
            .Select(row => new { row.SourceVersionId, row.Position, row.Vector })
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false);
        await foreach (var row in rows)
        {
            VectorCodec.Decode(row.Vector, values);
            var hit = new Hit(row.SourceVersionId, scorable[row.SourceVersionId], row.Position, KnowledgeStoreRules.Cosine(queryVector, values));
            if (kept.Count < query.Limit)
            {
                kept.Enqueue(hit, hit);
            }
            else
            {
                kept.EnqueueDequeue(hit, hit);
            }
        }

        return [.. kept.UnorderedItems.Select(item => item.Element).Order(Hit.BestFirst)];
    }

    private static async Task<RetrievedChunk[]> LoadTextAsync(KnowledgeDbContext context, Hit[] winners, CancellationToken cancellationToken)
    {
        var texts = new Dictionary<ChunkKey, string>();
        foreach (var version in winners.GroupBy(hit => hit.VersionId, StringComparer.Ordinal))
        {
            var versionId = version.Key;
            int[] positions = [.. version.Select(hit => hit.Position)];
            var rows = await context.Chunks.AsNoTracking()
                .Where(row => row.SourceVersionId == versionId && positions.Contains(row.Position))
                .Select(row => new { row.Position, row.Text })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in rows)
            {
                texts[new ChunkKey(versionId, row.Position)] = row.Text;
            }
        }

        // A version superseded while the search ran has lost its chunks: its winners are not returned.
        return winners
            .Where(hit => texts.ContainsKey(hit.Key))
            .Select(hit => new RetrievedChunk(hit.Source, hit.Position, texts[hit.Key], hit.Score))
            .ToArray();
    }

    /// <summary>Names one chunk: its version and its position.</summary>
    private readonly record struct ChunkKey(string VersionId, int Position);

    /// <summary>A scored chunk, without its text.</summary>
    private readonly record struct Hit(string VersionId, KnowledgeSource Source, int Position, double Score)
    {
        /// <summary>Orders hits as the result does: score descending, then source id, then position.</summary>
        public static readonly IComparer<Hit> BestFirst = Comparer<Hit>.Create(
            (left, right) => KnowledgeStoreRules.CompareRank(left.Score, left.Source.SourceId, left.Position, right.Score, right.Source.SourceId, right.Position));

        /// <summary>Orders hits worst first, so the heap's root is the hit to drop.</summary>
        public static readonly IComparer<Hit> WorstFirst = Comparer<Hit>.Create((left, right) => BestFirst.Compare(right, left));

        /// <summary>Gets the chunk the hit names.</summary>
        public ChunkKey Key => new(this.VersionId, this.Position);
    }

    /// <summary>One version to store, encoded once and written by each attempt.</summary>
    private sealed class VersionWrite(KnowledgeSource source, string[] texts, byte[][] vectors, int dimensions)
    {
        public KnowledgeSource Source { get; } = source;

        /// <summary>Gets or sets the id of the current version the latest attempt read, or null when there was none.</summary>
        public string? ReadCurrentId { get; set; }

        public KnowledgeSourceVersionEntity ToEntity(DateTimeOffset createdAt)
        {
            var version = new KnowledgeSourceVersionEntity
            {
                Id = Guid.CreateVersion7(this.Source.LastUpdatedAt).ToString("N"),
                SourceId = this.Source.SourceId,
                Origin = this.Source.Origin,
                Version = this.Source.Version,
                EmbeddingModelId = this.Source.EmbeddingModelId,
                Dimensions = dimensions,
                ChunkCount = texts.Length,
                CreatedAt = createdAt,
            };
            version.Chunks.AddRange(texts.Select((text, position) => new KnowledgeChunkEntity
            {
                SourceVersionId = version.Id,
                Position = position,
                Text = text,
                Vector = vectors[position],
                CreatedAt = createdAt,
            }));
            return version;
        }
    }

    /// <summary>The conditional supersede found the version it read already superseded. It never leaves this class.</summary>
    private sealed class SupersedeLostException : Exception
    {
        /// <summary>Initializes a new instance of the <see cref="SupersedeLostException"/> class.</summary>
        public SupersedeLostException()
            : base("Another writer superseded the version this write read.")
        {
        }

        /// <summary>Initializes a new instance of the <see cref="SupersedeLostException"/> class.</summary>
        /// <param name="message">The message.</param>
        public SupersedeLostException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="SupersedeLostException"/> class.</summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        public SupersedeLostException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
