# Changelog

## [0.1.0] - 2026-11-15

### Added

- `DataKnowledgeStore`: a durable `IKnowledgeStore` over HoneyDrunk.Data's Entity Framework layer, registered with `AddKnowledgeDataProvider`. It runs the store contract every provider shares, on SQLite.
- One current version per source, held by a filtered unique index. A writer that loses a race re-reads and decides again as the second writer would, at most 100 attempts in all; at that bound it stores nothing and throws `InvalidOperationException` naming the last refusal's exception type. A refusal where no current version was seen before or after is retried at most 5 times in a row, then surfaces as a `DbUpdateException` without entities.
- Writes run under the database provider's execution strategy; a retry after a lost commit reply finds the version stored and answers `Unchanged`.
- Search streams versions, positions and vectors only, keeps the best results in memory and loads text for those alone.
- A context configured with sensitive data logging is refused, so passages and vectors cannot reach logs through Entity Framework.

### Schema

- First version of the `knowledge` schema: `knowledge.SourceVersion` (`Id`, `SourceId`, `Origin`, `Version`, `EmbeddingModelId`, `Dimensions`, `ChunkCount`, `CreatedAt`, `SupersededAt`) and `knowledge.Chunk` (`SourceVersionId`, `Position`, `Text`, `Vector`, `CreatedAt`). A superseded version keeps its row and loses its chunks.
- The SQL Server table scripts ship in the package's `sql/` folder: `knowledge.sql`, `knowledge.SourceVersion.sql` and `knowledge.Chunk.sql`, with every named key, check, index and column description, binary collations on the compared text, and `datetimeoffset(7)` instants.
