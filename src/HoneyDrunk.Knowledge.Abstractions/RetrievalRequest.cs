using System.Globalization;

namespace HoneyDrunk.Knowledge.Abstractions;

/// <summary>A question to ask of named sources, handed to an <see cref="IRetrievalPipeline"/>.</summary>
/// <param name="QueryText">The query: 1 to 4,000 characters, not white space only.</param>
/// <param name="SourceIds">The sources to search: 1 to 100 valid source ids with no repeat (ordinal). The record keeps a read-only copy.</param>
/// <param name="Limit">The most chunks to return: 1 to 50.</param>
public sealed record RetrievalRequest(string QueryText, IReadOnlyList<string> SourceIds, int Limit = KnowledgeLimits.DefaultLimit)
{
    /// <summary>Gets the checked query text, unchanged.</summary>
    public string QueryText { get; } = KnowledgeValidation.QueryText(QueryText, nameof(QueryText));

    /// <summary>Gets a read-only copy of the checked source ids.</summary>
    public IReadOnlyList<string> SourceIds { get; } = KnowledgeValidation.SourceIds(SourceIds, nameof(SourceIds));

    /// <summary>Gets the checked limit.</summary>
    public int Limit { get; } = KnowledgeValidation.Limit(Limit, nameof(Limit));

    /// <summary>Returns the query length, source count and limit, never the query (invariant 147).</summary>
    /// <returns>The metadata text.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"RetrievalRequest {{ QueryLength = {this.QueryText.Length}, SourceCount = {this.SourceIds.Count}, Limit = {this.Limit} }}");
}
