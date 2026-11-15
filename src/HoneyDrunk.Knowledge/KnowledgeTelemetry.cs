using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Knowledge.Abstractions;
using System.Diagnostics;

namespace HoneyDrunk.Knowledge;

/// <summary>
/// Activity names, the tag allowlist and the outcomes. Only metadata crosses the telemetry boundary: ids,
/// counts, the model id, the provider and the outcome, never document text, chunk text, a query or a vector
/// (invariant 147). Latency is the activity's duration.
/// </summary>
internal static class KnowledgeTelemetry
{
    /// <summary>Activity for an ingest.</summary>
    public const string IngestActivity = "knowledge.ingest";

    /// <summary>Activity for a retrieval.</summary>
    public const string RetrieveActivity = "knowledge.retrieve";

    /// <summary>Activity for one store operation.</summary>
    public const string StoreActivity = "knowledge.store";

    /// <summary>The operation tag.</summary>
    public const string Operation = "knowledge.operation";

    /// <summary>The source id tag.</summary>
    public const string SourceId = "knowledge.source_id";

    /// <summary>The source count tag.</summary>
    public const string SourceCount = "knowledge.source_count";

    /// <summary>The chunk count tag.</summary>
    public const string ChunkCount = "knowledge.chunk_count";

    /// <summary>The result count tag.</summary>
    public const string ResultCount = "knowledge.result_count";

    /// <summary>The mismatched source count tag.</summary>
    public const string MismatchedCount = "knowledge.mismatched_count";

    /// <summary>The embedding model tag.</summary>
    public const string EmbeddingModelId = "knowledge.embedding_model_id";

    /// <summary>The provider tag: the store's type name.</summary>
    public const string Provider = "knowledge.provider";

    /// <summary>The outcome tag.</summary>
    public const string Outcome = "knowledge.outcome";

    /// <summary>The ingest operation.</summary>
    public const string IngestOperation = "ingest";

    /// <summary>The retrieve operation.</summary>
    public const string RetrieveOperation = "retrieve";

    /// <summary>The store's get operation.</summary>
    public const string GetOperation = "get";

    /// <summary>The store's store-version operation.</summary>
    public const string StoreVersionOperation = "store_version";

    /// <summary>The store's search operation.</summary>
    public const string SearchOperation = "search";

    /// <summary>The store's delete operation.</summary>
    public const string DeleteOperation = "delete";

    /// <summary>The success outcome.</summary>
    public const string Success = "success";

    /// <summary>The error outcome.</summary>
    public const string Error = "error";

    /// <summary>The cancelled outcome.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Returns the outcome tag value of an ingest outcome: <c>added</c>, <c>superseded</c> or <c>unchanged</c>.</summary>
    /// <param name="outcome">The ingest outcome.</param>
    /// <returns>The tag value.</returns>
    public static string OutcomeName(IngestOutcome outcome) => outcome switch
    {
        IngestOutcome.Added => "added",
        IngestOutcome.Superseded => "superseded",
        IngestOutcome.Unchanged => "unchanged",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), "The ingest outcome is not a defined value."),
    };

    /// <summary>Starts an activity carrying its operation and, when given, the provider and the source id.</summary>
    /// <param name="telemetry">The activity factory.</param>
    /// <param name="activityName">The activity name.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="provider">The provider's type name, if any.</param>
    /// <param name="sourceId">The source id, if the operation names one.</param>
    /// <returns>The activity, or <see langword="null"/> when not sampled.</returns>
    public static Activity? Start(ITelemetryActivityFactory telemetry, string activityName, string operation, string? provider = null, string? sourceId = null)
    {
        var tags = new Dictionary<string, object?>(StringComparer.Ordinal) { [Operation] = operation };
        if (provider is not null)
        {
            tags[Provider] = provider;
        }

        if (sourceId is not null)
        {
            tags[SourceId] = sourceId;
        }

        return telemetry.Start(activityName, tags);
    }
}
