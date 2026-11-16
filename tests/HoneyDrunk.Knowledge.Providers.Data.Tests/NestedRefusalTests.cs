using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using Xunit;
using static HoneyDrunk.Knowledge.Providers.Contract.TestSources;

namespace HoneyDrunk.Knowledge.Providers.Data.Tests;

/// <summary>
/// A refusal that holds entities, nested deeper than one level inside what a write throws, leaves the store and the
/// delete path rebuilt without entities at every depth, inside wrappers of the same types and messages.
/// </summary>
public sealed class NestedRefusalTests : IDisposable
{
    private const string Wrapped = "wrapped by the test";

    private readonly SqliteDatabase database = new();

    /// <summary>
    /// A store whose retrying strategy gives up on <see cref="InvalidOperationException"/>s that wrap a refusal
    /// holding entities surfaces <see cref="RetryLimitExceededException"/>, then the
    /// <see cref="InvalidOperationException"/>, then a <see cref="DbUpdateException"/> with no entity, and stores
    /// nothing.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_refusal_two_levels_inside_a_retry_limit_carries_no_entity()
    {
        var captured = await CaptureRefusalWithEntitiesAsync();
        var refuse = new RefuseCommit(captured);
        var store = this.database.CreateStore(options =>
        {
            options.UseSqlite(this.database.ConnectionString, sqlite => sqlite.ExecutionStrategy(dependencies => new RefuseCommit.RetryStrategy(dependencies)));
            options.AddInterceptors(refuse);
        });

        var gaveUp = await Assert.ThrowsAsync<RetryLimitExceededException>(
            () => store.StoreVersionAsync(Source(), Chunks("refused passage", ModelA, [1f, 0f])));

        Assert.Equal(3, refuse.Refusals);
        var wrapper = Assert.IsType<InvalidOperationException>(gaveUp.InnerException);
        Assert.Equal(Wrapped, wrapper.Message);
        var refused = Assert.IsType<DbUpdateException>(wrapper.InnerException);
        Assert.Empty(refused.Entries);
        Assert.Equal(captured.Message, refused.Message);
        Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(refused.InnerException);
        Assert.DoesNotContain("captured passage", gaveUp.ToString(), StringComparison.Ordinal);
        Assert.Null(await store.GetSourceAsync("docs:intro"));
    }

    /// <summary>
    /// A delete that fails with an exception wrapping a refusal that holds entities surfaces the same wrapper, whose
    /// <see cref="DbUpdateException"/> carries no entity.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_refusal_inside_a_failed_delete_carries_no_entity()
    {
        var captured = await CaptureRefusalWithEntitiesAsync();
        var store = this.database.CreateStore(new RefuseDelete(captured));
        await this.database.CreateStore().StoreVersionAsync(Source(), Chunks("one", ModelA, [1f, 0f]));

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteSourceAsync("docs:intro"));

