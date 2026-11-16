using HoneyDrunk.Knowledge.Abstractions;

namespace HoneyDrunk.Knowledge.Providers.Data;

/// <summary>
/// A durable <see cref="IKnowledgeStore"/> over HoneyDrunk.Data's Entity Framework layer. Source versions and their
/// passages survive a restart and are shared by every host that uses the same database. It gives the same answers
/// as the in-memory provider. It logs nothing and reads no clock: every instant comes from the source record.
/// </summary>
/// <remarks>
/// Register it with <c>AddKnowledgeDataProvider</c> and compose it behind the HoneyDrunk.Knowledge runtime with
/// <c>AddHoneyDrunkKnowledge&lt;DataKnowledgeStore&gt;</c>, in either order.
/// </remarks>
public sealed class DataKnowledgeStore : IKnowledgeStore
{
    private readonly KnowledgeDataService versions;

    /// <summary>Initializes a new instance of the <see cref="DataKnowledgeStore"/> class.</summary>
    /// <param name="versions">The data service.</param>
    internal DataKnowledgeStore(KnowledgeDataService versions)
    {
        this.versions = versions ?? throw new ArgumentNullException(nameof(versions));
    }

    /// <inheritdoc />
    public async Task<KnowledgeSource?> GetSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!FitsSourceId(sourceId))
        {
            return null;
        }

        return await this.versions.GetCurrentAsync(sourceId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IngestOutcome> StoreVersionAsync(KnowledgeSource source, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(chunks);
        cancellationToken.ThrowIfCancellationRequested();
        var dimensions = KnowledgeStoreRules.ValidateChunks(source, chunks);
        return await this.versions.StoreVersionAsync(source, chunks, dimensions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RetrievalResult> SearchAsync(SimilarityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return await this.versions.SearchAsync(query, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!FitsSourceId(sourceId))
        {
            return false;
        }

        return await this.versions.DeleteSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// An id outside the source-id grammar names no stored source, and is answered without a query: SQL Server
    /// compares strings with trailing spaces ignored, even under the binary collation, a longer id could match by
    /// truncation, and a non-ASCII id is converted to the column's code page. Only a well-formed id reaches the
    /// database.
    /// </summary>
    private static bool FitsSourceId(string sourceId) => KnowledgeStoreRules.IsSourceId(sourceId);
}
