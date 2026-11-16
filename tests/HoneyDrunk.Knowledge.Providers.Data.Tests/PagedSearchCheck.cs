using HoneyDrunk.Knowledge.Abstractions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Xunit;
using static HoneyDrunk.Knowledge.Providers.Contract.TestSources;

namespace HoneyDrunk.Knowledge.Providers.Data.Tests;

/// <summary>
/// Checks a search that reads its chunk vectors over several pages against a brute-force ranking and against the
/// one-page search, on whichever database the caller's stores use. The SQLite tests compile this file, and the
/// SQL Server tests link it.
/// </summary>
internal static class PagedSearchCheck
{
    /// <summary>The page sizes tried: one row, sizes that end a page inside and at the end of a version, and sizes around the 30 scorable rows.</summary>
    private static readonly int[] PageRows = [1, 4, 7, 10, 11, 29, 30, 31];

    private static readonly int[] Limits = [1, 5, 30, 50];

    /// <summary>
    /// Stores three scorable sources (30 chunks, with equal scores across sources and within one) and two
    /// mismatched ones, then searches with every page size and limit. Every paged result equals the brute-force
    /// ranking and the result of a search that reads every row in one page: same chunks, scores, order and
    /// mismatched sources.
    /// </summary>
    /// <param name="createStore">Creates a store over one shared database, reading the given rows per search page.</param>
    /// <returns>A task.</returns>
    public static async Task Paged_search_equals_brute_force(Func<int, IKnowledgeStore> createStore)
    {
        var writer = createStore(KnowledgeDataService.DefaultSearchPageRows);
        var random = new Random(11);
        float[] Next() => [.. Enumerable.Range(0, 3).Select(_ => (float)((random.NextDouble() * 2) - 1))];
        float[][] a = [.. Enumerable.Range(0, 11).Select(_ => Next())];
        float[][] b = [.. Enumerable.Range(0, 9).Select(_ => Next())];
        float[][] c = [.. Enumerable.Range(0, 10).Select(_ => Next())];

        // Equal scores across sources (b copies a's first three) and within one source (b's 5 copies its 4).
        b[0] = a[0];
        b[1] = a[1];
        b[2] = a[2];
        b[5] = b[4];
        c[9] = a[10];
        var stored = new Dictionary<string, float[][]>(StringComparer.Ordinal) { ["docs:a"] = a, ["docs:b"] = b, ["docs:c"] = c };
        var after = TimeSpan.Zero;
        foreach (var (sourceId, vectors) in stored)
        {
            after += TimeSpan.FromMinutes(1);
            await writer.StoreVersionAsync(Source(sourceId, updatedAfter: after), Chunks(sourceId, ModelA, vectors));
        }

        await writer.StoreVersionAsync(Source("docs:model-b", model: ModelB), Chunks("m", ModelB, [1f, 0f, 0f], [0f, 1f, 0f]));
        await writer.StoreVersionAsync(Source("docs:wide"), Chunks("w", ModelA, [1f, 0f, 0f, 0f]));
        var sources = new Dictionary<string, KnowledgeSource>(StringComparer.Ordinal);
        foreach (var sourceId in stored.Keys)
        {
            sources[sourceId] = (await writer.GetSourceAsync(sourceId))!;
        }

        string[] named = ["docs:wide", "docs:c", "docs:missing", "docs:a", "docs:model-b", "docs:b"];
        float[][] queries = [a[0], Next(), [0f, 0f, 1f]];
        var onePage = createStore(KnowledgeDataService.DefaultSearchPageRows);
        foreach (var queryVector in queries)
        {
            foreach (var limit in Limits)
            {
                var query = Query(queryVector, named, limit);
                var expected = BruteForce(stored, sources, queryVector, limit);
                var whole = await onePage.SearchAsync(query);
                Assert.Equal(expected, whole.Chunks);
                Assert.Equal(["docs:model-b", "docs:wide"], whole.MismatchedSourceIds);
                foreach (var rows in PageRows)
                {
                    var paged = await createStore(rows).SearchAsync(query);

                    Assert.Equal(expected, paged.Chunks);
                    Assert.Equal(whole.MismatchedSourceIds, paged.MismatchedSourceIds);
                }
            }
        }
    }

