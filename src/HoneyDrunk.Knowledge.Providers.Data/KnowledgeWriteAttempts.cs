using Microsoft.EntityFrameworkCore;

namespace HoneyDrunk.Knowledge.Providers.Data;

/// <summary>
/// Counts the attempts of one store call and decides, after each loss at the database, whether the call decides
/// again or gives up. A refused insert is a lost race unless the re-read finds the very version this writer read
/// still current: while that row is current, the filtered unique index lets no other writer in, so the refusal is
/// no race and is reported at once. Every message names the rule and exception type names only, never a value.
/// </summary>
internal sealed class KnowledgeWriteAttempts
{
    /// <summary>
    /// How many times one store call decides and writes before it gives up. Each attempt after the first follows a
    /// loss to another writer of the same source, so the bound is reached only when other writers keep winning.
    /// </summary>
    internal const int MaxWriteAttempts = 100;

    /// <summary>
    /// How many times in a row a store call decides again after a refused insert when neither its own read nor the
    /// re-read saw a current version. Under SQL Server's locking read committed a read can miss the current row
    /// while another writer replaces it, so that is how a lost race can look; it is also how a refusal that is no
    /// race looks on a new source, so it is retried only a few times before the refusal is reported. The run starts
    /// again after any loss in which a read saw a current version. When the refusal that ends a run is also the
    /// <see cref="MaxWriteAttempts"/>th attempt, the refusal (a <see cref="DbUpdateException"/>) is what surfaces.
    /// </summary>
    internal const int MaxBlindRetries = 5;

    private const string GaveUp =
        "The version could not be stored: on every attempt the database refused it while other writers appeared to be storing versions of the same source. Nothing was stored; the call can be repeated.";

    private const string SupersedeLost = "The last refusal was a conditional supersede that found the version it read already replaced.";

    private int attempts;
    private int blindRun;
    private string lastRefusal = SupersedeLost;

    /// <summary>
    /// Records a conditional supersede that found the version this writer read already replaced: another writer
    /// won. Returns to decide again, or throws once the call has made <see cref="MaxWriteAttempts"/> attempts.
    /// </summary>
    /// <exception cref="InvalidOperationException">The call reached <see cref="MaxWriteAttempts"/>.</exception>
    public void LostAtSupersede()
    {
        this.attempts++;
        this.blindRun = 0;
        this.lastRefusal = SupersedeLost;
        this.ThrowAtCap();
    }

    /// <summary>
    /// Records a refused insert and what the re-read found. Returns to decide again, or throws when the refusal is
    /// no race, when blind losses run past <see cref="MaxBlindRetries"/>, or at <see cref="MaxWriteAttempts"/>.
    /// </summary>
    /// <param name="error">The refusal.</param>
    /// <param name="readCurrentId">The current version id the attempt's own read saw, or null when it saw none.</param>
    /// <param name="rereadCurrentId">The current version id the re-read saw, or null when it saw none.</param>
    /// <exception cref="DbUpdateException">The refusal is no race; it carries no entity.</exception>
    /// <exception cref="InvalidOperationException">The call reached <see cref="MaxWriteAttempts"/>.</exception>
    public void RefusedInsert(DbUpdateException error, string? readCurrentId, string? rereadCurrentId)
    {
        ArgumentNullException.ThrowIfNull(error);
        this.attempts++;
        if (rereadCurrentId is not null && string.Equals(rereadCurrentId, readCurrentId, StringComparison.Ordinal))
        {
            throw Sanitized(error);
        }

        if (readCurrentId is null && rereadCurrentId is null)
        {
            this.blindRun++;
            if (this.blindRun > MaxBlindRetries)
            {
                throw Sanitized(error);
            }
        }
        else
        {
            this.blindRun = 0;
        }

        this.lastRefusal = "The last refusal's type was " + (error.InnerException ?? error).GetType().Name + ".";
        this.ThrowAtCap();
    }

    /// <summary>
    /// The refusal without the entities Entity Framework attached to it: the same message and the database's own
    /// exception (itself sanitized by <see cref="KnowledgeExceptions.Sanitize"/>), and no entity, so no passage or
    /// vector, in the exception a caller sees.
    /// </summary>
    /// <param name="error">The refusal.</param>
    /// <returns>A refusal that carries no entity.</returns>
    internal static DbUpdateException Sanitized(DbUpdateException error) => new(error.Message, KnowledgeExceptions.SanitizeInner(error.InnerException));

    private void ThrowAtCap()
    {
        if (this.attempts >= MaxWriteAttempts)
        {
            throw new InvalidOperationException($"{GaveUp} {this.lastRefusal}");
        }
    }
}
