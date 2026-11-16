namespace HoneyDrunk.Knowledge.Abstractions;

/// <summary>
/// The passages a search ranked, and the named sources it could not score. Chunks come in score order,
/// highest first, then by <see cref="KnowledgeSource.SourceId"/> (ordinal), then by position.
/// </summary>
/// <param name="Chunks">The ranked passages. The record keeps a read-only copy.</param>
/// <param name="MismatchedSourceIds">
/// The named sources that exist but were embedded under another model, or with vectors of another
/// length, than the query: none of their passages is scored (invariant 146). At most 100 valid source
/// ids with no repeat, in ordinal order. The record keeps a read-only copy.
/// </param>
public sealed record RetrievalResult(IReadOnlyList<RetrievedChunk> Chunks, IReadOnlyList<string> MismatchedSourceIds)
{
    /// <summary>Gets a read-only copy of the ranked passages.</summary>
    public IReadOnlyList<RetrievedChunk> Chunks { get; } = KnowledgeValidation.Items(Chunks, nameof(Chunks));

    /// <summary>Gets a read-only copy of the mismatched source ids.</summary>
    public IReadOnlyList<string> MismatchedSourceIds { get; } = KnowledgeValidation.OptionalSourceIds(MismatchedSourceIds, nameof(MismatchedSourceIds));
}
