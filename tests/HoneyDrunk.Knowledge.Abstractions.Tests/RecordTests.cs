using HoneyDrunk.AI.Abstractions.Embeddings;
using Xunit;

namespace HoneyDrunk.Knowledge.Abstractions.Tests;

/// <summary>The checks each record makes in its constructor, and what its text shows.</summary>
public sealed class RecordTests
{
    private const string Origin = "https://example.test/guide";
    private const string Model = "model-a";
    private const string TextSentinel = "KNOWLEDGE-TEXT-SENTINEL-4471";
    private const string QuerySentinel = "KNOWLEDGE-QUERY-SENTINEL-4472";
    private const float VectorSentinel = 4242.25f;

    private static readonly DateTimeOffset At = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Source ids matching the pinned pattern are accepted by every record that carries one; any other
    /// is refused, naming the parameter.
    /// </summary>
    /// <param name="token">The id, or a length token.</param>
    /// <param name="accepted">Whether the id is accepted.</param>
    [Theory]
    [InlineData("a", true)]
    [InlineData("docs:intro", true)]
    [InlineData("0.release_notes-2", true)]
    [InlineData("len:128", true)]
    [InlineData("", false)]
    [InlineData("A", false)]
    [InlineData("docs:Intro", false)]
    [InlineData("-x", false)]
    [InlineData(".x", false)]
    [InlineData("len:129", false)]
    [InlineData("a b", false)]
    [InlineData("char:e9", false)]
    [InlineData("a/b", false)]
    [InlineData("a\n", false)]
    public void Source_ids_follow_the_pattern(string token, bool accepted)
    {
        var id = Expand(token);
        if (accepted)
        {
            Assert.Equal(id, Source(id).SourceId);
            Assert.Equal(id, new KnowledgeDocument(id, Origin, "v1", "text").SourceId);
            Assert.Equal([id], new RetrievalRequest("query", [id]).SourceIds);
            Assert.Equal([id], new SimilarityQuery(QueryEmbedding("query"), [id], 5).SourceIds);
            return;
        }

        Assert.Equal("SourceId", Assert.ThrowsAny<ArgumentException>(() => Source(id)).ParamName);
        Assert.Equal("SourceId", Assert.ThrowsAny<ArgumentException>(() => new KnowledgeDocument(id, Origin, "v1", "text")).ParamName);
        Assert.Equal("SourceIds", Assert.ThrowsAny<ArgumentException>(() => new RetrievalRequest("query", [id])).ParamName);
        Assert.Equal("SourceIds", Assert.ThrowsAny<ArgumentException>(() => new SimilarityQuery(QueryEmbedding("query"), [id], 5)).ParamName);
        Assert.Equal("SourceId", Assert.ThrowsAny<ArgumentException>(() => Source(null!)).ParamName);
    }

    /// <summary>Versions are 1 to 128 printable ASCII characters with no space.</summary>
    [Fact]
    public void Versions_are_printable_ascii_without_spaces()
    {
        Assert.Equal("v1", Source("docs:intro", version: "v1").Version);
        Assert.Equal("1.0.0-rc.1+build.7", Source("docs:intro", version: "1.0.0-rc.1+build.7").Version);
        Assert.Equal("!~", Source("docs:intro", version: "!~").Version);
        Assert.Equal(128, Source("docs:intro", version: new string('~', 128)).Version.Length);
        Assert.Equal("v1", new KnowledgeDocument("docs:intro", Origin, "v1", "text").Version);

        string?[] refused = [string.Empty, "v 1", "v\t1", "v" + (char)0xE9 + "1", "v" + (char)0x7F, "v" + (char)0xA0 + "1", new string('v', 129), null];
        foreach (var version in refused)
        {
            Assert.Equal("Version", Assert.ThrowsAny<ArgumentException>(() => Source("docs:intro", version: version!)).ParamName);
            Assert.Equal("Version", Assert.ThrowsAny<ArgumentException>(() => new KnowledgeDocument("docs:intro", Origin, version!, "text")).ParamName);
        }
    }

