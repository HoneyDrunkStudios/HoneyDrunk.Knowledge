using HoneyDrunk.Knowledge.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Xunit;
using static HoneyDrunk.Knowledge.Providers.Contract.TestSources;

namespace HoneyDrunk.Knowledge.Providers.Data.Tests;

/// <summary>Writers that race on one database, a writer that loses, and a commit whose reply is lost.</summary>
public sealed class RaceTests : IDisposable
{
    private readonly SqliteDatabase database = new();

    /// <summary>
    /// Two stores over one file write thirty versions each, in opposite orders, at once: exactly one version is
    /// current, only it has chunks (superseded versions keep their row, not their text), and every version written
    /// is a row.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Two_stores_on_one_database_racing_leave_one_current_version()
    {
        DataKnowledgeStore[] stores = [this.database.CreateStore(), this.database.CreateStore()];
        var versions = Enumerable.Range(0, 30).Select(index => $"v{index}").ToArray();

        var outcomes = await Task.WhenAll(stores.Select((store, index) => Task.Run(async () =>
        {
            var written = new List<IngestOutcome>();
            foreach (var version in index == 0 ? versions : versions.Reverse())
            {
                written.Add(await store.StoreVersionAsync(Source(version: version), Chunks($"text-{version}", ModelA, [1f, 0f], [0f, 1f])));
            }

            return written;
        })));

        var all = outcomes.SelectMany(list => list).ToArray();
        Assert.Equal(60, all.Length);
        Assert.Equal(1, all.Count(outcome => outcome == IngestOutcome.Added));
        await using var context = this.database.CreateContext();
        var rows = await context.SourceVersions.AsNoTracking().ToListAsync();
        var current = Assert.Single(rows, row => row.SupersededAt is null);
        Assert.Equal(all.Count(outcome => outcome != IngestOutcome.Unchanged), rows.Count);
        Assert.All(rows.Where(row => row.SupersededAt is not null), row => Assert.True(row.SupersededAt >= row.CreatedAt));
        Assert.Equal([current.Id], await context.Chunks.Select(row => row.SourceVersionId).Distinct().ToListAsync());
        var found = await stores[1].SearchAsync(Query([1f, 0f], ["docs:intro"]));
        Assert.Equal([$"text-{current.Version} 0", $"text-{current.Version} 1"], found.Chunks.Select(chunk => chunk.Text));
    }

    /// <summary>
    /// A writer whose read of the current version is overtaken by another writer's commit loses at the database
    /// (the filtered unique index, or the conditional supersede), re-reads, and answers as if it had come second.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_writer_that_loses_the_race_rereads_and_answers_as_the_second_writer()
    {
        var overtaken = new Overtaken();
        var store = this.database.CreateStore(overtaken);
        await this.database.CreateStore().StoreVersionAsync(Source(version: "v1"), Chunks("one", ModelA, [1f, 0f]));

        overtaken.Arm(Overtaken.CurrentReadSeesNothing);
        var same = await store.StoreVersionAsync(Source(version: "v1", updatedAfter: TimeSpan.FromMinutes(1)), Chunks("again", ModelA, [1f, 0f]));

        Assert.False(overtaken.Armed);
        Assert.Equal(IngestOutcome.Unchanged, same);
        Assert.Equal(["v1|current"], await this.LineageAsync());

        overtaken.Arm(Overtaken.CurrentReadSeesNothing);
        var next = await store.StoreVersionAsync(Source(version: "v2", updatedAfter: TimeSpan.FromMinutes(2)), Chunks("two", ModelA, [1f, 0f]));

        Assert.False(overtaken.Armed);
        Assert.Equal(IngestOutcome.Superseded, next);
        Assert.Equal([SupersededAfter("v1", 2), "v2|current"], await this.LineageAsync());

        overtaken.Arm(Overtaken.SupersedeFindsNothing);
        var third = await store.StoreVersionAsync(Source(version: "v3", updatedAfter: TimeSpan.FromMinutes(3)), Chunks("three", ModelA, [1f, 0f]));

        Assert.False(overtaken.Armed);
        Assert.Equal(IngestOutcome.Superseded, third);
        Assert.Equal([SupersededAfter("v1", 2), SupersededAfter("v2", 3), "v3|current"], await this.LineageAsync());
        var found = await store.SearchAsync(Query([1f, 0f], ["docs:intro"]));
        Assert.Equal(["three 0"], found.Chunks.Select(chunk => chunk.Text));
    }

