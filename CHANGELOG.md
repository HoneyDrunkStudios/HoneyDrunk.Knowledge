# Changelog

## [Unreleased]

## [0.1.0] - 2026-11-15

### Added

- `HoneyDrunk.Knowledge.Abstractions`: `IKnowledgeStore`, `IDocumentIngester` and `IRetrievalPipeline`, with `KnowledgeSource`, `KnowledgeDocument`, `KnowledgeChunk`, `IngestResult`, `RetrievalRequest`, `SimilarityQuery`, `RetrievedChunk`, `RetrievalResult` and `KnowledgeLimits` (ADR-0021). Every source records its embedding model, and every retrieved chunk carries its source and position.
- The contract-shape canary for the frozen Abstractions surfaces, run by the API compatibility workflow.
- The solution, `global.json` (.NET SDK 11.0.100) and the shared build properties. `HoneyDrunk.Knowledge.Abstractions` targets `net10.0` and `net11.0`.
- `HoneyDrunk.Knowledge`: the runtime. It splits plain text and Markdown into passages, embeds them through the host's `IEmbeddingGenerator`, stores versions through the host's `IKnowledgeStore` and retrieves the closest passages of the same embedding model. `AddHoneyDrunkKnowledge<TStore>` and `KnowledgeOptions`; `knowledge.ingest`, `knowledge.retrieve` and `knowledge.store` activities carrying metadata only.
- `HoneyDrunk.Knowledge.Providers.InMemory`: a deterministic in-memory store for tests and local development.
- `HoneyDrunk.Knowledge.Providers.Data`: a durable store over HoneyDrunk.Data's Entity Framework layer, with the SQL Server table scripts for the `knowledge` schema, one current version per source under concurrent writers, and a SQL Server test lane that is not a required check.
- The four packages are listed for release in the publish workflow (version 0.1.0).

### Changed

- The workflows build with .NET SDK 11.0.100.