        Assert.Equal(Wrapped, failed.Message);
        var refused = Assert.IsType<DbUpdateException>(failed.InnerException);
        Assert.Empty(refused.Entries);
        Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(refused.InnerException);
        Assert.DoesNotContain("captured passage", failed.ToString(), StringComparison.Ordinal);
        Assert.Equal("v1", (await store.GetSourceAsync("docs:intro"))!.Version);
    }

    /// <summary>
    /// The sanitizer leaves an exception that holds no entity as it is; inside an <see cref="AggregateException"/>
    /// it sanitizes every inner exception; a wrapper type with no (message, inner exception) constructor becomes an
    /// <see cref="InvalidOperationException"/> with its message.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task The_sanitizer_covers_aggregates_and_wrappers_it_cannot_rebuild()
    {
        var captured = await CaptureRefusalWithEntitiesAsync();
        var clean = new InvalidOperationException(Wrapped, new TimeoutException());
        Assert.Same(clean, KnowledgeExceptions.Sanitize(clean));

        var aggregate = Assert.IsType<AggregateException>(KnowledgeExceptions.Sanitize(
            new AggregateException(new InvalidOperationException(Wrapped, captured), new TimeoutException("other"))));
        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Empty(Assert.IsType<DbUpdateException>(Assert.IsType<InvalidOperationException>(aggregate.InnerExceptions[0]).InnerException).Entries);
        Assert.IsType<TimeoutException>(aggregate.InnerExceptions[1]);

        var rebuilt = KnowledgeExceptions.Sanitize(new NoRebuildException(captured));
        Assert.IsType<InvalidOperationException>(rebuilt);
        Assert.Equal(NoRebuildException.Text, rebuilt.Message);
        Assert.Empty(Assert.IsType<DbUpdateException>(rebuilt.InnerException).Entries);
        Assert.False(KnowledgeExceptions.HoldsEntities(rebuilt));
    }

    /// <inheritdoc />
    public void Dispose() => this.database.Dispose();

    /// <summary>Makes a real refusal that holds entities, on a database of its own whose trigger refuses every version.</summary>
    private static async Task<DbUpdateException> CaptureRefusalWithEntitiesAsync()
    {
        using var other = new SqliteDatabase();
        await using var context = other.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER \"RefuseSourceVersion\" BEFORE INSERT ON \"SourceVersion\" BEGIN SELECT RAISE(ABORT, 'refused by the test'); END");
        var version = new KnowledgeSourceVersionEntity
        {
            Id = Guid.CreateVersion7(Start).ToString("N"),
            SourceId = "docs:captured",
            Origin = "https://example.test/guide",
            Version = "v1",
            EmbeddingModelId = ModelA,
            Dimensions = 2,
            ChunkCount = 1,
            CreatedAt = Start,
        };
        version.Chunks.Add(new KnowledgeChunkEntity
        {
            SourceVersionId = version.Id,
            Position = 0,
            Text = "captured passage",
            Vector = VectorCodec.Encode([1f, 0f]),
            CreatedAt = Start,
        });
        context.SourceVersions.Add(version);

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.NotEmpty(refused.Entries);
        return refused;
    }

    /// <summary>A wrapper whose only constructor takes the inner exception alone.</summary>
    /// <param name="inner">The inner exception.</param>
    private sealed class NoRebuildException(Exception inner) : Exception(Text, inner)
    {
        /// <summary>The message.</summary>
        public const string Text = "a wrapper the sanitizer cannot rebuild";
    }

    /// <summary>Refuses every commit with an <see cref="InvalidOperationException"/> that wraps a refusal holding entities.</summary>
    /// <param name="captured">The refusal to wrap.</param>
    private sealed class RefuseCommit(DbUpdateException captured) : DbTransactionInterceptor
    {
        private int refusals;

        /// <summary>Gets how many commits were refused.</summary>
        public int Refusals => Volatile.Read(ref this.refusals);

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref this.refusals);
            throw new InvalidOperationException(Wrapped, captured);
        }

        /// <summary>Retries the wrapped refusal twice, then gives up.</summary>
        /// <param name="dependencies">The strategy's dependencies.</param>
        internal sealed class RetryStrategy(ExecutionStrategyDependencies dependencies) : ExecutionStrategy(dependencies, 2, TimeSpan.FromMilliseconds(10))
        {
            /// <inheritdoc />
            protected override bool ShouldRetryOn(Exception exception) => exception is InvalidOperationException { Message: Wrapped };
        }
    }

    /// <summary>Fails every delete of source versions with an <see cref="InvalidOperationException"/> that wraps a refusal holding entities.</summary>
    /// <param name="captured">The refusal to wrap.</param>
    private sealed class RefuseDelete(DbUpdateException captured) : DbCommandInterceptor
    {
        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            command.CommandText.StartsWith("DELETE FROM \"SourceVersion\"", StringComparison.Ordinal)
                ? throw new InvalidOperationException(Wrapped, captured)
                : ValueTask.FromResult(result);
    }
}
