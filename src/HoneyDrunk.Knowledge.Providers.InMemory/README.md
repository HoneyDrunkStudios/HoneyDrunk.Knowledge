# HoneyDrunk.Knowledge.Providers.InMemory

A deterministic, in-memory `IKnowledgeStore` for tests and local development. State is held under one lock and lost with the process. Only the current version of a source is kept. Not for production composition.

Compose it behind the runtime:

```csharp
services.AddHoneyDrunkKnowledge<InMemoryKnowledgeStore>(options => options.EmbeddingModelId = "my-model");
```
