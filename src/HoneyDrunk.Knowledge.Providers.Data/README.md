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

## What it guarantees

- **One current version per source.** A unique index filtered to `SupersededAt IS NULL` allows one. Storing a version is one transaction: read the current version, then write nothing (`Unchanged`), or supersede it with a conditional update, delete its chunks and insert the new version with its chunks. A writer whose read was overtaken by another writer's commit loses at the database (at the conditional update or at the filtered index), re-reads once, and answers as the second writer would. Each loss means another writer stored a version; a call that loses 100 times in a row stores nothing and throws `InvalidOperationException`.
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