    /// <summary>
    /// A commit whose reply is lost is retried by the host's retrying strategy; the retry finds the version stored
    /// and answers <see cref="IngestOutcome.Unchanged"/>, and the version is stored once.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_commit_whose_reply_is_lost_is_retried_and_answers_unchanged()
    {
        var lost = new LostCommitReply();
        var store = this.database.CreateStore(options =>
        {
            options.UseSqlite(this.database.ConnectionString, sqlite => sqlite.ExecutionStrategy(dependencies => new LostCommitReply.RetryStrategy(dependencies)));
            options.AddInterceptors(lost);
        });

        lost.Armed = true;
        var first = await store.StoreVersionAsync(Source(version: "v1"), Chunks("one", ModelA, [1f, 0f], [0f, 1f]));

        Assert.False(lost.Armed);
        Assert.Equal(IngestOutcome.Unchanged, first);
        Assert.Equal(["v1|current"], await this.LineageAsync());

        lost.Armed = true;
        var second = await store.StoreVersionAsync(Source(version: "v2", updatedAfter: TimeSpan.FromMinutes(1)), Chunks("two", ModelA, [1f, 0f]));

        Assert.False(lost.Armed);
        Assert.Equal(IngestOutcome.Unchanged, second);
        Assert.Equal([SupersededAfter("v1", 1), "v2|current"], await this.LineageAsync());
        await using var context = this.database.CreateContext();
        Assert.Equal(1, await context.Chunks.CountAsync());
    }

    /// <summary>Without a retrying strategy, a lost reply reaches the caller, and the version is stored once.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Without_retries_a_lost_reply_reaches_the_caller()
    {
        var lost = new LostCommitReply { Armed = true };
        var store = this.database.CreateStore(lost);

        await Assert.ThrowsAsync<LostCommitReply.LostReplyException>(() => store.StoreVersionAsync(Source(), Chunks("one", ModelA, [1f, 0f])));

        Assert.Equal(["v1|current"], await this.LineageAsync());
        Assert.Equal(IngestOutcome.Unchanged, await store.StoreVersionAsync(Source(), Chunks("one", ModelA, [1f, 0f])));
    }

    /// <inheritdoc />
    public void Dispose() => this.database.Dispose();

    private static string SupersededAfter(string version, int minutes) =>
        $"{version}|{(Start + TimeSpan.FromMinutes(minutes)).UtcTicks.ToString(CultureInfo.InvariantCulture)}";

    private async Task<string[]> LineageAsync()
    {
        await using var context = this.database.CreateContext();
        var rows = await context.SourceVersions.AsNoTracking().Where(row => row.SourceId == "docs:intro").ToListAsync();
        return rows
            .OrderBy(row => row.Version, StringComparer.Ordinal)
            .Select(row => $"{row.Version}|{(row.SupersededAt is { } at ? at.UtcTicks.ToString(CultureInfo.InvariantCulture) : "current")}")
            .ToArray();
    }

    /// <summary>
    /// Plays the moment another writer commits first, once: the next read of the current version inside a write
    /// sees none (the other writer's version is not yet visible to it), or the next conditional supersede finds
    /// the version it read already superseded.
    /// </summary>
    private sealed class Overtaken : DbCommandInterceptor
    {
        public const string CurrentReadSeesNothing = "read";

        public const string SupersedeFindsNothing = "supersede";

        private string? armed;

        public bool Armed => Volatile.Read(ref this.armed) is not null;

        public void Arm(string what) => Volatile.Write(ref this.armed, what);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.Transaction is null
                || !command.CommandText.Contains("FROM \"SourceVersion\"", StringComparison.Ordinal)
                || Interlocked.CompareExchange(ref this.armed, null, CurrentReadSeesNothing) is not CurrentReadSeesNothing)
            {
                return result;
            }

            await result.DisposeAsync();
            using var empty = new DataTable();
            return empty.CreateDataReader();
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"SourceVersion\"", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref this.armed, null, SupersedeFindsNothing) is SupersedeFindsNothing)
            {
                return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
            }

            return ValueTask.FromResult(result);
        }
    }
}
