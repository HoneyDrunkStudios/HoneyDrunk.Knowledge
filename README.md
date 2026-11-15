# HoneyDrunk.Knowledge

The knowledge node of the HoneyDrunk Grid (ADR-0021). It turns documents into retrievable passages: it splits a document, embeds the passages through the host's embedding generator, stores them as versioned sources, and answers a query with the closest stored passages, each with its source version and position.

Version 0.1.0. The node has no model of its own and no storage of its own: embedding comes from `HoneyDrunk.AI.Abstractions`' `IEmbeddingGenerator`, storage from a provider package the host chooses.

## Packages

| Package | What it is | HoneyDrunk dependencies |
|---|---|---|
| `HoneyDrunk.Knowledge.Abstractions` | The contracts: `IKnowledgeStore`, `IDocumentIngester`, `IRetrievalPipeline`, the records and `KnowledgeLimits`. Downstream nodes compile against this package only | `HoneyDrunk.AI.Abstractions` |
| `HoneyDrunk.Knowledge` | The runtime: chunking, ingest, retrieval, telemetry and DI registration | `HoneyDrunk.Knowledge.Abstractions`, `HoneyDrunk.Kernel.Abstractions`, `HoneyDrunk.AI.Abstractions` |
| `HoneyDrunk.Knowledge.Providers.InMemory` | A deterministic in-memory store for tests and local development. Not for production | `HoneyDrunk.Knowledge.Abstractions` |
| `HoneyDrunk.Knowledge.Providers.Data` | A durable store over HoneyDrunk.Data's Entity Framework layer, with the SQL Server table scripts. No database driver | `HoneyDrunk.Knowledge.Abstractions`, `HoneyDrunk.Data.EntityFramework` |

Providers reference the Abstractions package and never the runtime.

## Host composition

A host registers a store provider, the runtime, an embedding generator and Kernel's telemetry:

```csharp
services.AddHoneyDrunkNode(...).AddTelemetry();                    // Kernel: ITelemetryActivityFactory
services.AddSingleton<IEmbeddingGenerator>(...);                    // the host's generator
services.AddKnowledgeDataProvider(options => options.UseSqlServer(connection, sql => sql.EnableRetryOnFailure()));
services.AddHoneyDrunkKnowledge<DataKnowledgeStore>(options => options.EmbeddingModelId = "the-model-your-generator-serves");
```

`IDocumentIngester` and `IRetrievalPipeline` are scoped; resolve them from a scope. The runtime README lists the options and what each call checks; the Data provider README holds the tables, the SQL Server scripts and the concurrency behaviour.

A host that runs outside an HTTP request (a job or worker) needs two more things, because Kernel's default `IGridContextAccessor` reads the current request:

1. Replace the registration, do not add a second one: `services.Replace(ServiceDescriptor.Singleton<IGridContextAccessor, ScopedGridContextAccessor>())`.
2. Wrap each ingest and each retrieve in `using (ScopedGridContextAccessor.SetContext(...))`. The scoped accessor throws when no context is set.

## The embedding requirement

Every source records the embedding model that made its vectors, and a vector is only ever compared with a vector of the same model and length. The host sets `KnowledgeOptions.EmbeddingModelId` to the model its generator serves; the runtime refuses a generator that answers with another model. A search lists any named source embedded by another model in `RetrievalResult.MismatchedSourceIds` and scores nothing across models. Changing the model means ingesting the sources again: a version already current under the configured model is `Unchanged`, a version under another model is superseded.

No live embedding provider exists yet. Tests and local development use `HoneyDrunk.AI.Providers.InMemory`'s deterministic generator, which is not a language model; until a live provider ships, ranking quality with it says nothing about real retrieval.

## What the node does not do

- It never writes document text, passage text, a query or a vector to a log, an activity or an exception message it writes itself.
- It does not fetch documents, parse HTML or decide what to ingest: the host passes text.
- It ships no database migrations. The Data provider ships SQL Server table scripts for the host's database project.

## Build and test

```bash
dotnet test HoneyDrunk.Knowledge.slnx -c Release
dotnet pack HoneyDrunk.Knowledge.slnx -c Release -o artifacts
```

The SQL Server tests are outside the solution; see the Data provider README.
