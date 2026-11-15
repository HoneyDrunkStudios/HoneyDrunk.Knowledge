# Changelog

## [0.1.0] - 2026-11-15

### Added

- `IKnowledgeStore`, `IDocumentIngester` and `IRetrievalPipeline` (ADR-0021). The ingester and the pipeline delegate embedding to `HoneyDrunk.AI.Abstractions`' `IEmbeddingGenerator`.
- `KnowledgeSource` (with `EmbeddingModelId`), `KnowledgeDocument`, `KnowledgeChunk`, `IngestOutcome`, `IngestResult`, `RetrievalRequest`, `SimilarityQuery`, `RetrievedChunk` and `RetrievalResult`. `KnowledgeChunk` and `SimilarityQuery` carry AI's `Embedding` record.
- `KnowledgeLimits`: the bounds each record checks in its constructor. A refusal names the parameter and never echoes the value.
- Record text (`ToString()`) carries lengths, counts and model ids only, never document, chunk or query text or a vector.
- The contract-shape canary for `IKnowledgeStore`, `IDocumentIngester`, `IRetrievalPipeline` and `KnowledgeSource`.
