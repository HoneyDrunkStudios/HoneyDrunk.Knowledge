# HoneyDrunk.Knowledge.Abstractions

Contracts for HoneyDrunk knowledge retrieval (ADR-0021). Downstream nodes compile against this package only.

- `IKnowledgeStore`: get a source's current version, store a version with its chunks in one atomic step, rank the chunks of current versions against an embedded query, delete a source.
- `IDocumentIngester`: splits a document into passages and embeds them through the host's `IEmbeddingGenerator`.
- `IRetrievalPipeline`: embeds a query through the host's `IEmbeddingGenerator` and ranks passages embedded by the same model.
- `KnowledgeSource`, `KnowledgeDocument`, `KnowledgeChunk`, `IngestOutcome`, `IngestResult`, `RetrievalRequest`, `SimilarityQuery`, `RetrievedChunk`, `RetrievalResult` and the bounds in `KnowledgeLimits`.

Every source records the embedding model that made its vectors, and every retrieved chunk carries its source and position. A search never scores a source embedded by another model: it lists it in `RetrievalResult.MismatchedSourceIds`.

`KnowledgeChunk` and `SimilarityQuery` carry the `Embedding` record of `HoneyDrunk.AI.Abstractions` (text, vector and model id together). The text of a document, chunk or query never appears in a record's `ToString()` or in a refusal message.

The only HoneyDrunk dependency is `HoneyDrunk.AI.Abstractions`, for `Embedding` and `IEmbeddingGenerator`.
