using HoneyDrunk.AI.Abstractions.Embeddings;
using HoneyDrunk.Knowledge.Abstractions;

namespace HoneyDrunk.Knowledge.Providers.Data;

/// <summary>One retrievable passage of one source version, with its embedding.</summary>
internal sealed class KnowledgeChunkEntity
{
    /// <summary>Gets or sets the id of the version the chunk belongs to.</summary>
    public string SourceVersionId { get; set; } = string.Empty;

    /// <summary>Gets or sets the chunk's position in its version, from 0.</summary>
    public int Position { get; set; }

    /// <summary>Gets or sets the passage text, verbatim. It is never logged.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the embedding vector, packed by <see cref="VectorCodec"/>.</summary>
    public byte[] Vector { get; set; } = [];

    /// <summary>Gets or sets the ingest instant of the chunk's version, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Rebuilds the chunk the contract stored: its text, its vector and the version's model.</summary>
    /// <param name="embeddingModelId">The embedding model of the chunk's version.</param>
    /// <returns>The chunk.</returns>
    public KnowledgeChunk ToChunk(string embeddingModelId) =>
        new(this.Position, new Embedding(this.Text, VectorCodec.Decode(this.Vector), embeddingModelId));
}