    /// <summary>
    /// A source superseded between two pages of a search contributes nothing: its chunks scored on the first page
    /// are dropped when their text is loaded (the text is read by version id, and the old version has none), and
    /// the new version's chunks are never read, because the search reads only the versions current when it began.
    /// No chunk comes back with the new version's text, and the next search finds the new version.
    /// </summary>
    /// <param name="createStore">Creates a store over one shared database, reading the given rows per search page with the given interceptors.</param>
    /// <returns>A task.</returns>
    public static async Task Supersede_between_pages_returns_no_text_of_the_new_version(Func<int, IInterceptor[], IKnowledgeStore> createStore)
    {
        var writer = createStore(KnowledgeDataService.DefaultSearchPageRows, []);

        // docs:a's version id is older, so its chunks fill the first page; they score highest.
        await writer.StoreVersionAsync(Source("docs:a"), Chunks("old a", ModelA, [1f, 0f], [1f, 0.1f], [1f, 0.2f]));
        await writer.StoreVersionAsync(Source("docs:b", updatedAfter: TimeSpan.FromMinutes(1)), Chunks("b", ModelA, [0.5f, 1f], [0f, 1f]));
        var superseding = new BeforeSecondPage(() => writer.StoreVersionAsync(
            Source("docs:a", version: "v2", updatedAfter: TimeSpan.FromMinutes(2)),
            Chunks("new a", ModelA, [1f, 0f], [1f, 0.1f], [1f, 0.2f])));

        var found = await createStore(2, [superseding]).SearchAsync(Query([1f, 0f], ["docs:a", "docs:b"]));

        Assert.True(superseding.Ran);
        Assert.Equal(["b 0", "b 1"], found.Chunks.Select(chunk => chunk.Text));
        Assert.All(found.Chunks, chunk => Assert.Equal("docs:b", chunk.Source.SourceId));
        var next = await writer.SearchAsync(Query([1f, 0f], ["docs:a"]));
        Assert.Equal(["new a 0", "new a 1", "new a 2"], next.Chunks.Select(chunk => chunk.Text));
        Assert.All(next.Chunks, chunk => Assert.Equal("v2", chunk.Source.Version));
    }

    private static RetrievedChunk[] BruteForce(Dictionary<string, float[][]> stored, Dictionary<string, KnowledgeSource> sources, float[] query, int limit) =>
        stored
            .SelectMany(source => source.Value.Select((vector, position) => new RetrievedChunk(sources[source.Key], position, $"{source.Key} {position}", Cosine(query, vector))))
            .OrderByDescending(chunk => chunk.Score)
            .ThenBy(chunk => chunk.Source.SourceId, StringComparer.Ordinal)
            .ThenBy(chunk => chunk.Position)
            .Take(limit)
            .ToArray();

    /// <summary>Cosine similarity as the contract defines it, computed the same way, so equal inputs give equal scores.</summary>
    private static double Cosine(float[] left, float[] right)
    {
        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (var index = 0; index < left.Length; index++)
        {
            double x = left[index];
            double y = right[index];
            dot += x * y;
            leftNorm += x * x;
            rightNorm += y * y;
        }

        if (leftNorm <= 0 || rightNorm <= 0)
        {
            return 0;
        }

        return Math.Clamp(dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm)), KnowledgeLimits.MinScore, KnowledgeLimits.MaxScore);
    }

    /// <summary>Runs an action once, just before the search's second page of vectors is read.</summary>
    private sealed class BeforeSecondPage(Func<Task> action) : DbCommandInterceptor
    {
        private int pages;

        public bool Ran { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("Vector", StringComparison.Ordinal) && Interlocked.Increment(ref this.pages) == 2)
            {
                await action();
                this.Ran = true;
            }

            return result;
        }
    }
}
