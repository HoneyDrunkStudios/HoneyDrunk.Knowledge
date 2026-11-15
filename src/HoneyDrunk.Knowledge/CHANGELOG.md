# Changelog

## [Unreleased]

### Added

- Document chunking: plain text and Markdown are cut into passages of at most 1,200 characters, at most 1,024 per document.
- `AddHoneyDrunkKnowledge<TStore>` and `KnowledgeOptions` (`EmbeddingModelId`, required; `EmbeddingBatchSize`, 1 to 256, default 64), checked on first use.
- Ingest through `IEmbeddingGenerator`: an unchanged version makes no generator call; every batch is checked for count, order, text, model and vector shape, and a failed batch stores nothing.
- Retrieval through `IEmbeddingGenerator`: the query is embedded once and checked for count, text, model and vector shape; the store ranks only passages of the same model and lists the other named sources as mismatched.
- `knowledge.ingest`, `knowledge.retrieve` and `knowledge.store` activities and log messages carrying metadata only. Only the caller's cancellation is `cancelled`; any other failure, a generator's or provider's own timeout included, is `error`.
