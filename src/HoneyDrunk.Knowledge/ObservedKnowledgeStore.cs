using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Knowledge.Abstractions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace HoneyDrunk.Knowledge;

/// <summary>
/// The runtime's <see cref="IKnowledgeStore"/>: it wraps the host's provider and runs each call in one
/// <c>knowledge.store</c> activity tagged with metadata only (invariant 147). A failure is logged by its
/// exception type name and rethrown unchanged.
/// </summary>
/// <param name="inner">The provider.</param>
/// <param name="providerName">The provider's type name, for telemetry and logs.</param>
/// <param name="telemetry">The activity factory.</param>
/// <param name="logger">The logger.</param>
internal sealed class ObservedKnowledgeStore(
    IKnowledgeStore inner,
    string providerName,
    ITelemetryActivityFactory telemetry,
    ILogger<ObservedKnowledgeStore> logger) : IKnowledgeStore
{
    private readonly IKnowledgeStore inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly string providerName = string.IsNullOrWhiteSpace(providerName) ? throw new ArgumentException("A provider name is required.", nameof(providerName)) : providerName;
    private readonly ITelemetryActivityFactory telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    private readonly ILogger<ObservedKnowledgeStore> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<KnowledgeSource?> GetSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        using var activity = this.Start(KnowledgeTelemetry.GetOperation, sourceId);
        var source = await this.RunAsync(activity, KnowledgeTelemetry.GetOperation, () => this.inner.GetSourceAsync(sourceId, cancellationToken)).ConfigureAwait(false);
        activity?.SetTag(KnowledgeTelemetry.ResultCount, source is null ? 0 : 1);
        if (source is not null)
        {
            activity?.SetTag(KnowledgeTelemetry.EmbeddingModelId, source.EmbeddingModelId);
        }

        activity?.SetTag(KnowledgeTelemetry.Outcome, KnowledgeTelemetry.Success);
        return source;
    }

    /// <inheritdoc />
    public async Task<IngestOutcome> StoreVersionAsync(KnowledgeSource source, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(chunks);
        using var activity = this.Start(KnowledgeTelemetry.StoreVersionOperation, source.SourceId);
        activity?.SetTag(KnowledgeTelemetry.EmbeddingModelId, source.EmbeddingModelId);
        activity?.SetTag(KnowledgeTelemetry.ChunkCount, chunks.Count);
        var outcome = await this.RunAsync(activity, KnowledgeTelemetry.StoreVersionOperation, () => this.inner.StoreVersionAsync(source, chunks, cancellationToken)).ConfigureAwait(false);
        activity?.SetTag(KnowledgeTelemetry.Outcome, KnowledgeTelemetry.OutcomeName(outcome));
        return outcome;
    }

    /// <inheritdoc />
    public async Task<RetrievalResult> SearchAsync(SimilarityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var activity = this.Start(KnowledgeTelemetry.SearchOperation);
        activity?.SetTag(KnowledgeTelemetry.SourceCount, query.SourceIds.Count);
        activity?.SetTag(KnowledgeTelemetry.EmbeddingModelId, query.Query.ModelId);
        var result = await this.RunAsync(activity, KnowledgeTelemetry.SearchOperation, () => this.inner.SearchAsync(query, cancellationToken)).ConfigureAwait(false);
        activity?.SetTag(KnowledgeTelemetry.ResultCount, result.Chunks.Count);
        activity?.SetTag(KnowledgeTelemetry.MismatchedCount, result.MismatchedSourceIds.Count);
        activity?.SetTag(KnowledgeTelemetry.Outcome, KnowledgeTelemetry.Success);
        return result;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        using var activity = this.Start(KnowledgeTelemetry.DeleteOperation, sourceId);
        var deleted = await this.RunAsync(activity, KnowledgeTelemetry.DeleteOperation, () => this.inner.DeleteSourceAsync(sourceId, cancellationToken)).ConfigureAwait(false);
        activity?.SetTag(KnowledgeTelemetry.ResultCount, deleted ? 1 : 0);
        activity?.SetTag(KnowledgeTelemetry.Outcome, KnowledgeTelemetry.Success);
        return deleted;
    }

    private Activity? Start(string operation, string? sourceId = null) =>
        KnowledgeTelemetry.Start(this.telemetry, KnowledgeTelemetry.StoreActivity, operation, this.providerName, sourceId);

    private async Task<T> RunAsync<T>(Activity? activity, string operation, Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            activity?.SetTag(KnowledgeTelemetry.Outcome, KnowledgeTelemetry.Cancelled);
            throw;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            activity?.SetTag(KnowledgeTelemetry.Outcome, KnowledgeTelemetry.Error);
            KnowledgeLog.StoreOperationFailed(this.logger, operation, this.providerName, error.GetType().Name);
            throw;
        }
    }
}