    /// <summary>Origins are 1 to 2,048 characters and model ids 1 to 200, neither with a control character.</summary>
    [Fact]
    public void Origins_and_model_ids_have_bounded_length_and_no_control_character()
    {
        Assert.Equal(2_048, Source("docs:intro", origin: new string('o', 2_048)).Origin.Length);
        Assert.Equal("a plain name", Source("docs:intro", origin: "a plain name").Origin);
        Assert.Equal(200, Source("docs:intro", model: new string('m', 200)).EmbeddingModelId.Length);

        string?[] origins = [string.Empty, new string('o', 2_049), "https://example.test/\nguide", "\u0000", null];
        foreach (var origin in origins)
        {
            Assert.Equal("Origin", Assert.ThrowsAny<ArgumentException>(() => Source("docs:intro", origin: origin!)).ParamName);
            Assert.Equal("Origin", Assert.ThrowsAny<ArgumentException>(() => new KnowledgeDocument("docs:intro", origin!, "v1", "text")).ParamName);
        }

        string?[] models = [string.Empty, new string('m', 201), "model\u0007a", null];
        foreach (var model in models)
        {
            Assert.Equal("EmbeddingModelId", Assert.ThrowsAny<ArgumentException>(() => Source("docs:intro", model: model!)).ParamName);
        }
    }

    /// <summary>Document text is 1 to 524,288 characters, not white space only, and kept verbatim.</summary>
    [Fact]
    public void Document_text_is_1_to_524288_characters_and_not_blank()
    {
        Assert.Equal(KnowledgeLimits.MaxDocumentTextLength, new KnowledgeDocument("docs:intro", Origin, "v1", new string('x', 524_288)).Text.Length);
        Assert.Equal("  x\n", new KnowledgeDocument("docs:intro", Origin, "v1", "  x\n").Text);
        Assert.Equal("x", new KnowledgeDocument("docs:intro", Origin, "v1", "x").Text);

        string?[] refused = [string.Empty, "   ", "\r\n\t", new string('x', 524_289), null];
        foreach (var text in refused)
        {
            Assert.Equal("Text", Assert.ThrowsAny<ArgumentException>(() => new KnowledgeDocument("docs:intro", Origin, "v1", text!)).ParamName);
        }
    }

    /// <summary>A chunk holds a position of 0 to 1,023 and an embedding of 1 to 1,200 characters, 1 to 8,192 finite values and a model id.</summary>
    [Fact]
    public void Chunk_bounds_hold()
    {
        Assert.Equal(0, new KnowledgeChunk(0, ChunkEmbedding("text")).Position);
        Assert.Equal(1_023, new KnowledgeChunk(1_023, ChunkEmbedding("text")).Position);
        Assert.Equal(1_200, new KnowledgeChunk(0, ChunkEmbedding(new string('x', 1_200))).Embedding.Value.Length);
        Assert.Equal(8_192, new KnowledgeChunk(0, new Embedding("text", Vector(8_192), Model)).Embedding.Vector.Count);
        Assert.Equal(Model, new KnowledgeChunk(0, ChunkEmbedding("text")).Embedding.ModelId);

        Assert.Equal("Position", Assert.Throws<ArgumentOutOfRangeException>(() => new KnowledgeChunk(-1, ChunkEmbedding("text"))).ParamName);
        Assert.Equal("Position", Assert.Throws<ArgumentOutOfRangeException>(() => new KnowledgeChunk(1_024, ChunkEmbedding("text"))).ParamName);

        Embedding?[] refused =
        [
            null,
            new Embedding(string.Empty, Vector(4), Model),
            new Embedding(new string('x', 1_201), Vector(4), Model),
            new Embedding(null!, Vector(4), Model),
            new Embedding("text", [], Model),
            new Embedding("text", Vector(8_193), Model),
            new Embedding("text", null!, Model),
            new Embedding("text", [0.5f, float.NaN], Model),
            new Embedding("text", [float.PositiveInfinity], Model),
            new Embedding("text", [float.NegativeInfinity], Model),
            new Embedding("text", Vector(4), string.Empty),
            new Embedding("text", Vector(4), null!),
            new Embedding("text", Vector(4), new string('m', 201)),
            new Embedding("text", Vector(4), "model\na"),
        ];
        foreach (var embedding in refused)
        {
            Assert.Equal("Embedding", Assert.ThrowsAny<ArgumentException>(() => new KnowledgeChunk(0, embedding!)).ParamName);
        }
    }

