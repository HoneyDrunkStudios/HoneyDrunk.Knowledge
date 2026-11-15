# Changelog

## [Unreleased]

### Added

- `HoneyDrunk.Knowledge.Abstractions`: `IKnowledgeStore`, `IDocumentIngester` and `IRetrievalPipeline`, with `KnowledgeSource`, `KnowledgeDocument`, `KnowledgeChunk`, `IngestResult`, `RetrievalRequest`, `SimilarityQuery`, `RetrievedChunk`, `RetrievalResult` and `KnowledgeLimits` (ADR-0021). Every source records its embedding model, and every retrieved chunk carries its source and position.
- The contract-shape canary for the frozen Abstractions surfaces, run by the API compatibility workflow.
- The solution, `global.json` (.NET SDK 11.0.100) and the shared build properties. `HoneyDrunk.Knowledge.Abstractions` targets `net10.0` and `net11.0`.

### Changed

- The workflows build with .NET SDK 11.0.100.
