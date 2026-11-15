using HoneyDrunk.AI.Abstractions.Embeddings;
using HoneyDrunk.AI.Providers.InMemory;
using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Context;
using HoneyDrunk.Kernel.Hosting;
using HoneyDrunk.Kernel.Telemetry;
using HoneyDrunk.Knowledge.Abstractions;
using HoneyDrunk.Knowledge.Providers.InMemory;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Xunit;

namespace HoneyDrunk.Knowledge.Canary;

/// <summary>Checks Knowledge's activities against HoneyDrunk.Kernel's real telemetry and grid context.</summary>
public sealed class KernelCanaryTests
{
    private const string CorrelationId = "corr-knowledge-canary";

    /// <summary>
    /// With Kernel's real activity factory and a grid context in place, an ingest and a retrieval each start
    /// their <c>knowledge.*</c> activities, and some tag on every one of them carries the grid correlation id.
    /// The canary does not name Kernel's tag, only that the id reaches the activity.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Grid_context_correlation_reaches_the_ingest_and_retrieve_activities()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHoneyDrunkNode(options =>
        {
            options.NodeId = new("knowledge-canary");
            options.SectorId = new("core");
            options.StudioId = new("test-studio");
            options.EnvironmentId = new("test");
        }).AddTelemetry();

        // A host with no HTTP request (a job) swaps in Kernel's ambient accessor; the node's default reads the request.
        services.AddSingleton<IGridContextAccessor, ScopedGridContextAccessor>();
        services.AddSingleton<IEmbeddingGenerator>(new InMemoryEmbeddingGenerator("model-a"));
        services.AddHoneyDrunkKnowledge<InMemoryKnowledgeStore>(options => options.EmbeddingModelId = "model-a");
        await using var provider = services.BuildServiceProvider();

        var started = new List<StartedActivity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName.StartsWith("knowledge.", StringComparison.Ordinal))
                {
                    lock (started)
                    {
                        started.Add(new StartedActivity(activity.OperationName, [.. activity.TagObjects.Select(tag => tag.Value?.ToString())]));
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        await using (var scope = provider.CreateAsyncScope())
        {
            using (ScopedGridContextAccessor.SetContext(new GridContextSnapshot("knowledge-canary", "test-studio", "test", correlationId: CorrelationId)))
            {
                await scope.ServiceProvider.GetRequiredService<IDocumentIngester>().IngestAsync(
                    new KnowledgeDocument("docs:intro", "https://example.test/guide", "v1", "A guide passage.\n\nAnother guide passage."));
                await scope.ServiceProvider.GetRequiredService<IRetrievalPipeline>().RetrieveAsync(
                    new RetrievalRequest("A guide passage.", ["docs:intro"]));
            }
        }

        string[] names = [.. started.Select(activity => activity.Name).Distinct()];
        Assert.Contains("knowledge.ingest", names);
        Assert.Contains("knowledge.retrieve", names);
        Assert.Contains("knowledge.store", names);
        foreach (var activity in started)
        {
            Assert.Contains(CorrelationId, activity.TagValues);
        }
    }

    private sealed record StartedActivity(string Name, string?[] TagValues);
}