    /// <summary>A retrieval request holds 1 to 4,000 characters of query, 1 to 100 distinct valid ids and a limit of 1 to 50.</summary>
    [Fact]
    public void Request_bounds_hold()
    {
        var hundred = Ids(100);

        Assert.Equal(4_000, new RetrievalRequest(new string('q', 4_000), ["docs:intro"]).QueryText.Length);
        Assert.Equal(100, new RetrievalRequest("query", hundred).SourceIds.Count);
        Assert.Equal(5, new RetrievalRequest("query", ["docs:intro"]).Limit);
        Assert.Equal(1, new RetrievalRequest("query", ["docs:intro"], 1).Limit);
        Assert.Equal(50, new RetrievalRequest("query", ["docs:intro"], 50).Limit);

        string?[] queries = [string.Empty, "  \n", new string('q', 4_001), null];
        foreach (var query in queries)
        {
            Assert.Equal("QueryText", Assert.ThrowsAny<ArgumentException>(() => new RetrievalRequest(query!, ["docs:intro"])).ParamName);
        }

        IReadOnlyList<string>?[] lists = [[], Ids(101), ["docs:intro", "docs:other", "docs:intro"], ["docs:intro", null!], null];
        foreach (var list in lists)
        {
            Assert.Equal("SourceIds", Assert.ThrowsAny<ArgumentException>(() => new RetrievalRequest("query", list!)).ParamName);
        }

        Assert.Equal("Limit", Assert.Throws<ArgumentOutOfRangeException>(() => new RetrievalRequest("query", ["docs:intro"], 0)).ParamName);
        Assert.Equal("Limit", Assert.Throws<ArgumentOutOfRangeException>(() => new RetrievalRequest("query", ["docs:intro"], 51)).ParamName);
    }

    /// <summary>A similarity query checks its embedding's text as query text, and its vector and model id as a chunk's.</summary>
    [Fact]
    public void Similarity_query_bounds_hold()
    {
        Assert.Equal(4_000, new SimilarityQuery(QueryEmbedding(new string('q', 4_000)), ["docs:intro"], 5).Query.Value.Length);
        Assert.Equal(8_192, new SimilarityQuery(new Embedding("query", Vector(8_192), Model), ["docs:intro"], 5).Query.Vector.Count);
        Assert.Equal(100, new SimilarityQuery(QueryEmbedding("query"), Ids(100), 50).SourceIds.Count);

        Embedding?[] refused =
        [
            null,
            new Embedding(string.Empty, Vector(4), Model),
            new Embedding(" \t ", Vector(4), Model),
            new Embedding(new string('q', 4_001), Vector(4), Model),
            new Embedding("query", [], Model),
            new Embedding("query", Vector(8_193), Model),
            new Embedding("query", [float.NaN], Model),
            new Embedding("query", [float.PositiveInfinity], Model),
            new Embedding("query", Vector(4), string.Empty),
            new Embedding("query", Vector(4), new string('m', 201)),
        ];
        foreach (var query in refused)
        {
            Assert.Equal("Query", Assert.ThrowsAny<ArgumentException>(() => new SimilarityQuery(query!, ["docs:intro"], 5)).ParamName);
        }

        Assert.Equal("SourceIds", Assert.ThrowsAny<ArgumentException>(() => new SimilarityQuery(QueryEmbedding("query"), [], 5)).ParamName);
        Assert.Equal("SourceIds", Assert.ThrowsAny<ArgumentException>(() => new SimilarityQuery(QueryEmbedding("query"), Ids(101), 5)).ParamName);
        Assert.Equal("SourceIds", Assert.ThrowsAny<ArgumentException>(() => new SimilarityQuery(QueryEmbedding("query"), ["docs:a", "docs:a"], 5)).ParamName);
        Assert.Equal("Limit", Assert.Throws<ArgumentOutOfRangeException>(() => new SimilarityQuery(QueryEmbedding("query"), ["docs:intro"], 0)).ParamName);
        Assert.Equal("Limit", Assert.Throws<ArgumentOutOfRangeException>(() => new SimilarityQuery(QueryEmbedding("query"), ["docs:intro"], 51)).ParamName);
    }

    /// <summary>A retrieved chunk scores a finite cosine from -1 to 1, with a position and text of chunk bounds.</summary>
    [Fact]
    public void Score_is_finite_and_within_minus_one_and_one()
    {
        double[] accepted = [-1, 0, 0.5, 1];
        foreach (var score in accepted)
        {
            Assert.Equal(score, new RetrievedChunk(Source("docs:intro"), 0, "text", score).Score);
        }

        double[] refused = [double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.0001, 1.0001];
        foreach (var score in refused)
        {
            Assert.Equal("Score", Assert.Throws<ArgumentOutOfRangeException>(() => new RetrievedChunk(Source("docs:intro"), 0, "text", score)).ParamName);
        }

        Assert.Equal("Source", Assert.ThrowsAny<ArgumentException>(() => new RetrievedChunk(null!, 0, "text", 0)).ParamName);
        Assert.Equal("Position", Assert.Throws<ArgumentOutOfRangeException>(() => new RetrievedChunk(Source("docs:intro"), 1_024, "text", 0)).ParamName);
        Assert.Equal("Text", Assert.ThrowsAny<ArgumentException>(() => new RetrievedChunk(Source("docs:intro"), 0, string.Empty, 0)).ParamName);
        Assert.Equal("Text", Assert.ThrowsAny<ArgumentException>(() => new RetrievedChunk(Source("docs:intro"), 0, new string('x', 1_201), 0)).ParamName);
    }

