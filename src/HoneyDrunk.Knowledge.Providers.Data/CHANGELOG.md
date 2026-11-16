# Changelog

## [0.1.0] - 2026-11-15

### Added

- `DataKnowledgeStore`: a durable `IKnowledgeStore` over HoneyDrunk.Data's Entity Framework layer, registered with `AddKnowledgeDataProvider`. It runs the store contract every provider shares, on SQLite.
- One current version per source, held by a filtered unique index. A writer that loses a race re-reads and decides again as the second writer would, at most 100 attempts in all; at that bound it stores nothing and throws `InvalidOperationException` naming the last refusal's exception type. A refusal where no current version was seen before or after is retried at most 5 times in a row, then surfaces as a `DbUpdateException` without entities.
- No exception from a failed write or delete carries an entity: a `DbUpdateException` at any depth (for example inside a retrying strategy's `RetryLimitExceededException`) is rebuilt without entities, inside wrappers of the same types and messages.
- Writes run under the database provider's execution strategy; a retry after a lost commit reply finds the version stored and answers `Unchanged`.
- Search reads versions, positions and vectors only, in pages of at most 256 rows (each its own query, so a retrying execution strategy buffers one page at most), keeps the best results in memory across the pages and loads text for those alone.
- A get or a delete of an id outside the source-id grammar answers `null` or `false` without a query, so SQL Server's comparison, which ignores trailing spaces even under the binary collation, never matches it to a different stored source.
- A context configured with sensitive data logging is refused, so passages and vectors cannot reach logs through Entity Framework.

### Schema

- First version of the `knowledge` schema: `knowledge.SourceVersion` (`Id`, `SourceId`, `Origin`, `Version`, `EmbeddingModelId`, `Dimensions`, `ChunkCount`, `CreatedAt`, `SupersededAt`) and `knowledge.Chunk` (`SourceVersionId`, `Position`, `Text`, `Vector`, `CreatedAt`). A superseded version keeps its row and loses its chunks.
- The SQL Server table scripts ship in the package's `sql/` folder (packed from `Sql/` in the source tree): `knowledge.sql`, `knowledge.SourceVersion.sql` and `knowledge.Chunk.sql`, with every named key, check, index and column description, binary collations on the compared text, and `datetimeoffset(7)` instants.
