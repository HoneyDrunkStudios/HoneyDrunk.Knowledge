using HoneyDrunk.AI.Abstractions.Embeddings;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Knowledge.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HoneyDrunk.Knowledge;

/// <summary>Registers the HoneyDrunk.Knowledge runtime.</summary>
public static class KnowledgeServiceCollectionExtensions
{
    /// <summary>
    /// Adds the knowledge runtime over a provider. The provider is registered as its concrete type only, as a
    /// singleton and only if the host or the provider's own registration has not registered it already, so the
    /// two can be called in either order. <see cref="IKnowledgeStore"/> resolves per scope to a wrapper that runs
    /// each call over the provider in a telemetry activity, and <see cref="IDocumentIngester"/> is scoped. The
    /// host supplies <see cref="IEmbeddingGenerator"/>, <see cref="ITelemetryActivityFactory"/> and logging;
    /// <see cref="TimeProvider.System"/> is added unless the host registered a clock. The options are checked on
    /// first use.
    /// </summary>
    /// <typeparam name="TStore">The provider type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options; <see cref="KnowledgeOptions.EmbeddingModelId"/> is required.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddHoneyDrunkKnowledge<TStore>(this IServiceCollection services, Action<KnowledgeOptions> configure)
        where TStore : class, IKnowledgeStore
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddOptions<KnowledgeOptions>()
            .Configure(configure)
            .Validate(KnowledgeOptions.HasValidModelId, KnowledgeOptions.EmbeddingModelIdRule)
            .Validate(KnowledgeOptions.HasValidBatchSize, KnowledgeOptions.EmbeddingBatchSizeRule);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TStore>();
        services.AddScoped<IKnowledgeStore>(provider => new ObservedKnowledgeStore(
            provider.GetRequiredService<TStore>(),
            typeof(TStore).Name,
            provider.GetRequiredService<ITelemetryActivityFactory>(),
            provider.GetRequiredService<ILogger<ObservedKnowledgeStore>>()));
        services.AddScoped<IDocumentIngester, DocumentIngester>();
        return services;
    }
}
