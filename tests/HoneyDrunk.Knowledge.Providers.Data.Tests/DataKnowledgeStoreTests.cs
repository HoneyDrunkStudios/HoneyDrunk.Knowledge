using HoneyDrunk.Knowledge.Abstractions;
using HoneyDrunk.Knowledge.Providers.Contract;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Xunit;
using static HoneyDrunk.Knowledge.Providers.Contract.TestSources;

namespace HoneyDrunk.Knowledge.Providers.Data.Tests;

/// <summary>The store contract against the Data provider on a SQLite file, and how its search reads.</summary>
public sealed class DataKnowledgeStoreTests : KnowledgeStoreContract, IDisposable
{
    private readonly SqliteDatabase database = new();

    /// <summary>
    /// The search streams positions and vectors only, never text, and then loads the text of the winners alone:
    /// nine chunks are scored, two rows of text are read.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Search_loads_text_for_the_winners_only()
    {
        var log = new ReadLog();
        var store = this.database.CreateStore(log);
        await store.StoreVersionAsync(Source("docs:a"), Chunks("a", ModelA, [1f, 0f], [0f, 1f], [1f, 1f], [-1f, 0f], [0f, -1f]));
        await store.StoreVersionAsync(Source("docs:b"), Chunks("b", ModelA, [2f, 0f], [1f, 2f], [0f, 3f], [-1f, -1f]));
        log.Clear();

        var found = await store.SearchAsync(Query([1f, 0f], ["docs:a", "docs:b"], limit: 2));

        Assert.Equal(["a 0", "b 0"], found.Chunks.Select(chunk => chunk.Text));
        var reads = log.Reads;
        var streaming = Assert.Single(reads, read => read.Command.Contains("\"Vector\"", StringComparison.Ordinal));
        Assert.DoesNotContain("\"Text\"", streaming.Command, StringComparison.Ordinal);
        Assert.Equal(9, streaming.Rows);
        var texts = reads.Where(read => read.Command.Contains("\"Text\"", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(texts);
        Assert.All(texts, read => Assert.DoesNotContain("\"Vector\"", read.Command, StringComparison.Ordinal));
        Assert.Equal(2, texts.Sum(read => read.Rows));
        Assert.Equal(2, found.Chunks.Count);
    }

    /// <summary>
    /// The search reads vectors in pages of at most the set number of rows, each its own query: nine chunks in
    /// pages of four are three reads (4, 4, 1); in pages of three, a last full page is followed by one empty read.
    /// No page reads text, and the result equals the one-page search.
    /// </summary>
    /// <param name="pageRows">The most rows one page reads.</param>
    /// <param name="expectedRows">The rows each vector read returns, in order.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(4, new[] { 4, 4, 1 })]
    [InlineData(3, new[] { 3, 3, 3, 0 })]
    [InlineData(9, new[] { 9, 0 })]
    [InlineData(10, new[] { 9 })]
    public async Task Search_reads_vectors_in_pages_of_the_set_size(int pageRows, int[] expectedRows)
    {
        var writer = this.database.CreateStore();
        await writer.StoreVersionAsync(Source("docs:a"), Chunks("a", ModelA, [1f, 0f], [0f, 1f], [1f, 1f], [-1f, 0f], [0f, -1f]));
        await writer.StoreVersionAsync(Source("docs:b", updatedAfter: TimeSpan.FromMinutes(1)), Chunks("b", ModelA, [2f, 0f], [1f, 2f], [0f, 3f], [-1f, -1f]));
        var query = Query([1f, 0f], ["docs:a", "docs:b"], limit: 3);
        var log = new ReadLog();

        var paged = await this.database.CreatePagedStore(pageRows, log).SearchAsync(query);

        var pages = log.Reads.Where(read => read.Command.Contains("\"Vector\"", StringComparison.Ordinal)).ToArray();
        Assert.Equal(expectedRows, pages.Select(read => read.Rows));
        Assert.All(pages, read => Assert.DoesNotContain("\"Text\"", read.Command, StringComparison.Ordinal));
        Assert.Equal((await writer.SearchAsync(query)).Chunks, paged.Chunks);
        Assert.Equal(["a 0", "b 0", "a 2"], paged.Chunks.Select(chunk => chunk.Text));
    }

    /// <summary>A search read over several pages gives the brute-force ranking and the one-page result.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public Task Search_over_several_pages_equals_brute_force() =>
        PagedSearchCheck.Paged_search_equals_brute_force(rows => this.database.CreatePagedStore(rows));

    /// <summary>A source superseded between two search pages returns no passage, and none of the new version's text.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public Task Supersede_between_search_pages_returns_no_text_of_the_new_version() =>
        PagedSearchCheck.Supersede_between_pages_returns_no_text_of_the_new_version((rows, interceptors) => this.database.CreatePagedStore(rows, interceptors));

    /// <summary>A search that names only mismatched or unknown sources reads no chunk at all.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Search_with_nothing_to_score_reads_no_chunk()
    {
        var log = new ReadLog();
        var store = this.database.CreateStore(log);
        await store.StoreVersionAsync(Source("docs:b", model: ModelB), Chunks("b", ModelB, [1f, 0f]));
        log.Clear();

        var found = await store.SearchAsync(Query([1f, 0f], ["docs:b", "docs:missing"]));

        Assert.Empty(found.Chunks);
        Assert.Equal(["docs:b"], found.MismatchedSourceIds);
        Assert.DoesNotContain(log.Reads, read => read.Command.Contains("\"Chunk\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// A get or a delete of an id outside the source-id grammar is answered without sending any command to the
    /// database; a well-formed id is still looked up.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task An_id_outside_the_grammar_sends_no_command()
    {
        var commands = new CommandCount();
        var store = this.database.CreateStore(commands);
        await store.StoreVersionAsync(Source(), Chunks("a", ModelA, [1f, 0f]));
        commands.Reset();

        Assert.Null(await store.GetSourceAsync("docs:intro "));
        Assert.False(await store.DeleteSourceAsync("docs:intro "));
        Assert.Null(await store.GetSourceAsync(new string('a', 129)));
        Assert.False(await store.DeleteSourceAsync("DOCS:INTRO"));

        Assert.Equal(0, commands.Count);
        Assert.NotNull(await store.GetSourceAsync("docs:intro"));
        Assert.NotEqual(0, commands.Count);
    }

    /// <summary>The shared source-id check accepts exactly the ids a <see cref="KnowledgeSource"/> accepts.</summary>
    [Fact]
    public void The_store_id_check_matches_the_source_record()
    {
        var single = Enumerable.Range(0, 0x250).Select(code => ((char)code).ToString());
        var afterLetter = Enumerable.Range(0, 0x250).Select(code => "a" + (char)code);
        string[] lengths = [string.Empty, new string('a', 127), new string('a', 128), new string('a', 129), "docs:intro", "docs:intro ", " docs:intro"];

        foreach (var candidate in single.Concat(afterLetter).Concat(lengths))
        {
            Assert.Equal(AcceptedBySource(candidate), KnowledgeStoreRules.IsSourceId(candidate));
        }
    }

    /// <inheritdoc />
    public void Dispose() => this.database.Dispose();

    /// <inheritdoc />
    protected override IKnowledgeStore CreateStore() => this.database.CreateStore();

    private static bool AcceptedBySource(string sourceId)
    {
        try
        {
            _ = Source(sourceId);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Counts every command a context creates, of any kind.</summary>
    private sealed class CommandCount : DbCommandInterceptor
    {
        private int count;

        public int Count => Volatile.Read(ref this.count);

        public void Reset() => Interlocked.Exchange(ref this.count, 0);

        public override DbCommand CommandCreated(CommandEndEventData eventData, DbCommand result)
        {
            Interlocked.Increment(ref this.count);
            return result;
        }
    }

    /// <summary>Records each query a context runs and how many rows its reader returned.</summary>
    private sealed class ReadLog : DbCommandInterceptor
    {
        private readonly List<Read> reads = [];

        public IReadOnlyList<Read> Reads
        {
            get
            {
                lock (this.reads)
                {
                    return [.. this.reads];
                }
            }
        }

        public void Clear()
        {
            lock (this.reads)
            {
                this.reads.Clear();
            }
        }

        public override ValueTask<InterceptionResult> DataReaderClosingAsync(DbCommand command, DataReaderClosingEventData eventData, InterceptionResult result)
        {
            lock (this.reads)
            {
                this.reads.Add(new Read(command.CommandText, eventData.ReadCount - 1));
            }

            return ValueTask.FromResult(result);
        }

        public override InterceptionResult DataReaderClosing(DbCommand command, DataReaderClosingEventData eventData, InterceptionResult result)
        {
            lock (this.reads)
            {
                this.reads.Add(new Read(command.CommandText, eventData.ReadCount - 1));
            }

            return result;
        }
    }

    /// <summary>One query and the number of rows its reader returned.</summary>
    /// <param name="Command">The command text.</param>
    /// <param name="Rows">
    /// The rows the reader returned. Entity Framework counts every read call, the last one that finds no row
    /// included, and every query here reads to the end, so that last call is taken off.
    /// </param>
    private sealed record Read(string Command, int Rows);
}
