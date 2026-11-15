using HoneyDrunk.AI.Abstractions.Embeddings;
using System.Text.RegularExpressions;

namespace HoneyDrunk.Knowledge.Abstractions;

/// <summary>
/// The checks the Knowledge records make. Every refusal names the parameter and states the rule; none
/// echoes the refused value, which may be document or query text (invariant 147).
/// </summary>
internal static partial class KnowledgeValidation
{
    /// <summary>Checks a source id against <c>^[a-z0-9][a-z0-9._:-]{0,127}$</c>.</summary>
    /// <param name="sourceId">The source id.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The source id, unchanged.</returns>
    internal static string SourceId(string sourceId, string paramName)
    {
        ArgumentNullException.ThrowIfNull(sourceId, paramName);
        return SourceIdPattern().IsMatch(sourceId)
            ? sourceId
            : throw new ArgumentException("A source id must be 1 to 128 characters of a-z, 0-9, '.', '_', ':' or '-', starting with a-z or 0-9.", paramName);
    }

    /// <summary>Checks an origin: 1 to 2,048 characters with no control character.</summary>
    /// <param name="origin">The origin.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The origin, unchanged.</returns>
    internal static string Origin(string origin, string paramName)
    {
        ArgumentNullException.ThrowIfNull(origin, paramName);
        return origin.Length is >= 1 and <= KnowledgeLimits.MaxOriginLength && !origin.Any(char.IsControl)
            ? origin
            : throw new ArgumentException("An origin must be 1 to 2,048 characters with no control character.", paramName);
    }

    /// <summary>Checks a version: 1 to 128 printable ASCII characters with no space.</summary>
    /// <param name="version">The version.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The version, unchanged.</returns>
    internal static string Version(string version, string paramName)
    {
        ArgumentNullException.ThrowIfNull(version, paramName);
        return version.Length is >= 1 and <= KnowledgeLimits.MaxVersionLength && version.All(c => c is > ' ' and <= '~')
            ? version
            : throw new ArgumentException("A version must be 1 to 128 printable ASCII characters with no space.", paramName);
    }

    /// <summary>Checks an embedding model id: 1 to 200 characters with no control character.</summary>
    /// <param name="modelId">The model id.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The model id, unchanged.</returns>
    internal static string EmbeddingModelId(string modelId, string paramName)
    {
        ArgumentNullException.ThrowIfNull(modelId, paramName);
        return IsModelId(modelId)
            ? modelId
            : throw new ArgumentException("An embedding model id must be 1 to 200 characters with no control character.", paramName);
    }

    /// <summary>
    /// Checks a source's instant: at or after 1970-01-01T00:00:00Z, compared as an instant whatever its offset. Every
    /// provider can then store it (a store may derive time-ordered ids from it).
    /// </summary>
    /// <param name="lastUpdatedAt">The instant.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The instant, unchanged.</returns>
    internal static DateTimeOffset LastUpdatedAt(DateTimeOffset lastUpdatedAt, string paramName) =>
        lastUpdatedAt >= DateTimeOffset.UnixEpoch
            ? lastUpdatedAt
            : throw new ArgumentOutOfRangeException(paramName, "A source must be dated at or after 1970-01-01T00:00:00Z.");

    /// <summary>Checks document text: 1 to 524,288 characters, not white space only, kept verbatim.</summary>
    /// <param name="text">The text.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The text, unchanged.</returns>
    internal static string DocumentText(string text, string paramName)
    {
        ArgumentNullException.ThrowIfNull(text, paramName);
        return text.Length <= KnowledgeLimits.MaxDocumentTextLength && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new ArgumentException("Document text must be 1 to 524,288 characters and not white space only.", paramName);
    }

    /// <summary>Checks chunk text: 1 to 1,200 characters, kept verbatim.</summary>
    /// <param name="text">The text.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The text, unchanged.</returns>
    internal static string ChunkText(string text, string paramName)
    {
        ArgumentNullException.ThrowIfNull(text, paramName);
        return IsChunkText(text)
            ? text
            : throw new ArgumentException("Chunk text must be 1 to 1,200 characters.", paramName);
    }

    /// <summary>Checks query text: 1 to 4,000 characters, not white space only, kept verbatim.</summary>
    /// <param name="text">The text.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The text, unchanged.</returns>
    internal static string QueryText(string text, string paramName)
    {
        ArgumentNullException.ThrowIfNull(text, paramName);
        return IsQueryText(text)
            ? text
            : throw new ArgumentException("Query text must be 1 to 4,000 characters and not white space only.", paramName);
    }

    /// <summary>Checks a chunk position: 0 to 1,023.</summary>
    /// <param name="position">The position.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The position, unchanged.</returns>
    internal static int Position(int position, string paramName) =>
        position is >= 0 and < KnowledgeLimits.MaxChunksPerVersion
            ? position
            : throw new ArgumentOutOfRangeException(paramName, "A chunk position must be 0 to 1,023.");

    /// <summary>Checks a chunk count: 0 to 1,024.</summary>
    /// <param name="count">The count.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The count, unchanged.</returns>
    internal static int ChunkCount(int count, string paramName) =>
        count is >= 0 and <= KnowledgeLimits.MaxChunksPerVersion
            ? count
            : throw new ArgumentOutOfRangeException(paramName, "A chunk count must be 0 to 1,024.");

    /// <summary>Checks a retrieval limit: 1 to 50.</summary>
    /// <param name="limit">The limit.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The limit, unchanged.</returns>
    internal static int Limit(int limit, string paramName) =>
        limit is >= 1 and <= KnowledgeLimits.MaxLimit
            ? limit
            : throw new ArgumentOutOfRangeException(paramName, "The limit must be 1 to 50.");

