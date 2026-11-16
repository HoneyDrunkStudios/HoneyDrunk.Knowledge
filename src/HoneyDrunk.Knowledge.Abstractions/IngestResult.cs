namespace HoneyDrunk.Knowledge.Abstractions;

/// <summary>The result of ingesting one document.</summary>
/// <param name="Source">The source as it now stands.</param>
/// <param name="Outcome">What storing the version did.</param>
/// <param name="ChunkCount">The number of chunks the ingest stored: 0 to 1,024.</param>
public sealed record IngestResult(KnowledgeSource Source, IngestOutcome Outcome, int ChunkCount)
{
    /// <summary>Gets the source.</summary>
    public KnowledgeSource Source { get; } = Source ?? throw new ArgumentNullException(nameof(Source));

    /// <summary>Gets the checked outcome.</summary>
    public IngestOutcome Outcome { get; } = KnowledgeValidation.Outcome(Outcome, nameof(Outcome));

    /// <summary>Gets the checked chunk count.</summary>
    public int ChunkCount { get; } = KnowledgeValidation.ChunkCount(ChunkCount, nameof(ChunkCount));
}
