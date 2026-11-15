using HoneyDrunk.AI.Abstractions.Embeddings;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Knowledge.Abstractions;
using HoneyDrunk.Knowledge.Providers.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace HoneyDrunk.Knowledge.Tests;

/// <summary>
/// One composed runtime over an in-memory store, a fake clock, a recording telemetry factory and a list logger.
/// Each call runs in its own scope, as a host's request would. Two rigs given one store are two hosts, or two
/// configurations, over the same storage.
/// </summary>
internal sealed class RuntimeRig : IDisposable
{
    /// <summary>The model the rig is configured with unless a test says otherwise.</summary>
    public const string Model = "model-a";

    /// <summary>The origin of every test document.</summary>
    public const string Origin = "https://example.test/guide";

    /// <summary>The instant the fake clock starts at.</summary>
    public static readonly DateTimeOffset Start = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Initializes a new instance of the <see cref="RuntimeRig"/> class.</summary>
    /// <param name="generator">The embedding generator.</param>
    /// <param name="modelId">The configured model id.</param>
    /// <param name="store">The store to share, or null for a new one.</param>
    public RuntimeRig(ScriptedEmbeddingGenerator generator, string modelId = Model, InMemoryKnowledgeStore? store = null)
    {
        this.Generator = generator;
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(this.Logs);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<TimeProvider>(this.Time);
        services.AddSingleton<ITelemetryActivityFactory>(this.Telemetry);
        services.AddSingleton<IEmbeddingGenerator>(generator);
        if (store is not null)
        {
            services.AddSingleton(store);
        }

        services.AddHoneyDrunkKnowledge<InMemoryKnowledgeStore>(options => options.EmbeddingModelId = modelId);
        this.Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        this.Store = this.Services.GetRequiredService<InMemoryKnowledgeStore>();
    }

    /// <summary>Gets the fake clock.</summary>
    public FakeTimeProvider Time { get; } = new(Start);

    /// <summary>Gets the recording telemetry factory.</summary>
    public RecordingTelemetryActivityFactory Telemetry { get; } = new();

    /// <summary>Gets the list logger.</summary>
    public ListLogger Logs { get; } = new();

    /// <summary>Gets the embedding generator.</summary>
    public ScriptedEmbeddingGenerator Generator { get; }

    /// <summary>Gets the composed services.</summary>
    public ServiceProvider Services { get; }

    /// <summary>Gets the provider instance behind every scope's <see cref="IKnowledgeStore"/>.</summary>
    public InMemoryKnowledgeStore Store { get; }

    /// <summary>Makes a document of whole passages, each one chunk.</summary>
    /// <param name="sourceId">The source id.</param>
    /// <param name="version">The version.</param>
    /// <param name="passages">How many passages.</param>
    /// <param name="mark">Text every passage carries.</param>
    /// <returns>The document.</returns>
    public static KnowledgeDocument Document(string sourceId, string version, int passages, string mark = "") =>
        new(sourceId, Origin, version, string.Join("\n\n", Enumerable.Range(0, passages).Select(index => Passage(sourceId, version, index, mark))));

    /// <summary>
    /// Makes one passage of about 730 characters: two never fit in one 1,200-character chunk, so each passage is
    /// one chunk, and its text is the chunk's text exactly.
    /// </summary>
    /// <param name="sourceId">The source id.</param>
    /// <param name="version">The version.</param>
    /// <param name="index">The passage's place.</param>
    /// <param name="mark">Text the passage carries.</param>
    /// <returns>The passage.</returns>
    public static string Passage(string sourceId, string version, int index, string mark = "") =>
        $"Passage {index} of {sourceId} {version}. {mark} {new string('x', 700)}";

    /// <summary>Ingests a document in a new scope.</summary>
    /// <param name="document">The document.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The ingest result.</returns>
    public Task<IngestResult> IngestAsync(KnowledgeDocument document, CancellationToken cancellationToken = default) =>
        this.InScopeAsync(services => services.GetRequiredService<IDocumentIngester>().IngestAsync(document, cancellationToken));

    /// <summary>Retrieves in a new scope.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The retrieval result.</returns>
    public Task<RetrievalResult> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default) =>
        this.InScopeAsync(services => services.GetRequiredService<IRetrievalPipeline>().RetrieveAsync(request, cancellationToken));

    /// <summary>Runs a call against a new scope's services.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="call">The call.</param>
    /// <returns>The call's result.</returns>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> call)
    {
        await using var scope = this.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider);
    }

    /// <inheritdoc />
    public void Dispose() => this.Services.Dispose();
}
