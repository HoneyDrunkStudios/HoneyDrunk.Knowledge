# HoneyDrunk.Knowledge

The runtime behind the knowledge contracts. It splits a document into passages, embeds them through the host's `IEmbeddingGenerator` and stores the version in the host's `IKnowledgeStore` provider.

## Registration

```csharp
services.AddHoneyDrunkKnowledge<MyKnowledgeStore>(options =>
{
    options.EmbeddingModelId = "the-model-your-generator-serves";
    options.EmbeddingBatchSize = 64;
});
```

The provider type is a singleton, added only if it is not registered already, so a provider's own registration can come before or after this call. `IKnowledgeStore` and `IDocumentIngester` are scoped. The host registers `IEmbeddingGenerator`, Kernel's `ITelemetryActivityFactory` and logging; `TimeProvider.System` is added unless the host registered a clock.

| Option | Rule |
|---|---|
| `EmbeddingModelId` | Required: the model the host's generator serves, 1 to 200 characters, not white space only, with no control character |
| `EmbeddingBatchSize` | 1 to 256 passages per generator call; default 64 |

The options are checked when the runtime first reads them (`OptionsValidationException`). Call `ValidateOnStart` on `AddOptions<KnowledgeOptions>()` to check them at start-up instead.

## Ingest

A version already current under the configured model is `Unchanged`, with no generator call. Otherwise the text is split into passages and embedded in batches. Each batch must return one embedding per passage, in order, for the passage sent, from the configured model, with vectors of 1 to 8,192 finite values, all of one length; otherwise the ingest throws `InvalidOperationException` and stores nothing. A new version is stamped with the clock's time and supersedes the current one.

## Chunking

Chunking takes plain text or Markdown, treated alike. Paragraphs are packed up to 1,200 characters; a longer paragraph is cut at the last space. A document holds at most 1,024 passages. HTML is not parsed here: pass text.

## Telemetry and logs

Ingest runs in a `knowledge.ingest` activity, and each store call in a `knowledge.store` activity. Activities and logs carry ids, counts, the model id, the provider's type name, the outcome and exception type names only, never document text, passage text, a query or a vector.
