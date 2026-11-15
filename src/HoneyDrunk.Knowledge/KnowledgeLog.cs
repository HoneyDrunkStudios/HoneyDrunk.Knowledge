using Microsoft.Extensions.Logging;

namespace HoneyDrunk.Knowledge;

/// <summary>
/// Log messages. They carry ids, counts, the model id, the provider, outcomes and exception type names,
/// never document text, chunk text, a query or a vector (invariant 147).
/// </summary>
internal static partial class KnowledgeLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Knowledge source {SourceId} ingested under model {EmbeddingModelId}: {Outcome}, {ChunkCount} chunks")]
    public static partial void Ingested(ILogger logger, string sourceId, string embeddingModelId, string outcome, int chunkCount);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Knowledge ingest of source {SourceId} failed ({ExceptionType})")]
    public static partial void IngestFailed(ILogger logger, string sourceId, string exceptionType);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Knowledge store operation {Operation} failed in provider {Provider} ({ExceptionType})")]
    public static partial void StoreOperationFailed(ILogger logger, string operation, string provider, string exceptionType);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Knowledge retrieval over {SourceCount} sources under model {EmbeddingModelId}: {ResultCount} chunks, {MismatchedCount} mismatched sources")]
    public static partial void Retrieved(ILogger logger, int sourceCount, string embeddingModelId, int resultCount, int mismatchedCount);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Knowledge retrieval over {SourceCount} sources failed ({ExceptionType})")]
    public static partial void RetrieveFailed(ILogger logger, int sourceCount, string exceptionType);
}
