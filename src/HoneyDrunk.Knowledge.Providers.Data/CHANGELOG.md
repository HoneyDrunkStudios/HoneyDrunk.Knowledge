# Changelog

## [Unreleased]

### Added

- `DataKnowledgeStore`: a durable `IKnowledgeStore` over HoneyDrunk.Data's Entity Framework layer, registered with `AddKnowledgeDataProvider`. It runs the store contract every provider shares, on SQLite.
- One current version per source, held by a filtered unique index. A writer that loses a race re-reads and answers as the second writer would.
- Writes run under the database provider's execution strategy; a retry after a lost commit reply finds the version stored and answers `Unchanged`.
- Search streams versions, positions and vectors only, keeps the best results in memory and loads text for those alone.
- A context configured with sensitive data logging is refused, so passages and vectors cannot reach logs through Entity Framework.

### Schema

- First version of the `knowledge` schema: `knowledge.SourceVersion` (`Id`, `SourceId`, `Origin`, `Version`, `EmbeddingModelId`, `Dimensions`, `ChunkCount`, `CreatedAt`, `SupersededAt`) and `knowledge.Chunk` (`SourceVersionId`, `Position`, `Text`, `Vector`, `CreatedAt`). A superseded version keeps its row and loses its chunks.
