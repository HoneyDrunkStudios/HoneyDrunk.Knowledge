namespace HoneyDrunk.Knowledge.Abstractions;

/// <summary>The bounds every Knowledge record checks in its constructor.</summary>
public static class KnowledgeLimits
{
    /// <summary>The longest source id: 128 characters of <c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>_</c>, <c>:</c> or <c>-</c>, starting with <c>a-z</c> or <c>0-9</c>.</summary>
    public const int MaxSourceIdLength = 128;

    /// <summary>The longest origin, in characters.</summary>
    public const int MaxOriginLength = 2_048;

    /// <summary>The longest version: printable ASCII characters with no space.</summary>
    public const int MaxVersionLength = 128;

    /// <summary>The longest embedding model id, in characters.</summary>
    public const int MaxEmbeddingModelIdLength = 200;

    /// <summary>The longest document text, in characters.</summary>
    public const int MaxDocumentTextLength = 524_288;

    /// <summary>The most chunks one version of a source may hold; positions run from 0 to one less than this.</summary>
    public const int MaxChunksPerVersion = 1_024;

    /// <summary>The longest chunk text, in characters.</summary>
    public const int MaxChunkTextLength = 1_200;

    /// <summary>The most values an embedding vector may hold.</summary>
    public const int MaxVectorDimensions = 8_192;

    /// <summary>The longest query text, in characters.</summary>
    public const int MaxQueryTextLength = 4_000;

    /// <summary>The most source ids one request or query may name.</summary>
    public const int MaxSourceIds = 100;

    /// <summary>The largest number of chunks a retrieval may return.</summary>
    public const int MaxLimit = 50;

    /// <summary>The number of chunks a retrieval returns when the caller names none.</summary>
    public const int DefaultLimit = 5;

    /// <summary>The lowest score: cosine similarity of opposite vectors.</summary>
    public const double MinScore = -1;

    /// <summary>The highest score: cosine similarity of vectors pointing the same way.</summary>
    public const double MaxScore = 1;
}