    /// <summary>Results refuse missing parts and hold copies of their lists.</summary>
    [Fact]
    public void Results_refuse_missing_parts()
    {
        var chunk = new RetrievedChunk(Source("docs:intro"), 0, "text", 0.5);

        Assert.Equal("Source", Assert.ThrowsAny<ArgumentException>(() => new IngestResult(null!, IngestOutcome.Added, 1)).ParamName);
        Assert.Equal("Outcome", Assert.Throws<ArgumentOutOfRangeException>(() => new IngestResult(Source("docs:intro"), (IngestOutcome)7, 1)).ParamName);
        Assert.Equal("ChunkCount", Assert.Throws<ArgumentOutOfRangeException>(() => new IngestResult(Source("docs:intro"), IngestOutcome.Added, -1)).ParamName);
        Assert.Equal("ChunkCount", Assert.Throws<ArgumentOutOfRangeException>(() => new IngestResult(Source("docs:intro"), IngestOutcome.Added, 1_025)).ParamName);
        Assert.Equal(1_024, new IngestResult(Source("docs:intro"), IngestOutcome.Superseded, 1_024).ChunkCount);

        Assert.Equal("Chunks", Assert.ThrowsAny<ArgumentException>(() => new RetrievalResult(null!, [])).ParamName);
        Assert.Equal("Chunks", Assert.ThrowsAny<ArgumentException>(() => new RetrievalResult([chunk, null!], [])).ParamName);
        Assert.Equal("MismatchedSourceIds", Assert.ThrowsAny<ArgumentException>(() => new RetrievalResult([chunk], null!)).ParamName);
        Assert.Equal("MismatchedSourceIds", Assert.ThrowsAny<ArgumentException>(() => new RetrievalResult([chunk], ["Docs"])).ParamName);
        Assert.Empty(new RetrievalResult([], []).Chunks);
    }

    /// <summary>
    /// Every checked list and vector is a read-only copy: changing or casting the caller's list
    /// afterwards cannot change what was checked.
    /// </summary>
    [Fact]
    public void Checked_lists_and_vectors_are_copies()
    {
        var vector = new[] { 0.5f, 0.25f };
        var ids = new List<string> { "docs:intro" };
        var chunk = new KnowledgeChunk(0, new Embedding("text", vector, Model));
        var query = new SimilarityQuery(new Embedding("query", vector, Model), ids, 5);
        var request = new RetrievalRequest("query", ids);
        var result = new RetrievalResult([new RetrievedChunk(Source("docs:intro"), 0, "text", 0.5)], ids);

        vector[0] = float.NaN;
        ids[0] = "Not An Id";

        Assert.Equal([0.5f, 0.25f], chunk.Embedding.Vector);
        Assert.Equal([0.5f, 0.25f], query.Query.Vector);
        Assert.Equal(["docs:intro"], query.SourceIds);
        Assert.Equal(["docs:intro"], request.SourceIds);
        Assert.Equal(["docs:intro"], result.MismatchedSourceIds);
        Assert.False(CanWrite(chunk.Embedding.Vector));
        Assert.False(CanWrite(query.SourceIds));
        Assert.False(CanWrite(request.SourceIds));
        Assert.False(CanWrite(result.Chunks));
    }

    /// <summary>Records print lengths, counts and model ids, never text, a query or a vector (invariant 147).</summary>
    [Fact]
    public void ToString_never_prints_text_query_or_vector()
    {
        var vector = new[] { VectorSentinel, 1f };
        var printed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KnowledgeDocument"] = new KnowledgeDocument("docs:intro", Origin, "v1", TextSentinel).ToString(),
            ["KnowledgeChunk"] = new KnowledgeChunk(3, new Embedding(TextSentinel, vector, Model)).ToString(),
            ["RetrievalRequest"] = new RetrievalRequest(QuerySentinel, ["docs:intro"]).ToString(),
            ["SimilarityQuery"] = new SimilarityQuery(new Embedding(QuerySentinel, vector, Model), ["docs:intro"], 5).ToString(),
            ["RetrievedChunk"] = new RetrievedChunk(Source("docs:intro"), 3, TextSentinel, 0.5).ToString(),
        };

