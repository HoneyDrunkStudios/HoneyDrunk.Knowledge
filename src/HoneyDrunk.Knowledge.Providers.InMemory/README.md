# HoneyDrunk.Knowledge.Providers.InMemory

A deterministic, in-memory `IKnowledgeStore` for tests and local development. State is held under one lock and lost with the process. Only the current version of a source is kept. Not for production composition.

Compose it behind the runtime:

```csharp
services.AddHoneyDrunkKnowledge<InMemoryKnowledgeStore>(options => options.EmbeddingModelId = "my-model");
```

It enforces the same store contract as the durable provider: `StoreVersionAsync` is atomic (`Added`, `Unchanged` or `Superseded`), two concurrent calls never leave two current versions, a search ranks only current versions embedded by the query's model with vectors of the query's length and lists every other named source in `MismatchedSourceIds`, and results are ordered by score, then source id, then position. One shared contract test suite runs against this store and `HoneyDrunk.Knowledge.Providers.Data`.

The package references `HoneyDrunk.Knowledge.Abstractions` only. It logs nothing and reads no clock.
