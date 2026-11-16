using System.Globalization;

namespace HoneyDrunk.Knowledge.Abstractions;

/// <summary>One passage a search returned, with the source version it came from and its position (invariant 145).</summary>
/// <param name="Source">The source version the passage belongs to.</param>
/// <param name="Position">The passage's place in its version: 0 to 1,023.</param>
/// <param name="Text">The passage's text: 1 to 1,200 characters, verbatim.</param>
/// <param name="Score">
/// The cosine similarity of the passage's vector and the query's: finite, from -1 to 1, higher is closer.
/// A vector whose norm is zero scores 0.
/// </param>
public sealed record RetrievedChunk(KnowledgeSource Source, int Position, string Text, double Score)
{
    /// <summary>Gets the source version.</summary>
    public KnowledgeSource Source { get; } = Source ?? throw new ArgumentNullException(nameof(Source));

    /// <summary>Gets the checked position.</summary>
    public int Position { get; } = KnowledgeValidation.Position(Position, nameof(Position));

    /// <summary>Gets the checked text, unchanged.</summary>
    public string Text { get; } = KnowledgeValidation.ChunkText(Text, nameof(Text));

    /// <summary>Gets the checked score.</summary>
    public double Score { get; } = KnowledgeValidation.Score(Score, nameof(Score));

    /// <summary>
    /// Returns the source id, version, model id, position, text length and score, never the text
    /// (invariant 147).
    /// </summary>
    /// <returns>The metadata text.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"RetrievedChunk {{ SourceId = {this.Source.SourceId}, Version = {this.Source.Version}, EmbeddingModelId = {this.Source.EmbeddingModelId}, Position = {this.Position}, TextLength = {this.Text.Length}, Score = {this.Score} }}");
}
