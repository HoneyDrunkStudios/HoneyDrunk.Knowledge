using HoneyDrunk.Data.Abstractions.Transactions;
using HoneyDrunk.Data.EntityFramework.Registration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HoneyDrunk.Knowledge.Providers.Data;

/// <summary>Registers the durable knowledge provider.</summary>
public static class KnowledgeDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the knowledge tables' context through HoneyDrunk.Data and <see cref="DataKnowledgeStore"/> as a
    /// singleton. Compose it behind the runtime with <c>AddHoneyDrunkKnowledge&lt;DataKnowledgeStore&gt;(...)</c>,
    /// called before or after this method. The host's database project carries the tables; this package ships no
    /// migrations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureDbContext">
    /// Configures the database provider, for example <c>UseSqlServer(connection, sql =&gt; sql.EnableRetryOnFailure())</c>.
    /// Sensitive data logging must stay off: with it on, Entity Framework writes parameter values, which are
    /// passages and vectors, into logs, so the context refuses to be created.
    /// </param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddKnowledgeDataProvider(this IServiceCollection services, Action<DbContextOptionsBuilder> configureDbContext)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureDbContext);
        var hostOwnsUnitOfWork = services.Any(descriptor => descriptor.ServiceType == typeof(IUnitOfWork));
        var hostOwnsUnitOfWorkFactory = services.Any(descriptor => descriptor.ServiceType == typeof(IUnitOfWorkFactory));

        // The correlation interceptor is off: it reads a scoped diagnostics context, which the singleton options
        // cannot resolve in a host that validates scopes.
        services.AddHoneyDrunkDataEntityFramework<KnowledgeDbContext>(
            configureDbContext,
            efOptions => efOptions.EnableCorrelationInterceptor = false);
        KeepOptionsConfigurationSingleton(services);

        // HoneyDrunk.Data gives its non-generic unit of work to the first context registered. That must never be
        // the knowledge context: a host that registers its own context later would save through the wrong one,
        // silently, and would hold a handle on the knowledge tables.
        if (!hostOwnsUnitOfWork)
        {
            services.RemoveAll<IUnitOfWork>();
        }

        if (!hostOwnsUnitOfWorkFactory)
        {
            services.RemoveAll<IUnitOfWorkFactory>();
        }

        services.TryAddSingleton<KnowledgeDataService>();

        // The runtime registers the provider type with TryAdd and resolves it by that type, so this registration
        // must be the only one whichever method the host calls first.
        services.RemoveAll<DataKnowledgeStore>();
        services.AddSingleton(provider => new DataKnowledgeStore(provider.GetRequiredService<KnowledgeDataService>()));
        return services;
    }

    /// <summary>
    /// HoneyDrunk.Data registers the context both as a singleton factory and as a scoped context, which leaves the
    /// singleton options depending on a scoped options configuration. A host that validates scopes (the default in
    /// the Development environment) then cannot create a context from the factory. The configuration holds only
    /// the host's delegate, so it is safe as a singleton.
    /// </summary>
    private static void KeepOptionsConfigurationSingleton(IServiceCollection services)
    {
        for (var i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(IDbContextOptionsConfiguration<KnowledgeDbContext>)
                && descriptor.Lifetime != ServiceLifetime.Singleton
                && descriptor.ImplementationFactory is { } factory)
            {
                services[i] = ServiceDescriptor.Singleton(descriptor.ServiceType, factory);
            }
        }
    }
}
