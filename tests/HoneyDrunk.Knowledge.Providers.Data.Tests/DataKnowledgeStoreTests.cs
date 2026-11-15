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

    /// <inheritdoc />
    public void Dispose() => this.database.Dispose();

    /// <inheritdoc />
    protected override IKnowledgeStore CreateStore() => this.database.CreateStore();

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
