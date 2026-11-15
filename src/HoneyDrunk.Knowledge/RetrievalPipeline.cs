using HoneyDrunk.AI.Abstractions.Embeddings;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Knowledge.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoneyDrunk.Knowledge;

/// <summary>
/// The runtime's <see cref="IRetrievalPipeline"/>. The query text is embedded once through
/// <see cref="IEmbeddingGenerator"/>; the embedding must be one, for the query sent, from the configured model,
/// with a vector of 1 to 8,192 finite values, or the retrieval throws <see cref="InvalidOperationException"/> and the
/// store is never searched. The store then ranks only passages embedded by that model and lists the other named
/// sources as mismatched (ADR-0021 D5, D6; invariant 146). Each call runs in one <c>knowledge.retrieve</c>
/// activity tagged with metadata only (invariant 147). Exceptions from the generator and the store pass through
/// unchanged; their messages are theirs.
/// </summary>
/// <param name="store">The store.</param>
/// <param name="generator">The host's embedding generator.</param>
/// <param name="options">The runtime options, checked on first use.</param>
/// <param name="telemetry">The activity factory.</param>
/// <param name="logger">The logger.</param>
internal sealed class RetrievalPipeline(
    IKnowledgeStore store,
    IEmbeddingGenerator generator,
    IOptions<KnowledgeOptions> options,
    ITelemetryActivityFactory telemetry,
    ILogger<RetrievalPipeline> logger) : IRetrievalPipeline
{
    private readonly IKnowledgeStore store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IEmbeddingGenerator generator = generator ?? throw new ArgumentNullException(nameof(generator));
    private readonly IOptions<KnowledgeOptions> options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ITelemetryActivityFactory telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    private readonly ILogger<RetrievalPipeline> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<RetrievalResult> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var activity = KnowledgeTelemetry.Start(this.telemetry, KnowledgeTelemetry.RetrieveActivity, KnowledgeTelemetry.RetrieveOperation);
        activity?.SetTag(KnowledgeTelemetry.SourceCount, request.SourceIds.Count);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var modelId = this.options.Value.EmbeddingModelId;
            activity?.SetTag(KnowledgeTelemetry.EmbeddingModelId, modelId);
            var returned = await this.generator.GenerateAsync([request.QueryText], cancellationToken: cancellationToken).ConfigureAwait(false);
            var embedding = CheckQuery(request.QueryText, returned, modelId);
            var result = await this.store.SearchAsync(new SimilarityQuery(embedding, request.SourceIds, request.Limit), cancellationToken).ConfigureAwait(false);
            activity?.SetTag(KnowledgeTelemetry.ResultCount, result.Chunks.Count);
            activity?.SetTag(KnowledgeTelemetry.MismatchedCount, result.MismatchedSourceIds.Count);
            activity?.SetTag(KnowledgeTelemetry.Outcome, KnowledgeTelemetry.Success);
            KnowledgeLog.Retrieved(this.logger, request.SourceIds.Count, modelId, result.Chunks.Count, result.MismatchedSourceIds.Count);
            return result;
        }
        catch (Exception error) when (!KnowledgeTelemetry.IsCallerCancellation(error, cancellationToken))
        {
            activity?.SetTag(KnowledgeTelemetry.Outcome, KnowledgeTelemetry.Error);
            KnowledgeLog.RetrieveFailed(this.logger, request.SourceIds.Count, error.GetType().Name);
            throw;
        }
        catch (OperationCanceledException)
        {
            activity?.SetTag(KnowledgeTelemetry.Outcome, KnowledgeTelemetry.Cancelled);
            throw;
        }
    }

    private static Embedding CheckQuery(string queryText, IReadOnlyList<Embedding>? returned, string modelId)
    {
        if (returned is not { Count: 1 } || returned[0] is not { } embedding)
        {
            throw new InvalidOperationException("The embedding generator must return exactly one embedding for the query.");
        }

        if (!string.Equals(embedding.ModelId, modelId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The query embedding must come from the model named by KnowledgeOptions.EmbeddingModelId.");
        }

        if (!string.Equals(embedding.Value, queryText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The query embedding must be for the query text sent.");
        }

        if (embedding.Vector is not { Count: >= 1 and <= KnowledgeLimits.MaxVectorDimensions } vector || !vector.All(float.IsFinite))
        {
            throw new InvalidOperationException("The query vector must hold 1 to 8,192 finite values.");
        }

        return embedding;
    }
}
