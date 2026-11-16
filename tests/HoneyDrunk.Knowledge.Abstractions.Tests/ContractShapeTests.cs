using HoneyDrunk.Knowledge.Abstractions.Tests.ContractShape;
using System.Reflection;
using Xunit;

namespace HoneyDrunk.Knowledge.Abstractions.Tests;

/// <summary>The contract-shape canary for the frozen Knowledge surfaces (ADR-0021, invariant 148).</summary>
public sealed class ContractShapeTests
{
    private const string BaselinePath = "api/HoneyDrunk.Knowledge.Abstractions.contract-shape.txt";

    private static readonly Type[] Roots =
    [
        typeof(IKnowledgeStore),
        typeof(IDocumentIngester),
        typeof(IRetrievalPipeline),
        typeof(KnowledgeSource),
    ];

    /// <summary>
    /// The frozen contracts match the committed shape. Set <c>HONEYDRUNK_UPDATE_CONTRACT_SHAPE=1</c>
    /// to regenerate it, which is refused unless <c>Version</c> has risen.
    /// </summary>
    [Fact]
    [Trait("Category", "ContractShape")]
    public void Contract_shape_matches_the_frozen_file()
    {
        var shape = "# package: HoneyDrunk.Knowledge.Abstractions\n"
            + "# roots: " + string.Join(", ", Roots.Select(root => root.Name)) + "\n"
            + ContractShapeRenderer.Render(Roots);
        var baseline = Path.Join(RepositoryRoot.Find(), BaselinePath.Replace('/', Path.DirectorySeparatorChar));
        var informational = typeof(KnowledgeSource).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException("The Abstractions assembly has no informational version.");
        var update = Environment.GetEnvironmentVariable("HONEYDRUNK_UPDATE_CONTRACT_SHAPE") == "1";

        var (passed, message) = ContractShapeCheck.Verify(shape, baseline, informational.Split('+')[0], update);

        Assert.True(passed, message);
    }

    /// <summary>The frozen file renders every surface and names the embedding record by its full name.</summary>
    [Fact]
    [Trait("Category", "ContractShape")]
    public void Contract_shape_names_the_surfaces_and_the_embedding_record()
    {
        var shape = ContractShapeRenderer.Render(Roots);

        Assert.Contains("type HoneyDrunk.Knowledge.Abstractions.IKnowledgeStore: interface", shape, StringComparison.Ordinal);
        Assert.Contains("type HoneyDrunk.Knowledge.Abstractions.IDocumentIngester: interface", shape, StringComparison.Ordinal);
        Assert.Contains("type HoneyDrunk.Knowledge.Abstractions.IRetrievalPipeline: interface", shape, StringComparison.Ordinal);
        Assert.Contains("type HoneyDrunk.Knowledge.Abstractions.KnowledgeSource: sealed class", shape, StringComparison.Ordinal);
        Assert.Contains("type HoneyDrunk.Knowledge.Abstractions.KnowledgeChunk: sealed class", shape, StringComparison.Ordinal);
        Assert.Contains("type HoneyDrunk.Knowledge.Abstractions.SimilarityQuery: sealed class", shape, StringComparison.Ordinal);
        Assert.Contains("type HoneyDrunk.Knowledge.Abstractions.RetrievedChunk: sealed class", shape, StringComparison.Ordinal);
        Assert.Contains("HoneyDrunk.AI.Abstractions.Embeddings.Embedding Embedding { get; }", shape, StringComparison.Ordinal);
        Assert.Contains("HoneyDrunk.AI.Abstractions.Embeddings.Embedding Query { get; }", shape, StringComparison.Ordinal);
        Assert.DoesNotContain("type HoneyDrunk.AI.", shape, StringComparison.Ordinal);
    }
}
