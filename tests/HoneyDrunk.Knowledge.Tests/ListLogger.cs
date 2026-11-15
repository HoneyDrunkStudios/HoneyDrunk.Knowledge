using Microsoft.Extensions.Logging;

namespace HoneyDrunk.Knowledge.Tests;

/// <summary>
/// A logger factory whose every logger keeps each entry it is given: the level, the event id, the formatted
/// message, the structured values and any exception. Registered as <see cref="ILoggerFactory"/> with the
/// abstractions' <see cref="Logger{T}"/>, it captures what the runtime logs through <see cref="ILogger{T}"/>.
/// </summary>
internal sealed class ListLogger : ILoggerFactory, ILogger
{
    private readonly List<Entry> entries = [];

    /// <summary>Gets the entries logged, in order.</summary>
    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (this.entries)
            {
                return [.. this.entries];
            }
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => this;

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        var values = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
        var entry = new Entry(logLevel, eventId.Id, formatter(state, exception), [.. values], exception);
        lock (this.entries)
        {
            this.entries.Add(entry);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>One log entry.</summary>
    /// <param name="Level">The level.</param>
    /// <param name="EventId">The event id.</param>
    /// <param name="Message">The formatted message.</param>
    /// <param name="Values">The structured values, including the template under <c>{OriginalFormat}</c>.</param>
    /// <param name="Exception">The exception passed to the logger, if any.</param>
    internal sealed record Entry(LogLevel Level, int EventId, string Message, IReadOnlyList<KeyValuePair<string, object?>> Values, Exception? Exception);
}
