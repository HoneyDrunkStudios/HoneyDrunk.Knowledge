using HoneyDrunk.AI.Abstractions.Embeddings;
using HoneyDrunk.AI.Providers.InMemory;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Knowledge.Abstractions;
using HoneyDrunk.Knowledge.Providers.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HoneyDrunk.Knowledge.Tests;

/// <summary>Service registration.</summary>
public sealed class RegistrationTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The store and the ingester resolve per scope over one singleton provider, the store as the observed
    /// wrapper, and the composition validates. The retrieval pipeline, the third service, joins in the next change.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Registration_resolves_the_three_services_scoped_over_one_singleton_store()
    {
        var collection = Compose();
        collection.AddHoneyDrunkKnowledge<InMemoryKnowledgeStore>(options => options.EmbeddingModelId = "model-a");
        await using var services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(collection, item => item.ServiceType == typeof(InMemoryKnowledgeStore)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(collection, item => item.ServiceType == typeof(IKnowledgeStore)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(collection, item => item.ServiceType == typeof(IDocumentIngester)).Lifetime);
        Assert.Single(collection, item => item.ServiceType == typeof(TimeProvider));
        var options = services.GetRequiredService<IOptions<KnowledgeOptions>>().Value;
        Assert.Equal("model-a", options.EmbeddingModelId);
        Assert.Equal(64, options.EmbeddingBatchSize);

        await using var first = services.CreateAsyncScope();
        await using var second = services.CreateAsyncScope();
        var firstStore = Assert.IsType<ObservedKnowledgeStore>(first.ServiceProvider.GetRequiredService<IKnowledgeStore>());
        var secondStore = Assert.IsType<ObservedKnowledgeStore>(second.ServiceProvider.GetRequiredService<IKnowledgeStore>());
        Assert.NotSame(firstStore, secondStore);
        Assert.Same(firstStore, first.ServiceProvider.GetRequiredService<IKnowledgeStore>());
        var ingester = Assert.IsType<DocumentIngester>(first.ServiceProvider.GetRequiredService<IDocumentIngester>());
        Assert.NotSame(ingester, second.ServiceProvider.GetRequiredService<IDocumentIngester>());

        var result = await ingester.IngestAsync(new KnowledgeDocument("docs:intro", "https://example.test/guide", "v1", "A short guide."));

        Assert.Equal(Start, result.Source.LastUpdatedAt);
        Assert.Equal("v1", (await secondStore.GetSourceAsync("docs:intro"))!.Version);
        Assert.Equal("v1", (await services.GetRequiredService<InMemoryKnowledgeStore>().GetSourceAsync("docs:intro"))!.Version);
    }

    /// <summary>
    /// A provider registered before or after the runtime, as a provider's own registration does (it replaces
    /// any registration of its type), leaves one store type registration, and every scope reaches that instance.
    /// </summary>
    /// <param name="providerFirst">Whether the provider is registered before the runtime.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Registration_in_either_order_with_a_provider_keeps_one_store(bool providerFirst)
    {
        var provided = new InMemoryKnowledgeStore();
        var collection = Compose();
        if (providerFirst)
        {
            AddProvider(collection, provided);
            collection.AddHoneyDrunkKnowledge<InMemoryKnowledgeStore>(options => options.EmbeddingModelId = "model-a");
        }
        else
        {
            collection.AddHoneyDrunkKnowledge<InMemoryKnowledgeStore>(options => options.EmbeddingModelId = "model-a");
            AddProvider(collection, provided);
        }

        await using var services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Assert.Single(collection, item => item.ServiceType == typeof(InMemoryKnowledgeStore));
        Assert.Same(provided, services.GetRequiredService<InMemoryKnowledgeStore>());
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDocumentIngester>()
                .IngestAsync(new KnowledgeDocument("docs:intro", "https://example.test/guide", "v1", "A short guide."));
        }

        Assert.Equal("v1", (await provided.GetSourceAsync("docs:intro"))!.Version);
        await using var other = services.CreateAsyncScope();
        Assert.Equal("v1", (await other.ServiceProvider.GetRequiredService<IKnowledgeStore>().GetSourceAsync("docs:intro"))!.Version);
    }

    /// <summary>A null service collection or configuration is refused.</summary>
    [Fact]
    public void Registration_refuses_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => KnowledgeServiceCollectionExtensions.AddHoneyDrunkKnowledge<InMemoryKnowledgeStore>(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddHoneyDrunkKnowledge<InMemoryKnowledgeStore>(null!));
    }

    // A provider's registration owns its type: it removes any earlier one and adds its own instance.
    private static void AddProvider(IServiceCollection services, InMemoryKnowledgeStore store)
    {
        services.RemoveAll<InMemoryKnowledgeStore>();
        services.AddSingleton(store);
    }

    private static ServiceCollection Compose()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
        services.AddScoped<ITelemetryActivityFactory, RecordingTelemetryActivityFactory>();
        services.AddSingleton<IEmbeddingGenerator>(new InMemoryEmbeddingGenerator("model-a"));
        return services;
    }
}
