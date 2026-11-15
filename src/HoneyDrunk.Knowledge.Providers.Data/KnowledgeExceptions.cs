using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace HoneyDrunk.Knowledge.Providers.Data;

/// <summary>
/// Keeps entities out of the exceptions the provider's writes let escape. Entity Framework attaches the entities it
/// was saving (passage text and vector bytes) to a <see cref="DbUpdateException"/>, and a retrying strategy or a
/// host component can wrap that exception at any depth. Every write path passes what it throws through
/// <see cref="Sanitize"/>, which rebuilds each such refusal without entities inside wrappers of the same types and
/// messages.
/// </summary>
internal static class KnowledgeExceptions
{
    /// <summary>Tells whether an exception, or any exception inside it at any depth, holds entities.</summary>
    /// <param name="error">The exception.</param>
    /// <returns><see langword="true"/> when a <see cref="DbUpdateException"/> with entries is found.</returns>
    public static bool HoldsEntities(Exception? error) => error switch
    {
        null => false,
        DbUpdateException { Entries.Count: > 0 } => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(HoldsEntities),
        _ => HoldsEntities(error.InnerException),
    };

    /// <summary>
    /// Returns the exception unchanged when nothing in it holds entities. Otherwise rebuilds the path down to each
    /// refusal that does: a <see cref="DbUpdateException"/> keeps its message and the database's own exception and
    /// loses its entries; an <see cref="AggregateException"/> keeps its inner exceptions, each sanitized; any other
    /// wrapper is made again as the same type with its message and the sanitized inner exception, or, when the type
    /// has no public (message, inner exception) constructor, as an <see cref="InvalidOperationException"/> with its
    /// message. A rebuilt exception has no stack trace of its own.
    /// </summary>
    /// <param name="error">The exception.</param>
    /// <returns>An exception that holds no entity at any depth.</returns>
    public static Exception Sanitize(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!HoldsEntities(error))
        {
            return error;
        }

        return error switch
        {
            DbUpdateConcurrencyException concurrency => new DbUpdateConcurrencyException(concurrency.Message, SanitizeInner(concurrency.InnerException)),
            DbUpdateException refused => KnowledgeWriteAttempts.Sanitized(refused),
            AggregateException aggregate => new AggregateException(aggregate.InnerExceptions.Select(Sanitize)),
            _ => Rewrap(error, Sanitize(error.InnerException!)),
        };
    }

    /// <summary>Sanitizes an inner exception, which may be absent.</summary>
    /// <param name="inner">The inner exception.</param>
    /// <returns>The sanitized inner exception, or <see langword="null"/>.</returns>
    public static Exception? SanitizeInner(Exception? inner) => inner is null ? null : Sanitize(inner);

    private static Exception Rewrap(Exception wrapper, Exception inner)
    {
        var constructor = wrapper.GetType().GetConstructor(BindingFlags.Public | BindingFlags.Instance, [typeof(string), typeof(Exception)]);
        return constructor?.Invoke([wrapper.Message, inner]) as Exception ?? new InvalidOperationException(wrapper.Message, inner);
    }
}