    /// <summary>Checks a score: a finite cosine similarity from -1 to 1.</summary>
    /// <param name="score">The score.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The score, unchanged.</returns>
    internal static double Score(double score, string paramName) =>
        double.IsFinite(score) && score is >= KnowledgeLimits.MinScore and <= KnowledgeLimits.MaxScore
            ? score
            : throw new ArgumentOutOfRangeException(paramName, "A score must be finite and from -1 to 1.");

    /// <summary>Checks an outcome is a defined value.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The outcome, unchanged.</returns>
    internal static IngestOutcome Outcome(IngestOutcome outcome, string paramName) =>
        Enum.IsDefined(outcome)
            ? outcome
            : throw new ArgumentOutOfRangeException(paramName, "The outcome must be Added, Superseded or Unchanged.");

    /// <summary>Checks a list of source ids: 1 to 100 valid ids with no repeat (ordinal).</summary>
    /// <param name="sourceIds">The ids.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>A read-only copy, in the given order.</returns>
    internal static IReadOnlyList<string> SourceIds(IReadOnlyList<string> sourceIds, string paramName)
    {
        ArgumentNullException.ThrowIfNull(sourceIds, paramName);
        if (sourceIds.Count is < 1 or > KnowledgeLimits.MaxSourceIds)
        {
            throw new ArgumentException("A request must name 1 to 100 source ids.", paramName);
        }

        return DistinctIds(sourceIds, paramName);
    }

    /// <summary>Checks a list of source ids that may be empty: at most 100 valid ids with no repeat (ordinal).</summary>
    /// <param name="sourceIds">The ids.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>A read-only copy, in the given order.</returns>
    internal static IReadOnlyList<string> OptionalSourceIds(IReadOnlyList<string> sourceIds, string paramName)
    {
        ArgumentNullException.ThrowIfNull(sourceIds, paramName);
        if (sourceIds.Count > KnowledgeLimits.MaxSourceIds)
        {
            throw new ArgumentException("A result may list at most 100 source ids.", paramName);
        }

        return DistinctIds(sourceIds, paramName);
    }

    /// <summary>Checks a list holds no null item.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>A read-only copy, in the given order.</returns>
    internal static IReadOnlyList<T> Items<T>(IReadOnlyList<T> items, string paramName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items, paramName);
        var copy = items.ToArray();
        return copy.Any(item => item is null)
            ? throw new ArgumentException("The list must not hold a null item.", paramName)
            : Array.AsReadOnly(copy);
    }

    /// <summary>
    /// Checks a chunk's embedding: text of 1 to 1,200 characters, 1 to 8,192 finite values and a model id.
    /// </summary>
    /// <param name="embedding">The embedding.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>A copy whose vector is a read-only copy of the given one.</returns>
    internal static Embedding ChunkEmbedding(Embedding embedding, string paramName)
    {
        ArgumentNullException.ThrowIfNull(embedding, paramName);
        if (embedding.Value is null || !IsChunkText(embedding.Value))
        {
            throw new ArgumentException("A chunk's embedding text must be 1 to 1,200 characters.", paramName);
        }

        return Checked(embedding, paramName);
    }

    /// <summary>
    /// Checks a query's embedding: text as query text, 1 to 8,192 finite values and a model id.
    /// </summary>
    /// <param name="embedding">The embedding.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>A copy whose vector is a read-only copy of the given one.</returns>
    internal static Embedding QueryEmbedding(Embedding embedding, string paramName)
    {
        ArgumentNullException.ThrowIfNull(embedding, paramName);
        if (embedding.Value is null || !IsQueryText(embedding.Value))
        {
            throw new ArgumentException("A query's embedding text must be 1 to 4,000 characters and not white space only.", paramName);
        }

        return Checked(embedding, paramName);
    }

    private static Embedding Checked(Embedding embedding, string paramName)
    {
        if (embedding.ModelId is null || !IsModelId(embedding.ModelId))
        {
            throw new ArgumentException("An embedding's model id must be 1 to 200 characters with no control character.", paramName);
        }

        var vector = embedding.Vector?.ToArray();
        if (vector is null || vector.Length is < 1 or > KnowledgeLimits.MaxVectorDimensions || !vector.All(float.IsFinite))
        {
            throw new ArgumentException("An embedding's vector must hold 1 to 8,192 values, each finite.", paramName);
        }

        return embedding with { Vector = Array.AsReadOnly(vector) };
    }

    private static IReadOnlyList<string> DistinctIds(IReadOnlyList<string> sourceIds, string paramName)
    {
        var copy = sourceIds.ToArray();
        if (!copy.All(id => id is not null && SourceIdPattern().IsMatch(id)))
        {
            throw new ArgumentException("Each source id must be 1 to 128 characters of a-z, 0-9, '.', '_', ':' or '-', starting with a-z or 0-9.", paramName);
        }

        return copy.Distinct(StringComparer.Ordinal).Count() == copy.Length
            ? Array.AsReadOnly(copy)
            : throw new ArgumentException("A source id must not repeat.", paramName);
    }

    private static bool IsModelId(string modelId) =>
        modelId.Length is >= 1 and <= KnowledgeLimits.MaxEmbeddingModelIdLength && !modelId.Any(char.IsControl);

    private static bool IsChunkText(string text) => text.Length is >= 1 and <= KnowledgeLimits.MaxChunkTextLength;

    private static bool IsQueryText(string text) => text.Length <= KnowledgeLimits.MaxQueryTextLength && !string.IsNullOrWhiteSpace(text);

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9._:-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SourceIdPattern();
}
