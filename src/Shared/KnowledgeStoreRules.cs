using HoneyDrunk.Knowledge.Abstractions;

namespace HoneyDrunk.Knowledge.Providers;

/// <summary>
/// The store rules every provider shares: the chunk-set checks, the ingest decision, which sources a query may
/// score, the cosine score and the result order. Each provider assembly links this one source file in, so the
/// rules live in one place and add no public API. No message echoes text, a query or a vector (invariant 147).
/// </summary>
internal static class KnowledgeStoreRules
{
    /// <summary>
    /// Checks a chunk set: at least 1 and at most 1,024 chunks, positions 0 to n-1 in order, one vector length,
    /// and every embedding made by the source's model.
    /// </summary>
    /// <param name="source">The source version the chunks belong to.</param>
    /// <param name="chunks">The chunks to check.</param>
    /// <returns>The vector length shared by every chunk.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="chunks"/> is null.</exception>
    /// <exception cref="ArgumentException">The chunk set breaks a rule; <c>ParamName</c> is <c>chunks</c>.</exception>
    internal static int ValidateChunks(KnowledgeSource source, IReadOnlyList<KnowledgeChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(chunks);

        if (chunks.Count < 1 || chunks.Count > KnowledgeLimits.MaxChunksPerVersion)
        {
            throw new ArgumentException("A version holds 1 to 1,024 chunks.", nameof(chunks));
        }

        var dimensions = -1;
        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            if (chunk is null)
            {
                throw new ArgumentException("A chunk must not be null.", nameof(chunks));
            }

            if (chunk.Position != index)
            {
                throw new ArgumentException("Chunk positions must run from 0 to n-1 in order.", nameof(chunks));
            }

            if (!string.Equals(chunk.Embedding.ModelId, source.EmbeddingModelId, StringComparison.Ordinal))
            {
                throw new ArgumentException("Every chunk must be embedded by the source's model.", nameof(chunks));
            }

            var length = chunk.Embedding.Vector.Count;
            if (dimensions < 0)
            {
                dimensions = length;
            }
            else if (length != dimensions)
            {
                throw new ArgumentException("Every chunk must have a vector of one length.", nameof(chunks));
            }
        }

        return dimensions;
    }

    /// <summary>Decides what storing <paramref name="next"/> over <paramref name="current"/> means.</summary>
    /// <param name="current">The current version, or null when the source is new.</param>
    /// <param name="next">The version being stored.</param>
    /// <returns><c>Added</c> with no current version, <c>Unchanged</c> for an equal version and model, else <c>Superseded</c>.</returns>
    internal static IngestOutcome Decide(KnowledgeSource? current, KnowledgeSource next)
    {
        if (current is null)
        {
            return IngestOutcome.Added;
        }

        var same = string.Equals(current.Version, next.Version, StringComparison.Ordinal)
            && string.Equals(current.EmbeddingModelId, next.EmbeddingModelId, StringComparison.Ordinal);
        return same ? IngestOutcome.Unchanged : IngestOutcome.Superseded;
    }

    /// <summary>Tells whether a query may score a source's chunks: the same model and the same vector length (invariant 146).</summary>
    /// <param name="source">The current version of a named source.</param>
    /// <param name="dimensions">The vector length of that version's chunks.</param>
    /// <param name="query">The query.</param>
    /// <returns>True when the source's chunks may be scored.</returns>
    internal static bool CanScore(KnowledgeSource source, int dimensions, SimilarityQuery query) =>
        string.Equals(source.EmbeddingModelId, query.Query.ModelId, StringComparison.Ordinal)
        && dimensions == query.Query.Vector.Count;

    /// <summary>
    /// The cosine similarity of two vectors of one length, clamped to -1 to 1 because rounding can step past
    /// either end. A vector whose norm is zero scores 0.
    /// </summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector, of the same length.</param>
    /// <returns>A finite score from -1 to 1.</returns>
    internal static double Cosine(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (var index = 0; index < left.Length; index++)
        {
            double a = left[index];
            double b = right[index];
            dot += a * b;
            leftNorm += a * a;
            rightNorm += b * b;
        }

        // A sum of squares is never negative, so "not above zero" is exactly "zero" here.
        if (leftNorm <= 0 || rightNorm <= 0)
        {
            return 0;
        }

        var score = dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
        return Math.Clamp(score, KnowledgeLimits.MinScore, KnowledgeLimits.MaxScore);
    }

    /// <summary>Orders two hits: score descending, then source id (ordinal), then position.</summary>
    /// <param name="leftScore">The first hit's score.</param>
    /// <param name="leftSourceId">The first hit's source id.</param>
    /// <param name="leftPosition">The first hit's position.</param>
    /// <param name="rightScore">The second hit's score.</param>
    /// <param name="rightSourceId">The second hit's source id.</param>
    /// <param name="rightPosition">The second hit's position.</param>
    /// <returns>Negative when the first hit ranks ahead, positive when the second does.</returns>
    internal static int CompareRank(
        double leftScore,
        string leftSourceId,
        int leftPosition,
        double rightScore,
        string rightSourceId,
        int rightPosition)
    {
        var byScore = rightScore.CompareTo(leftScore);
        if (byScore != 0)
        {
            return byScore;
        }

        var bySource = string.CompareOrdinal(leftSourceId, rightSourceId);
        return bySource != 0 ? bySource : leftPosition.CompareTo(rightPosition);
    }

    /// <summary>Puts mismatched source ids in ordinal order.</summary>
    /// <param name="sourceIds">The ids.</param>
    /// <returns>The ids in ordinal order.</returns>
    internal static string[] OrderMismatched(IEnumerable<string> sourceIds) =>
        [.. sourceIds.Order(StringComparer.Ordinal)];
}
