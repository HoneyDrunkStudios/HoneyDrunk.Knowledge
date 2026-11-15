using HoneyDrunk.AI.Abstractions.Embeddings;
using HoneyDrunk.AI.Providers.InMemory;
using HoneyDrunk.Data.Abstractions.Diagnostics;
using HoneyDrunk.Data.Abstractions.Transactions;
using HoneyDrunk.Data.EntityFramework.Registration;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Knowledge.Abstractions;
using HoneyDrunk.Knowledge.Canary;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static HoneyDrunk.Knowledge.Providers.Contract.TestSources;

namespace HoneyDrunk.Knowledge.Providers.Data.Tests;

/// <summary>How a host composes the provider, and what survives a restart.</summary>
public sealed class RegistrationTests : IDisposable
{
    private const string Passage = "Stored passages survive a restart.\n\nThey are ranked by cosine similarity.";

    private readonly SqliteDatabase database = new();

    /// <summary>
    /// The runtime over the Data provider, registered in either order: one provider registration, one singleton
    /// behind every scope, and none of HoneyDrunk.Data's non-generic unit-of-work services taken for the
    /// provider's context. A document ingested through the runtime is retrieved, and is in the database.
    /// </summary>
    /// <param name="providerFirst">Whether the provider is registered before the runtime.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Registration_in_either_order_keeps_one_store_and_no_unit_of_work_of_ours(bool providerFirst)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEmbeddingGenerator>(new InMemoryEmbeddingGenerator(ModelA));
        services.AddScoped<ITelemetryActivityFactory>(_ => new NullTelemetryActivityFactory());
        if (providerFirst)
        {
            services.AddKnowledgeDataProvider(options => this.database.Configure(options));
            services.AddHoneyDrunkKnowledge<DataKnowledgeStore>(options => options.EmbeddingModelId = ModelA);
        }
        else
        {
            services.AddHoneyDrunkKnowledge<DataKnowledgeStore>(options => options.EmbeddingModelId = ModelA);
            services.AddKnowledgeDataProvider(options => this.database.Configure(options));
        }

        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == typeof(DataKnowledgeStore));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.DoesNotContain(services, candidate => candidate.ServiceType == typeof(IUnitOfWork) || candidate.ServiceType == typeof(IUnitOfWorkFactory));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var singleton = provider.GetRequiredService<DataKnowledgeStore>();

        await using (var scope = provider.CreateAsyncScope())
        {
            var ingested = await scope.ServiceProvider.GetRequiredService<IDocumentIngester>()
                .IngestAsync(new KnowledgeDocument("docs:intro", "https://example.test/guide", "v1", Passage));
            var retrieved = await scope.ServiceProvider.GetRequiredService<IRetrievalPipeline>()
                .RetrieveAsync(new RetrievalRequest(Passage, ["docs:intro"]));

            Assert.Equal(IngestOutcome.Added, ingested.Outcome);
            var top = Assert.Single(retrieved.Chunks);
            Assert.Equal(Passage, top.Text);
            Assert.InRange(top.Score, 1 - 1e-9, 1);
            Assert.Same(singleton, scope.ServiceProvider.GetRequiredService<DataKnowledgeStore>());
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Same(singleton, scope.ServiceProvider.GetRequiredService<DataKnowledgeStore>());
            Assert.Equal("v1", (await scope.ServiceProvider.GetRequiredService<IKnowledgeStore>().GetSourceAsync("docs:intro"))!.Version);
        }

        Assert.Equal(ModelA, (await this.database.CreateStore().GetSourceAsync("docs:intro"))!.EmbeddingModelId);
    }

    /// <summary>
    /// A host that turns on sensitive data logging is refused when the context is created, because Entity
    /// Framework would write passages and vectors into logs; nothing is stored and the message holds no content.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Sensitive_data_logging_is_refused()
    {
        await using var provider = this.Host(options => options.EnableSensitiveDataLogging());

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<DataKnowledgeStore>().StoreVersionAsync(Source(), Chunks("never logged", ModelA, [1f, 0f])));
        var direct = Assert.Throws<InvalidOperationException>(
            () => new KnowledgeDbContext(new DbContextOptionsBuilder<KnowledgeDbContext>().UseSqlite(this.database.ConnectionString).EnableSensitiveDataLogging().Options));

        Assert.Contains("sensitive data logging", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("never logged", refused.ToString(), StringComparison.Ordinal);
        Assert.Equal(refused.Message, direct.Message);
        Assert.Null(await this.database.CreateStore().GetSourceAsync("docs:intro"));
    }

    /// <summary>
    /// A host that registers HoneyDrunk.Data's scoped diagnostics context and validates scopes can still use the
    /// store: the provider's singleton options never ask for a scoped service.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_scoped_diagnostics_context_does_not_break_scope_validation()
    {
        var services = new ServiceCollection();
        services.AddScoped<IDataDiagnosticsContext>(_ => new FixedDiagnosticsContext());
        services.AddKnowledgeDataProvider(options => this.database.Configure(options));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var outcome = await provider.GetRequiredService<DataKnowledgeStore>().StoreVersionAsync(Source(), Chunks("validated", ModelA, [1f, 0f]));

        Assert.Equal(IngestOutcome.Added, outcome);
        Assert.NotNull(await this.database.CreateStore().GetSourceAsync("docs:intro"));
    }

    /// <summary>
    /// The provider never takes HoneyDrunk.Data's non-generic unit of work for its own context: a host that
    /// registers its context later still gets its own, and saves through it.
    /// </summary>
    /// <param name="hostFirst">Whether the host registers its context before the provider.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_host_context_owns_the_unit_of_work_in_either_order(bool hostFirst)
    {
        var services = new ServiceCollection();
        if (hostFirst)
        {
            services.AddHoneyDrunkDataEntityFramework<HostDbContext>(options => this.database.Configure(options));
            services.AddKnowledgeDataProvider(options => this.database.Configure(options));
        }
        else
        {
            services.AddKnowledgeDataProvider(options => this.database.Configure(options));
            services.AddHoneyDrunkDataEntityFramework<HostDbContext>(options => this.database.Configure(options));
        }

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.IsAssignableFrom<IUnitOfWork<HostDbContext>>(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        Assert.Equal([typeof(HostDbContext)], provider.GetRequiredService<IUnitOfWorkFactory>().GetType().GenericTypeArguments);
        Assert.NotNull(provider.GetRequiredService<DataKnowledgeStore>());
    }

    /// <summary>
    /// Versions, their lineage, chunks, scores and the result order are the same from a new service provider over
    /// the same file.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Restart_keeps_versions_chunks_and_order()
    {
        var query = Query([1f, 0f], ["docs:c", "docs:a", "docs:b"]);
        var later = TimeSpan.FromTicks(1_234_567);
        string[] before;
        await using (var host = this.Host())
        {
            var store = host.GetRequiredService<DataKnowledgeStore>();
            await store.StoreVersionAsync(Source("docs:a", version: "v1"), Chunks("a1", ModelA, [1f, 0f], [0f, 1f]));
            await store.StoreVersionAsync(Source("docs:a", version: "v2", updatedAfter: later), Chunks("a2", ModelA, [1f, 1f], [1f, 0f], [0f, 1f]));
            await store.StoreVersionAsync(Source("docs:b", updatedAfter: TimeSpan.FromMinutes(3)), Chunks("b\r\nline ", ModelA, [2f, 1f], [1f, 0f]));
            await store.StoreVersionAsync(Source("docs:c", model: ModelB), Chunks("c", ModelB, [1f, 0f]));
            before = Describe(await store.SearchAsync(query));
        }

        await using var after = this.Host();
        var restarted = after.GetRequiredService<DataKnowledgeStore>();

        Assert.Equal(
            [
                "docs:a|v2|" + (Start + later).UtcTicks + "|model-a|1|a2 1|1",
                "docs:b|v1|" + (Start + TimeSpan.FromMinutes(3)).UtcTicks + "|model-a|1|b\r\nline  1|1",
                "docs:b|v1|" + (Start + TimeSpan.FromMinutes(3)).UtcTicks + "|model-a|0|b\r\nline  0|0.8944271909999159",
                "docs:a|v2|" + (Start + later).UtcTicks + "|model-a|0|a2 0|0.7071067811865475",
                "docs:a|v2|" + (Start + later).UtcTicks + "|model-a|2|a2 2|0",
                "mismatched:docs:c",
            ],
            before);
        Assert.Equal(before, Describe(await restarted.SearchAsync(query)));
        var a = (await restarted.GetSourceAsync("docs:a"))!;
        Assert.Equal("v2", a.Version);
        Assert.Equal((Start + later).UtcTicks, a.LastUpdatedAt.UtcTicks);
        Assert.Equal(ModelA, a.EmbeddingModelId);
        await using var context = this.database.CreateContext();
        var lineage = await context.SourceVersions.AsNoTracking().Where(row => row.SourceId == "docs:a").ToListAsync();
        Assert.Equal(
            ["v1|" + (Start + later).UtcTicks + "|2", "v2|current|3"],
            lineage.OrderBy(row => row.Version, StringComparer.Ordinal).Select(row => $"{row.Version}|{row.SupersededAt?.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "current"}|{row.ChunkCount}"));
    }

    /// <summary>The registration refuses null arguments.</summary>
    [Fact]
    public void Registration_refuses_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => KnowledgeDataServiceCollectionExtensions.AddKnowledgeDataProvider(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddKnowledgeDataProvider(null!));
    }

    /// <inheritdoc />
    public void Dispose() => this.database.Dispose();

    private static string[] Describe(RetrievalResult result) =>
    [
        .. result.Chunks.Select(chunk => string.Join(
            '|',
            chunk.Source.SourceId,
            chunk.Source.Version,
            chunk.Source.LastUpdatedAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            chunk.Source.EmbeddingModelId,
            chunk.Position.ToString(System.Globalization.CultureInfo.InvariantCulture),
            chunk.Text,
            chunk.Score.ToString("R", System.Globalization.CultureInfo.InvariantCulture))),
        "mismatched:" + string.Join(',', result.MismatchedSourceIds),
    ];

    private ServiceProvider Host(Action<DbContextOptionsBuilder>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddKnowledgeDataProvider(options =>
        {
            this.database.Configure(options);
            extra?.Invoke(options);
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>A context a host owns, registered beside the provider's.</summary>
    /// <param name="options">The context options.</param>
    public sealed class HostDbContext(DbContextOptions<HostDbContext> options) : DbContext(options)
    {
    }

    private sealed class FixedDiagnosticsContext : IDataDiagnosticsContext
    {
        public string? CorrelationId => "correlation-1";

        public string? OperationId => "operation-1";

        public string? NodeId => "node-1";

        public IReadOnlyDictionary<string, string> Tags { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
