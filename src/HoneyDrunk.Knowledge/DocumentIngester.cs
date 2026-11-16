using HoneyDrunk.AI.Abstractions.Embeddings;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Knowledge.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoneyDrunk.Knowledge;

/// <summary>
/// The runtime's <see cref="IDocumentIngester"/>. A version already current under the configured model is
/// <see cref="IngestOutcome.Unchanged"/> with no embedding call. Otherwise the text is split into passages,
/// each batch of passages is embedded through <see cref="IEmbeddingGenerator"/> and checked, and the version
/// is stored only when every batch passed (ADR-0021 D5). Each call runs in one <c>knowledge.ingest</c>
/// activity tagged with metadata only (invariant 147), a cancelled token included; only the caller's cancellation
/// is tagged <c>cancelled</c>. Exceptions from the generator and the store pass through unchanged; their messages
/// are theirs.
/// </summary>
/// <param name="store">The store.</param>
/// <param name="generator">The host's embedding generator.</param>
/// <param name="options">The runtime options, checked on first use.</param>
/// <param name="timeProvider">The clock that stamps a new version.</param>
/// <param name="telemetry">The activity factory.</param>
/// <param name="logger">The logger.</param>
internal sealed class DocumentIngester(
    IKnowledgeStore store,
    IEmbeddingGenerator generator,
    IOptions<KnowledgeOptions> options,
    TimeProvider timeProvider,
    ITelemetryActivityFactory telemetry,
    ILogger<DocumentIngester> logger) : IDocumentIngester
{
    private readonly IKnowledgeStore store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IEmbeddingGenerator generator = generator ?? throw new ArgumentNullException(nameof(generator));
    private readonly IOptions<KnowledgeOptions> options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ITelemetryActivityFactory telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    private readonly ILogger<DocumentIngester> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<IngestResult> IngestAsync(KnowledgeDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var activity = KnowledgeTelemetry.Start(this.telemetry, KnowledgeTelemetry.IngestActivity, KnowledgeTelemetry.IngestOperation, sourceId: document.SourceId);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var settings = this.options.Value;
            activity?.SetTag(KnowledgeTelemetry.EmbeddingModelId, settings.EmbeddingModelId);
            var result = await this.IngestCheckedAsync(document, settings, cancellationToken).ConfigureAwait(false);
            var outcome = KnowledgeTelemetry.OutcomeName(result.Outcome);
            activity?.SetTag(KnowledgeTelemetry.ChunkCount, result.ChunkCount);
            activity?.SetTag(KnowledgeTelemetry.Outcome, outcome);
            KnowledgeLog.Ingested(this.logger, document.SourceId, settings.EmbeddingModelId, outcome, result.ChunkCount);
            return result;
        }
        catch (Exception error) when (!KnowledgeTelemetry.IsCallerCancellation(error, cancellationToken))
        {
            activity?.SetTag(KnowledgeTelemetry.Outcome, KnowledgeTelemetry.Error);
            KnowledgeLog.IngestFailed(this.logger, document.SourceId, error.GetType().Name);
            throw;
        }
        catch (OperationCanceledException)
        {
            activity?.SetTag(KnowledgeTelemetry.Outcome, KnowledgeTelemetry.Cancelled);
            throw;
        }
    }

    private static void CheckBatch(IReadOnlyList<string> sent, IReadOnlyList<Embedding>? returned, string modelId, ref int dimensions)
    {
        if (returned is null || returned.Count != sent.Count || returned.Any(embedding => embedding is null))
        {
            throw new InvalidOperationException("The embedding generator must return exactly one embedding per passage sent.");
        }

        if (!returned.All(embedding => string.Equals(embedding.ModelId, modelId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Every embedding must come from the model named by KnowledgeOptions.EmbeddingModelId.");
        }

        if (!returned.Select((embedding, index) => string.Equals(embedding.Value, sent[index], StringComparison.Ordinal)).All(same => same))
        {
            throw new InvalidOperationException("Each embedding must be for the passage sent at its place, in order.");
        }

        // The first batch's first vector sets the length every vector of the version is held to; a missing or
        // empty first vector gives 0 and fails the check below.
        var expected = dimensions != 0 ? dimensions : returned[0].Vector?.Count ?? 0;
        if (!returned.All(embedding => IsVector(embedding.Vector, expected)))
        {
            throw new InvalidOperationException("Every vector must hold 1 to 8,192 finite values, all of one length.");
        }

        dimensions = expected;
    }

    // A vector of 1 to 8,192 finite values, of exactly the given length.
    private static bool IsVector(IReadOnlyList<float>? vector, int dimensions) =>
        vector is { Count: >= 1 and <= KnowledgeLimits.MaxVectorDimensions }
        && vector.Count == dimensions
        && vector.All(float.IsFinite);

    private async Task<IngestResult> IngestCheckedAsync(KnowledgeDocument document, KnowledgeOptions settings, CancellationToken cancellationToken)
    {
        var modelId = settings.EmbeddingModelId;
        var current = await this.store.GetSourceAsync(document.SourceId, cancellationToken).ConfigureAwait(false);
        if (current is not null
            && string.Equals(current.Version, document.Version, StringComparison.Ordinal)
            && string.Equals(current.EmbeddingModelId, modelId, StringComparison.Ordinal))
        {
            return new IngestResult(current, IngestOutcome.Unchanged, 0);
        }

        var passages = TextChunker.Split(document.Text);
        var chunks = await this.EmbedAsync(passages, modelId, settings.EmbeddingBatchSize, cancellationToken).ConfigureAwait(false);
        var source = new KnowledgeSource(document.SourceId, document.Origin, document.Version, this.timeProvider.GetUtcNow(), modelId);
        var outcome = await this.store.StoreVersionAsync(source, chunks, cancellationToken).ConfigureAwait(false);
        if (outcome == IngestOutcome.Unchanged)
        {
            // Another writer stored this version under this model after the check above: report it as it stands.
            var stored = await this.store.GetSourceAsync(document.SourceId, cancellationToken).ConfigureAwait(false);
            return new IngestResult(stored ?? source, IngestOutcome.Unchanged, 0);
        }

        return new IngestResult(source, outcome, chunks.Length);
    }

    private async Task<KnowledgeChunk[]> EmbedAsync(IReadOnlyList<string> passages, string modelId, int batchSize, CancellationToken cancellationToken)
    {
        var chunks = new KnowledgeChunk[passages.Count];
        var dimensions = 0;
        for (var start = 0; start < passages.Count; start += batchSize)
        {
            var sent = passages.Skip(start).Take(batchSize).ToArray();
            var returned = await this.generator.GenerateAsync(sent, cancellationToken: cancellationToken).ConfigureAwait(false);
            CheckBatch(sent, returned, modelId, ref dimensions);
            for (var index = 0; index < sent.Length; index++)
            {
                chunks[start + index] = new KnowledgeChunk(start + index, returned[index]);
            }
        }

        return chunks;
    }
}
