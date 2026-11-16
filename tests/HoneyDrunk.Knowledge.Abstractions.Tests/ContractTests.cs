using HoneyDrunk.AI.Abstractions.Embeddings;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace HoneyDrunk.Knowledge.Abstractions.Tests;

/// <summary>The frozen surfaces, their members and where they name the embedding contracts (ADR-0021 D5).</summary>
public sealed class ContractTests
{
    private const string GeneratorCref = "T:HoneyDrunk.AI.Abstractions.Embeddings.IEmbeddingGenerator";

    /// <summary>The store, the ingester, the pipeline and the source record have exactly their pinned members.</summary>
    [Fact]
    public void The_four_frozen_surfaces_exist_with_their_members()
    {
        Assert.Equal(
            [
                "DeleteSourceAsync(System.String, System.Threading.CancellationToken)",
                "GetSourceAsync(System.String, System.Threading.CancellationToken)",
                "SearchAsync(HoneyDrunk.Knowledge.Abstractions.SimilarityQuery, System.Threading.CancellationToken)",
                "StoreVersionAsync(HoneyDrunk.Knowledge.Abstractions.KnowledgeSource, System.Collections.Generic.IReadOnlyList`1[HoneyDrunk.Knowledge.Abstractions.KnowledgeChunk], System.Threading.CancellationToken)",
            ],
            Methods(typeof(IKnowledgeStore)));
        Assert.Equal(typeof(Task<KnowledgeSource?>), typeof(IKnowledgeStore).GetMethod(nameof(IKnowledgeStore.GetSourceAsync))!.ReturnType);
        Assert.Equal(typeof(Task<IngestOutcome>), typeof(IKnowledgeStore).GetMethod(nameof(IKnowledgeStore.StoreVersionAsync))!.ReturnType);
        Assert.Equal(typeof(Task<RetrievalResult>), typeof(IKnowledgeStore).GetMethod(nameof(IKnowledgeStore.SearchAsync))!.ReturnType);
        Assert.Equal(typeof(Task<bool>), typeof(IKnowledgeStore).GetMethod(nameof(IKnowledgeStore.DeleteSourceAsync))!.ReturnType);

        Assert.Equal(["IngestAsync(HoneyDrunk.Knowledge.Abstractions.KnowledgeDocument, System.Threading.CancellationToken)"], Methods(typeof(IDocumentIngester)));
        Assert.Equal(typeof(Task<IngestResult>), typeof(IDocumentIngester).GetMethod(nameof(IDocumentIngester.IngestAsync))!.ReturnType);

        Assert.Equal(["RetrieveAsync(HoneyDrunk.Knowledge.Abstractions.RetrievalRequest, System.Threading.CancellationToken)"], Methods(typeof(IRetrievalPipeline)));
        Assert.Equal(typeof(Task<RetrievalResult>), typeof(IRetrievalPipeline).GetMethod(nameof(IRetrievalPipeline.RetrieveAsync))!.ReturnType);

        var properties = typeof(KnowledgeSource).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.PropertyType.Name + " " + property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["DateTimeOffset LastUpdatedAt", "String EmbeddingModelId", "String Origin", "String SourceId", "String Version"], properties);
    }

    /// <summary>Stored chunks and similarity queries carry AI's embedding record: text, vector and model together.</summary>
    [Fact]
    public void Chunks_and_queries_carry_the_AI_embedding_record()
    {
        Assert.Equal(typeof(Embedding), typeof(KnowledgeChunk).GetProperty(nameof(KnowledgeChunk.Embedding))!.PropertyType);
        Assert.Equal(typeof(Embedding), typeof(SimilarityQuery).GetProperty(nameof(SimilarityQuery.Query))!.PropertyType);
        Assert.Equal("HoneyDrunk.AI.Abstractions.Embeddings.Embedding", typeof(Embedding).FullName);
    }

    /// <summary>The ingester and the pipeline name the generator they delegate to in their documentation.</summary>
    [Fact]
    public void The_ingester_and_pipeline_name_the_embedding_generator()
    {
        var file = Path.ChangeExtension(typeof(IKnowledgeStore).Assembly.Location, ".xml");
        Assert.True(File.Exists(file), "The XML documentation file of the Abstractions assembly was not copied to the test output.");
        var members = XDocument.Load(file).Descendants("member").ToArray();

        Assert.True(SummaryNamesGenerator(members, typeof(IDocumentIngester)), "IDocumentIngester's summary does not name IEmbeddingGenerator.");
        Assert.True(SummaryNamesGenerator(members, typeof(IRetrievalPipeline)), "IRetrievalPipeline's summary does not name IEmbeddingGenerator.");
    }

    private static string[] Methods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name + "(" + string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.ToString())) + ")")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool SummaryNamesGenerator(IEnumerable<XElement> members, Type type) =>
        members
            .Where(member => (string?)member.Attribute("name") == "T:" + type.FullName)
            .SelectMany(member => member.Elements("summary"))
            .SelectMany(summary => summary.Descendants("see"))
            .Any(see => (string?)see.Attribute("cref") == GeneratorCref);
}
