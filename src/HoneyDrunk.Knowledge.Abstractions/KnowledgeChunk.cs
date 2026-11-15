using HoneyDrunk.AI.Abstractions.Embeddings;
using System.Globalization;

namespace HoneyDrunk.Knowledge.Abstractions;

/// <summary>
/// One retrievable passage of a version, with its embedding. <see cref="Embedding.Value"/> is the
/// chunk's text, so the text, the vector and the model that made it travel together.
/// </summary>
/// <param name="Position">The passage's place in its version: 0 to 1,023.</param>
/// <param name="Embedding">
/// The passage's embedding: text of 1 to 1,200 characters, a vector of 1 to 8,192 finite values and a
/// model id of 1 to 200 characters with no control character. The record keeps a read-only copy of the vector.
/// </param>
public sealed record KnowledgeChunk(int Position, Embedding Embedding)
{
    /// <summary>Gets the checked position.</summary>
    public int Position { get; } = KnowledgeValidation.Position(Position, nameof(Position));

    /// <summary>Gets the checked embedding, holding a read-only copy of the vector.</summary>
    public Embedding Embedding { get; } = KnowledgeValidation.ChunkEmbedding(Embedding, nameof(Embedding));

    /// <summary>Returns the position, text length, dimensions and model id, never the text or vector (invariant 147).</summary>
    /// <returns>The metadata text.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"KnowledgeChunk {{ Position = {this.Position}, TextLength = {this.Embedding.Value.Length}, Dimensions = {this.Embedding.Vector.Count}, EmbeddingModelId = {this.Embedding.ModelId} }}");
}
