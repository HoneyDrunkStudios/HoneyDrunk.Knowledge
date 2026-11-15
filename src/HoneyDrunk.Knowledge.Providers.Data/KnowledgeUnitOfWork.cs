using Microsoft.EntityFrameworkCore;

namespace HoneyDrunk.Knowledge.Providers.Data;

/// <summary>
/// Runs a change in one database transaction inside <c>Database.CreateExecutionStrategy().ExecuteAsync</c>, so it
/// works with or without a retrying strategy. Each attempt starts with an empty change tracker and decides again
/// from what it reads, so an attempt retried after a lost commit reply finds the change already stored. The
/// transaction commits when the work returns and rolls back when it throws.
/// </summary>
internal static class KnowledgeUnitOfWork
{
    /// <summary>Runs <paramref name="work"/> in a transaction and commits it.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="context">The context.</param>
    /// <param name="work">The change; it reads what it needs and saves its own changes.</param>
    /// <param name="cancellationToken">Cancels the transaction; nothing is kept when it is cancelled before the commit.</param>
    /// <returns>The work's result.</returns>
    public static Task<T> RunAsync<T>(KnowledgeDbContext context, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(
            async token =>
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                var result = await work(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return result;
            },
            cancellationToken);
    }

    /// <summary>Runs one statement inside the execution strategy, without a transaction of its own.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="context">The context.</param>
    /// <param name="statement">The statement.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The statement's result.</returns>
    public static Task<T> RunStatementAsync<T>(KnowledgeDbContext context, Func<CancellationToken, Task<T>> statement, CancellationToken cancellationToken) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(statement, cancellationToken);
}
