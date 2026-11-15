# HoneyDrunk.Knowledge.Providers.Data

A durable `IKnowledgeStore` over HoneyDrunk.Data's Entity Framework layer. Source versions and their passages survive a restart and are shared by every host that uses the same database. It gives the same answers as `HoneyDrunk.Knowledge.Providers.InMemory`: one shared contract test suite runs against both.

The package holds no database driver. The host chooses the database:

```csharp
services.AddKnowledgeDataProvider(options => options.UseSqlServer(connection, sql => sql.EnableRetryOnFailure()));
services.AddHoneyDrunkKnowledge<DataKnowledgeStore>(options => options.EmbeddingModelId = "my-model");
```

The two calls work in either order. `IKnowledgeStore` still resolves to the runtime's wrapper; this package registers only the provider behind it, as a singleton. The context, the entities and the data service are internal, so passages leave the package only through `IKnowledgeStore`.

## The tables

Two tables in a schema of their own, `knowledge`.

| Table | One row is |
|---|---|
| `knowledge.SourceVersion` | One version of one source, as ingested under one embedding model |
| `knowledge.Chunk` | One retrievable passage of one version, with its embedding |

- A superseded version keeps its `SourceVersion` row (version, model, times, chunk count); its chunks are deleted in the same transaction, so old text cannot be recovered.
- A vector is stored as little-endian IEEE 754 single-precision floats, packed, and read back bit for bit.
- The package ships no migrations. `Database.EnsureCreated()` is for tests only.

### SQL Server table scripts

The package carries the SQL Server scripts in its `sql/` folder (nothing is added to your build): `knowledge.sql` (the schema), `knowledge.SourceVersion.sql` and `knowledge.Chunk.sql`. Copy the three files unchanged into your database project and deploy them in that order. Each table file holds every key, check, index and column description:

- `Id`, `SourceId`, `Version`, `EmbeddingModelId` and `Chunk.SourceVersionId` use the binary collation `Latin1_General_100_BIN2`, so they compare by exact value; `Origin` and `Text` use the database's own.
- Instants are `datetimeoffset(7)` in UTC. `SupersededAt` is never before `CreatedAt` (`CK_SourceVersion_SupersededAt`): when a superseding host's clock runs behind the one that stored the current version, the provider holds `SupersededAt` at that version's `CreatedAt`.
- The checks refuse what the contract refuses: an id that is not 32 lower-case hexadecimal digits, a source id outside `a-z`, `0-9`, `.`, `_`, `:`, `-`, an empty origin or passage, more than 8,192 dimensions or 1,024 chunks, a position outside 0 to 1,023, and a vector that is not 4 to 32,768 bytes in whole floats.
- `UQ_SourceVersion_SourceId_Current` (unique, filtered to `SupersededAt IS NULL`) allows one current version per source; `IX_SourceVersion_SourceId` serves deleting a source; `PK_Chunk` on `(SourceVersionId, Position)` serves the foreign key and every chunk read. `FK_Chunk_SourceVersion` cascades.

Departures from the usual table rules, each with its reason: `SourceVersion.Id` is text, not `uniqueidentifier`, so one mapping works on SQLite and SQL Server; `SourceId` names a source with no table of its own, because a source is the set of its versions; `Chunk` has no `Id`, because its rows are parts of one version and are only read through it; `SourceVersion` has no `ModifiedAt` or `RowVersion`, because its one update is a conditional statement guarded by the filtered unique index.

## What it guarantees

- **One current version per source.** A unique index filtered to `SupersededAt IS NULL` allows one. Storing a version is one transaction: read the current version, then write nothing (`Unchanged`), or supersede it with a conditional update, delete its chunks and insert the new version with its chunks. A writer whose read was overtaken by another writer's commit loses at the database (at the conditional update or at the filtered index), re-reads and decides again as the second writer would, at most 100 attempts in all; a call that reaches that bound stores nothing and throws `InvalidOperationException`. A refused insert counts as a loss unless the re-read finds the version this writer read still current: under SQL Server's default locking read committed a read can miss the current row while another writer replaces it, so a re-read that finds no current version is a loss too. A refusal that is no race is reported as a `DbUpdateException` at once when the version the writer read is still current, and otherwise at the latest at the bound, as the `InvalidOperationException`.
- **Contention on SQL Server.** The store is correct at the default isolation. Turning on `READ_COMMITTED_SNAPSHOT` for the database (Azure SQL Database has it on by default) lets reads see the last committed version instead of missing it, which reduces how often a writer has to decide again.
- **Retries.** Writes run inside the database provider's execution strategy. With a retrying strategy (for example `EnableRetryOnFailure`), a store whose commit succeeded but whose reply was lost is retried, finds the version stored and answers `Unchanged`. Without one, that error reaches the caller although the version is stored; storing it again answers `Unchanged`.
- **Bounded reads.** A search reads the current versions of the named sources, then streams only their chunks' version, position and vector, never their text. Scores are computed in .NET (no database has a vector type here), the best `Limit` are kept, and text is loaded for those alone.
- **No content in logs.** The package has no logger, starts no activity and reads no clock. A context configured with `EnableSensitiveDataLogging` is refused with `InvalidOperationException`, because Entity Framework would then write passages and vectors into logs. A `DbUpdateException` from a failed write carries no entities.

## Notes for hosts

- `AddKnowledgeDataProvider` registers the context through `AddHoneyDrunkDataEntityFramework`, with HoneyDrunk.Data's correlation interceptor off. It never takes HoneyDrunk.Data's non-generic `IUnitOfWork` or `IUnitOfWorkFactory`: those stay with the host's own context, whichever is registered first.
- Text that is compared (ids, versions, model ids) must compare by exact value. If you create the tables yourself, give those columns a binary collation.
- A search takes several statements. A version superseded while it runs contributes no passage, so a search can return fewer passages than its limit.
- Scoring streams every chunk of the named sources' current versions; it slows past roughly 100,000 chunks.
- Known limit: a delete that is retried after a lost reply reports what the retry removed, which can be nothing, although the rows are gone.

## Tests

`dotnet test HoneyDrunk.Knowledge.slnx -c Release` runs the contract on SQLite and needs no database.

The SQL Server tests are not in the solution. They deploy the three scripts to a new database on the server named by `HONEYDRUNK_KNOWLEDGE_TEST_SQLSERVER` (a connection string with no database), run the same contract and the schema checks, and drop the database afterwards:

```bash
sqllocaldb create KnowledgeProviderTests -s
HONEYDRUNK_KNOWLEDGE_TEST_SQLSERVER='Server=(localdb)\KnowledgeProviderTests;Integrated Security=true;Encrypt=false' dotnet test tests/HoneyDrunk.Knowledge.Providers.Data.SqlServerTests -c Release
```

The `SQL Server (Data provider)` workflow runs them on pull requests that touch the provider; it is not a required check.
