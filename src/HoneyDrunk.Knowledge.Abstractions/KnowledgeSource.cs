namespace HoneyDrunk.Knowledge.Abstractions;

/// <summary>
/// The current version of one source, as a store holds it. Every retrieved chunk carries the source it
/// came from (invariant 145), and every source records the embedding model that made its vectors
/// (invariant 146).
/// </summary>
/// <param name="SourceId">
/// The host's id for the source: 1 to 128 characters of <c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>_</c>,
/// <c>:</c> or <c>-</c>, starting with <c>a-z</c> or <c>0-9</c>.
/// </param>
/// <param name="Origin">Where the host says the source comes from: 1 to 2,048 characters with no control character.</param>
/// <param name="Version">The host's version string: 1 to 128 printable ASCII characters with no space.</param>
/// <param name="LastUpdatedAt">When this version was ingested: at or after 1970-01-01T00:00:00Z.</param>
/// <param name="EmbeddingModelId">The model that made every vector of this version: 1 to 200 characters with no control character.</param>
public sealed record KnowledgeSource(string SourceId, string Origin, string Version, DateTimeOffset LastUpdatedAt, string EmbeddingModelId)
{
    /// <summary>Gets the checked source id.</summary>
    public string SourceId { get; } = KnowledgeValidation.SourceId(SourceId, nameof(SourceId));

    /// <summary>Gets the checked origin.</summary>
    public string Origin { get; } = KnowledgeValidation.Origin(Origin, nameof(Origin));

    /// <summary>Gets the checked version.</summary>
    public string Version { get; } = KnowledgeValidation.Version(Version, nameof(Version));

    /// <summary>
    /// Gets the checked ingest instant, at or after 1970-01-01T00:00:00Z. It keeps the <c>init</c> accessor the
    /// frozen shape records, and <c>with</c> is checked the same way as the constructor.
    /// </summary>
    public DateTimeOffset LastUpdatedAt
    {
        get;
        init => field = KnowledgeValidation.LastUpdatedAt(value, nameof(this.LastUpdatedAt));
    } = KnowledgeValidation.LastUpdatedAt(LastUpdatedAt, nameof(LastUpdatedAt));

    /// <summary>Gets the checked embedding model id.</summary>
    public string EmbeddingModelId { get; } = KnowledgeValidation.EmbeddingModelId(EmbeddingModelId, nameof(EmbeddingModelId));
}
