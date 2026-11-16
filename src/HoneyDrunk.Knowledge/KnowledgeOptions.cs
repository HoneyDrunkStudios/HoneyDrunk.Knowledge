using HoneyDrunk.Knowledge.Abstractions;

namespace HoneyDrunk.Knowledge;

/// <summary>
/// Settings for the knowledge runtime. They are checked when the runtime first reads them; a host may also
/// call <c>ValidateOnStart</c> on <c>AddOptions&lt;KnowledgeOptions&gt;()</c> to check them at start-up.
/// </summary>
public sealed class KnowledgeOptions
{
    /// <summary>The fewest passages sent to the embedding generator in one call.</summary>
    internal const int MinEmbeddingBatchSize = 1;

    /// <summary>The most passages sent to the embedding generator in one call.</summary>
    internal const int MaxEmbeddingBatchSize = 256;

    /// <summary>The failure message for an invalid <see cref="EmbeddingModelId"/>. It never holds the value.</summary>
    internal const string EmbeddingModelIdRule =
        "KnowledgeOptions.EmbeddingModelId is required: 1 to 200 characters, not white space only, with no control character.";

    /// <summary>The failure message for an invalid <see cref="EmbeddingBatchSize"/>. It never holds the value.</summary>
    internal const string EmbeddingBatchSizeRule = "KnowledgeOptions.EmbeddingBatchSize must be 1 to 256.";

    /// <summary>
    /// Gets or sets the id of the model the host's embedding generator serves: 1 to 200 characters, not white
    /// space only, with no control character. Required. The generator names its model only in the embeddings
    /// it returns, so this id lets an ingest find a version already current under the same model without a
    /// call, and every returned embedding must carry it or the ingest stores nothing.
    /// </summary>
    public string EmbeddingModelId { get; set; } = string.Empty;

    /// <summary>Gets or sets the most passages sent to the embedding generator in one call: 1 to 256. The default is 64.</summary>
    public int EmbeddingBatchSize { get; set; } = 64;

    /// <summary>Checks <see cref="EmbeddingModelId"/>.</summary>
    /// <param name="options">The options.</param>
    /// <returns>Whether the model id is valid.</returns>
    internal static bool HasValidModelId(KnowledgeOptions options) =>
        options.EmbeddingModelId is { Length: >= 1 and <= KnowledgeLimits.MaxEmbeddingModelIdLength } modelId
        && !string.IsNullOrWhiteSpace(modelId)
        && !modelId.Any(char.IsControl);

    /// <summary>Checks <see cref="EmbeddingBatchSize"/>.</summary>
    /// <param name="options">The options.</param>
    /// <returns>Whether the batch size is valid.</returns>
    internal static bool HasValidBatchSize(KnowledgeOptions options) =>
        options.EmbeddingBatchSize is >= MinEmbeddingBatchSize and <= MaxEmbeddingBatchSize;
}
