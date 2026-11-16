using HoneyDrunk.Knowledge.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HoneyDrunk.Knowledge.Providers.Data;

/// <summary>
/// Ranks the chunks of the scorable versions a search found. Only each chunk's version, position and vector are
/// read, in pages ordered by the chunk key, each page its own query; a heap kept across the pages holds the best
/// <see cref="SimilarityQuery.Limit"/>, and text is then loaded for those chunks alone. A retrying execution
/// strategy makes Entity Framework buffer a query's whole result, so one page is the most it buffers.
/// </summary>
internal static class KnowledgeSearchDataService
{
    /// <summary>
    /// Ranks the chunks of <paramref name="scorable"/> and loads the winners' text. The versions are fixed when the
    /// search begins: a version superseded while it runs loses its chunks, so its unread chunks are not scored and
    /// its winners find no text and are dropped; the version that replaced it is not in <paramref name="scorable"/>
    /// and is never read.
    /// </summary>
    /// <param name="context">The search's context.</param>
    /// <param name="scorable">The source of each scorable version, by version id.</param>
    /// <param name="query">The query.</param>
    /// <param name="pageRows">The most chunk rows one page reads.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The ranked chunks, best first.</returns>
    public static async Task<RetrievedChunk[]> SearchAsync(
        KnowledgeDbContext context,
        Dictionary<string, KnowledgeSource> scorable,
        SimilarityQuery query,
        int pageRows,
        CancellationToken cancellationToken)
    {
        var winners = await RankAsync(context, scorable, query, pageRows, cancellationToken).ConfigureAwait(false);
        return await LoadTextAsync(context, winners, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Scores every chunk of the scorable versions, one page at a time. The page is handed straight to
    /// <see cref="Ranking.Add"/> and is not referenced across the next read, so one page is held at a time.
    /// </summary>
    private static async Task<Hit[]> RankAsync(KnowledgeDbContext context, Dictionary<string, KnowledgeSource> scorable, SimilarityQuery query, int pageRows, CancellationToken cancellationToken)
    {
        string[] versionIds = [.. scorable.Keys.Order(StringComparer.Ordinal)];
        var ranking = new Ranking(scorable, query);
        ChunkCursor? after = null;
        do
        {
            after = ranking.Add(await ReadPageAsync(context, versionIds, after, pageRows, cancellationToken).ConfigureAwait(false), pageRows);
        }
        while (after is not null);

        return ranking.Winners();
    }

    /// <summary>
    /// Reads one page of chunk vectors, ordered by version id and position (the order of <c>PK_Chunk</c>), after the
    /// cursor: the rest of the cursor's version, then the versions after it. Version ids are 32 lower-case hex
    /// digits (<c>CK_SourceVersion_Id_Format</c>) in a binary-collated column, so the database orders them in the
    /// ordinal order <paramref name="versionIds"/> is sorted in, and the page order and the list of later versions
    /// agree. Each predicate is a range of the primary key (the rest of one version) or whole versions of it.
    /// </summary>
    private static Task<List<ChunkVector>> ReadPageAsync(KnowledgeDbContext context, string[] versionIds, ChunkCursor? after, int pageRows, CancellationToken cancellationToken)
    {
        var chunks = context.Chunks.AsNoTracking();
        if (after is { } cursor)
        {
            var versionId = cursor.VersionId;
            var position = cursor.Position;
            string[] later = [.. versionIds.Where(id => string.CompareOrdinal(id, versionId) > 0)];
            chunks = chunks.Where(row => (row.SourceVersionId == versionId && row.Position > position) || later.Contains(row.SourceVersionId));
        }
        else
        {
            chunks = chunks.Where(row => versionIds.Contains(row.SourceVersionId));
        }

        return chunks
            .OrderBy(row => row.SourceVersionId)
            .ThenBy(row => row.Position)
            .Take(pageRows)
            .Select(row => new ChunkVector(row.SourceVersionId, row.Position, row.Vector))
            .ToListAsync(cancellationToken);
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

        // A version superseded while the search ran has lost its chunks: its winners are not returned. Text is read
        // by version id, never by source id, so a winner never takes the text of the version that replaced its own.
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

    /// <summary>The last chunk a search page read; the next page starts after it.</summary>
    private sealed record ChunkCursor(string VersionId, int Position);

    /// <summary>One chunk's version, position and stored vector, without its text.</summary>
    private sealed record ChunkVector(string VersionId, int Position, byte[] Vector);

    /// <summary>The best hits seen so far, kept across the pages of one search.</summary>
    private sealed class Ranking(Dictionary<string, KnowledgeSource> scorable, SimilarityQuery query)
    {
        private readonly float[] queryVector = [.. query.Query.Vector];
        private readonly float[] values = new float[query.Query.Vector.Count];
        private readonly PriorityQueue<Hit, Hit> kept = new(query.Limit + 1, Hit.WorstFirst);

        /// <summary>Scores one page and keeps the best <see cref="SimilarityQuery.Limit"/> hits.</summary>
        /// <param name="page">The page, in key order.</param>
        /// <param name="pageRows">The most rows a page reads.</param>
        /// <returns>The cursor of the next page, or <see langword="null"/> when this page was the last.</returns>
        public ChunkCursor? Add(List<ChunkVector> page, int pageRows)
        {
            foreach (var row in page)
            {
                VectorCodec.Decode(row.Vector, this.values);
                var hit = new Hit(row.VersionId, scorable[row.VersionId], row.Position, KnowledgeStoreRules.Cosine(this.queryVector, this.values));
                if (this.kept.Count < query.Limit)
                {
                    this.kept.Enqueue(hit, hit);
                }
                else
                {
                    this.kept.EnqueueDequeue(hit, hit);
                }
            }

            // A short page is the last one; a full page may be followed by more rows, or by an empty page.
            return page.Count < pageRows ? null : new ChunkCursor(page[^1].VersionId, page[^1].Position);
        }

        /// <summary>Gets the kept hits, best first.</summary>
        /// <returns>The hits.</returns>
        public Hit[] Winners() => [.. this.kept.UnorderedItems.Select(item => item.Element).Order(Hit.BestFirst)];
    }
}