        var leaking = printed
            .Where(pair => pair.Value.Contains(TextSentinel, StringComparison.Ordinal)
                || pair.Value.Contains(QuerySentinel, StringComparison.Ordinal)
                || pair.Value.Contains("4242", StringComparison.Ordinal))
            .Select(pair => pair.Key);
        Assert.Empty(leaking);

        Assert.Contains("TextLength = 28", printed["KnowledgeDocument"], StringComparison.Ordinal);
        Assert.Contains("TextLength = 28", printed["KnowledgeChunk"], StringComparison.Ordinal);
        Assert.Contains("Dimensions = 2", printed["KnowledgeChunk"], StringComparison.Ordinal);
        Assert.Contains("EmbeddingModelId = model-a", printed["KnowledgeChunk"], StringComparison.Ordinal);
        Assert.Contains("QueryLength = 29", printed["RetrievalRequest"], StringComparison.Ordinal);
        Assert.Contains("SourceCount = 1", printed["RetrievalRequest"], StringComparison.Ordinal);
        Assert.Contains("EmbeddingModelId = model-a", printed["SimilarityQuery"], StringComparison.Ordinal);
        Assert.Contains("QueryLength = 29", printed["SimilarityQuery"], StringComparison.Ordinal);
        Assert.Contains("TextLength = 28", printed["RetrievedChunk"], StringComparison.Ordinal);
        Assert.Contains("SourceId = docs:intro", printed["RetrievedChunk"], StringComparison.Ordinal);
    }

    /// <summary>A refusal names the parameter and never echoes the value it refused.</summary>
    [Fact]
    public void Refusals_never_echo_the_value()
    {
        var refusals = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            ["SourceId"] = () => Source("docs:" + QuerySentinel),
            ["Origin"] = () => Source("docs:intro", origin: QuerySentinel + "\n"),
            ["Version"] = () => Source("docs:intro", version: QuerySentinel + " x"),
            ["EmbeddingModelId"] = () => Source("docs:intro", model: QuerySentinel + "\u0001"),
            ["Text"] = () => new KnowledgeDocument("docs:intro", Origin, "v1", TextSentinel + new string(' ', 524_288)),
            ["Embedding"] = () => new KnowledgeChunk(0, new Embedding(TextSentinel + new string('x', 1_200), [1f], Model)),
            ["QueryText"] = () => new RetrievalRequest(QuerySentinel + new string('q', 4_000), ["docs:intro"]),
            ["Query"] = () => new SimilarityQuery(new Embedding(QuerySentinel + new string('q', 4_000), [1f], Model), ["docs:intro"], 5),
            ["SourceIds"] = () => new RetrievalRequest("query", ["docs:" + QuerySentinel]),
            ["Limit"] = () => new RetrievalRequest("query", ["docs:intro"], 4_242),
            ["Score"] = () => new RetrievedChunk(Source("docs:intro"), 0, "text", 4_242.25),
        };

        foreach (var (name, refusal) in refusals)
        {
            var error = Assert.ThrowsAny<ArgumentException>(refusal);
            Assert.Equal(name, error.ParamName);
            Assert.DoesNotContain(QuerySentinel, error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(TextSentinel, error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("4242", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("4,242", error.Message, StringComparison.Ordinal);
        }
    }

    private static KnowledgeSource Source(string id, string origin = Origin, string version = "v1", string model = Model) =>
        new(id, origin, version, At, model);

    private static Embedding ChunkEmbedding(string text) => new(text, Vector(4), Model);

    private static Embedding QueryEmbedding(string text) => new(text, Vector(4), Model);

    private static float[] Vector(int length) => Enumerable.Repeat(0.5f, length).ToArray();

    private static string[] Ids(int count) => Enumerable.Range(0, count).Select(i => "docs:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    // "len:N" is N letters a; "char:XX" is the one character with that hexadecimal code.
    private static string Expand(string token)
    {
        if (token.StartsWith("len:", StringComparison.Ordinal))
        {
            return new string('a', int.Parse(token["len:".Length..], System.Globalization.CultureInfo.InvariantCulture));
        }

        return token.StartsWith("char:", StringComparison.Ordinal)
            ? ((char)int.Parse(token["char:".Length..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)).ToString()
            : token;
    }

    // True when a cast back to IList<T> can overwrite an item of the list.
    private static bool CanWrite<T>(IReadOnlyList<T> list)
    {
        if (list is not IList<T> items || items.Count == 0)
        {
            return false;
        }

        var first = items[0];
        try
        {
            items[0] = first;
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
