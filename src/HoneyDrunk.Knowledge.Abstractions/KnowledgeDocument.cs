using System.Globalization;

namespace HoneyDrunk.Knowledge.Abstractions;

/// <summary>One version of a source's text, handed to an <see cref="IDocumentIngester"/>.</summary>
/// <param name="SourceId">The source id, as <see cref="KnowledgeSource.SourceId"/>.</param>
/// <param name="Origin">The origin, as <see cref="KnowledgeSource.Origin"/>.</param>
/// <param name="Version">The version, as <see cref="KnowledgeSource.Version"/>.</param>
/// <param name="Text">The plain text or Markdown: 1 to 524,288 characters, not white space only, kept verbatim.</param>
public sealed record KnowledgeDocument(string SourceId, string Origin, string Version, string Text)
{
    /// <summary>Gets the checked source id.</summary>
    public string SourceId { get; } = KnowledgeValidation.SourceId(SourceId, nameof(SourceId));

    /// <summary>Gets the checked origin.</summary>
    public string Origin { get; } = KnowledgeValidation.Origin(Origin, nameof(Origin));

    /// <summary>Gets the checked version.</summary>
    public string Version { get; } = KnowledgeValidation.Version(Version, nameof(Version));

    /// <summary>Gets the checked text, unchanged.</summary>
    public string Text { get; } = KnowledgeValidation.DocumentText(Text, nameof(Text));

    /// <summary>Returns the source id, version and text length, never the text (invariant 147).</summary>
    /// <returns>The metadata text.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"KnowledgeDocument {{ SourceId = {this.SourceId}, Version = {this.Version}, TextLength = {this.Text.Length} }}");
}
