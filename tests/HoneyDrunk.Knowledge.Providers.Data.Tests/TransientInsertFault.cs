using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace HoneyDrunk.Knowledge.Providers.Data.Tests;

/// <summary>
/// Fails every insert of a source version with a fault the paired <see cref="RetryStrategy"/> treats as transient,
/// as a database that keeps dropping the connection would. Entity Framework wraps each failure in a
/// <c>DbUpdateException</c> that holds the entities being saved, and the strategy gives up after its retries.
/// </summary>
internal sealed class TransientInsertFault : DbCommandInterceptor
{
    private int faults;

    /// <summary>Gets how many inserts of a source version were failed.</summary>
    public int Faults => Volatile.Read(ref this.faults);

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        this.FailVersionInsert(command);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        this.FailVersionInsert(command);
        return ValueTask.FromResult(result);
    }

    private void FailVersionInsert(DbCommand command)
    {
        if (command.CommandText.Contains("INSERT INTO \"SourceVersion\"", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref this.faults);
            throw new FaultException();
        }
    }

    /// <summary>Retries <see cref="FaultException"/> twice, the way a provider's retry-on-failure strategy retries a transient error.</summary>
    /// <param name="dependencies">The strategy's dependencies.</param>
    internal sealed class RetryStrategy(ExecutionStrategyDependencies dependencies) : ExecutionStrategy(dependencies, MaxRetries, TimeSpan.FromMilliseconds(10))
    {
        /// <summary>How many times the strategy retries before it gives up.</summary>
        public const int MaxRetries = 2;

        /// <inheritdoc />
        protected override bool ShouldRetryOn(Exception exception) => exception is FaultException;
    }

    /// <summary>The transient fault.</summary>
    internal sealed class FaultException : Exception
    {
        /// <summary>Initializes a new instance of the <see cref="FaultException"/> class.</summary>
        public FaultException()
            : base("the connection dropped")
        {
        }

        /// <summary>Initializes a new instance of the <see cref="FaultException"/> class.</summary>
        /// <param name="message">The message.</param>
        public FaultException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="FaultException"/> class.</summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        public FaultException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
