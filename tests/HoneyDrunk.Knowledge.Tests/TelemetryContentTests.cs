using HoneyDrunk.AI.Abstractions.Embeddings;
using HoneyDrunk.Knowledge.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Globalization;
using Xunit;

namespace HoneyDrunk.Knowledge.Tests;

/// <summary>
/// The telemetry rule: each operation runs in one activity with an outcome, and no document text, chunk text,
/// query text or vector reaches an activity, a log entry or an exception message (invariant 147).
/// </summary>
public sealed class TelemetryContentTests
{
    private const string Intro = "docs:intro";
    private const string DocumentMark = "DOCUMENT-SENTINEL-5d2a";
    private const string QueryMark = "QUERY-SENTINEL-8c3f";

    private static readonly string[] ActivityNames = ["knowledge.ingest", "knowledge.retrieve", "knowledge.store"];

    private static readonly string[] TagNames =
    [
        "knowledge.operation",
        "knowledge.source_id",
        "knowledge.source_count",
        "knowledge.chunk_count",
        "knowledge.result_count",
        "knowledge.mismatched_count",
        "knowledge.embedding_model_id",
        "knowledge.provider",
        "knowledge.outcome",
    ];

    private static readonly string[] Outcomes = ["added", "superseded", "unchanged", "success", "error", "cancelled"];

    /// <summary>
    /// Document text and query text carry sentinels through every success path and every failing path (a generator
    /// that throws, a batch and a query answered by another model, a query answered for other text). Every activity
    /// name and tag name is pinned, and no tag value, log message, logged value or exception message holds a sentinel
    /// or a vector.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task No_content_or_query_in_telemetry_logs_or_errors()
    {
        var errors = new List<Exception>();
        using var rig = new RuntimeRig(new ScriptedEmbeddingGenerator(RuntimeRig.Model)
        {
            FailOnCall = 4,
            Rewrite = (call, embeddings) => call switch
            {
                5 or 6 => [.. embeddings.Select(embedding => embedding with { ModelId = "model-b" })],
                7 => [embeddings[0] with { Value = $"{QueryMark} another question" }],
                _ => embeddings,
            },
        });
        var query = new RetrievalRequest($"Where does {QueryMark} begin?", [Intro, "docs:guide"], 50);

        await rig.IngestAsync(RuntimeRig.Document(Intro, "v1", 2, DocumentMark));         // call 1: added
        await rig.IngestAsync(RuntimeRig.Document(Intro, "v1", 2, DocumentMark));         // no call: unchanged
        await rig.RetrieveAsync(query);                                                     // call 2: success
        rig.Time.Advance(TimeSpan.FromDays(1));
        await rig.IngestAsync(RuntimeRig.Document(Intro, "v2", 3, DocumentMark));         // call 3: superseded
        errors.Add(await Assert.ThrowsAnyAsync<Exception>(() => rig.IngestAsync(RuntimeRig.Document(Intro, "v3", 1, DocumentMark))));   // call 4 throws
        errors.Add(await Assert.ThrowsAnyAsync<Exception>(() => rig.IngestAsync(RuntimeRig.Document(Intro, "v3", 1, DocumentMark))));   // call 5: other model
        errors.Add(await Assert.ThrowsAnyAsync<Exception>(() => rig.RetrieveAsync(query)));                                          // call 6: other model
        errors.Add(await Assert.ThrowsAnyAsync<Exception>(() => rig.RetrieveAsync(query)));                                          // call 7: other text
        await rig.InScopeAsync(services => services.GetRequiredService<IKnowledgeStore>().GetSourceAsync(Intro));
        await rig.InScopeAsync(services => services.GetRequiredService<IKnowledgeStore>().DeleteSourceAsync(Intro));

        Assert.Equal(7, rig.Generator.Calls);
        Assert.Equal(4, errors.Count);
        Assert.All(errors, error =>
        {
            AssertHoldsNoContent(error.Message);
            AssertHoldsNoContent(error.ToString());
        });

        Assert.NotEmpty(rig.Telemetry.Activities);
        Assert.All(rig.Telemetry.Activities, activity =>
        {
            Assert.Contains(activity.OperationName, ActivityNames);
            Assert.All(activity.TagObjects, tag =>
            {
                Assert.Contains(tag.Key, TagNames);
                AssertIsNoVector(tag.Value);
                AssertHoldsNoContent(Convert.ToString(tag.Value, CultureInfo.InvariantCulture));
            });
        });

        Assert.NotEmpty(rig.Logs.Entries);
        Assert.Contains(rig.Logs.Entries, entry => entry.Message.Contains("InvalidOperationException", StringComparison.Ordinal));
        Assert.All(rig.Logs.Entries, entry =>
        {
            Assert.Null(entry.Exception);
            AssertHoldsNoContent(entry.Message);
            Assert.All(entry.Values, value =>
            {
                AssertIsNoVector(value.Value);
                AssertHoldsNoContent(Convert.ToString(value.Value, CultureInfo.InvariantCulture));
            });
        });
    }

    /// <summary>
    /// Each operation emits its activity, stopped and with an outcome from the pinned list: ingest added, superseded
    /// and unchanged; a retrieval; the store's get, store-version, search and delete; an error; a cancellation by the
    /// caller, before and during a call. Every other activity the case started has an outcome too.
    /// </summary>
    /// <param name="operation">The case.</param>
    /// <param name="activityName">The activity that case must emit last with that name.</param>
    /// <param name="operationTag">Its <c>knowledge.operation</c>.</param>
    /// <param name="outcome">Its <c>knowledge.outcome</c>.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData("ingest-added", "knowledge.ingest", "ingest", "added")]
    [InlineData("ingest-superseded", "knowledge.ingest", "ingest", "superseded")]
    [InlineData("ingest-unchanged", "knowledge.ingest", "ingest", "unchanged")]
    [InlineData("ingest-error", "knowledge.ingest", "ingest", "error")]
    [InlineData("ingest-cancelled", "knowledge.ingest", "ingest", "cancelled")]
    [InlineData("ingest-cancelled-during-the-call", "knowledge.ingest", "ingest", "cancelled")]
    [InlineData("retrieve", "knowledge.retrieve", "retrieve", "success")]
    [InlineData("retrieve-error", "knowledge.retrieve", "retrieve", "error")]
    [InlineData("retrieve-cancelled", "knowledge.retrieve", "retrieve", "cancelled")]
    [InlineData("retrieve-cancelled-during-the-call", "knowledge.retrieve", "retrieve", "cancelled")]
    [InlineData("store-version", "knowledge.store", "store_version", "added")]
    [InlineData("store-get", "knowledge.store", "get", "success")]
    [InlineData("store-search", "knowledge.store", "search", "success")]
    [InlineData("store-delete", "knowledge.store", "delete", "success")]
    [InlineData("store-error", "knowledge.store", "store_version", "error")]
    [InlineData("store-cancelled", "knowledge.store", "get", "cancelled")]
    public async Task Every_operation_emits_its_activity_with_an_outcome(string operation, string activityName, string operationTag, string outcome)
    {
        using var caller = new CancellationTokenSource();
        var failing = operation is "ingest-error" or "retrieve-error" or "ingest-cancelled-during-the-call" or "retrieve-cancelled-during-the-call"
            ? 2
            : (int?)null;
        using var rig = new RuntimeRig(new ScriptedEmbeddingGenerator(RuntimeRig.Model)
        {
            FailOnCall = failing,
            FailWith = operation.EndsWith("during-the-call", StringComparison.Ordinal)
                ? () => CancelledBy(caller)
                : null,
        });
        if (operation is not "ingest-added" and not "store-version" and not "store-error" and not "store-cancelled")
        {
            await rig.IngestAsync(RuntimeRig.Document(Intro, "v1", 2));
        }

        await RunAsync(rig, operation, caller);

        var activity = rig.Telemetry.Named(activityName)[^1];
        Assert.Equal(operationTag, activity.GetTagItem("knowledge.operation"));
        Assert.Equal(outcome, activity.GetTagItem("knowledge.outcome"));
        Assert.All(rig.Telemetry.Activities, started =>
        {
            Assert.True(started.IsStopped);
            Assert.Contains(Assert.IsType<string>(started.GetTagItem("knowledge.outcome")), Outcomes);
        });
    }

    /// <summary>
    /// A generator's own timeout, a cancellation the caller did not ask for, is an error and is logged by its type
    /// name; only the caller's cancellation is <c>cancelled</c>. Ingest and retrieval follow the same rule.
    /// </summary>
    /// <param name="retrieve">Whether the timeout happens in a retrieval rather than an ingest.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_generator_timeout_is_an_error_not_a_cancellation(bool retrieve)
    {
        using var rig = new RuntimeRig(new ScriptedEmbeddingGenerator(RuntimeRig.Model)
        {
            FailOnCall = 2,
            FailWith = () => new TaskCanceledException("The scripted generator timed out."),
        });
        await rig.IngestAsync(RuntimeRig.Document(Intro, "v1", 1));

        var error = await Assert.ThrowsAsync<TaskCanceledException>(() => retrieve
            ? rig.RetrieveAsync(new RetrievalRequest("How do I begin?", [Intro]))
            : rig.IngestAsync(RuntimeRig.Document(Intro, "v2", 1)));

        Assert.Same(rig.Generator.Failure, error);
        var activity = rig.Telemetry.Named(retrieve ? "knowledge.retrieve" : "knowledge.ingest")[^1];
        Assert.Equal("error", activity.GetTagItem("knowledge.outcome"));
        Assert.Contains(rig.Logs.Entries, entry => entry.Message.Contains(nameof(TaskCanceledException), StringComparison.Ordinal));
    }

    /// <summary>A provider's own timeout is an error of the store operation and is logged by its type name.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task A_provider_timeout_is_an_error_not_a_cancellation()
    {
        var telemetry = new RecordingTelemetryActivityFactory();
        using var logs = new ListLogger();
        var store = new ObservedKnowledgeStore(new TimingOutStore(), nameof(TimingOutStore), telemetry, new Logger<ObservedKnowledgeStore>(logs));

        await Assert.ThrowsAsync<TaskCanceledException>(() => store.GetSourceAsync(Intro));

        var activity = Assert.Single(telemetry.Named("knowledge.store"));
        Assert.Equal("error", activity.GetTagItem("knowledge.outcome"));
        Assert.Equal(nameof(TimingOutStore), activity.GetTagItem("knowledge.provider"));
        Assert.Contains(logs.Entries, entry => entry.Message.Contains(nameof(TaskCanceledException), StringComparison.Ordinal));
    }

    private static async Task RunAsync(RuntimeRig rig, string operation, CancellationTokenSource caller)
    {
        var request = new RetrievalRequest("How do I begin?", [Intro]);
        switch (operation)
        {
            case "ingest-added":
            case "store-version":
                await rig.IngestAsync(RuntimeRig.Document(Intro, "v1", 2));
                break;
            case "ingest-superseded":
                await rig.IngestAsync(RuntimeRig.Document(Intro, "v2", 2));
                break;
            case "ingest-unchanged":
                await rig.IngestAsync(RuntimeRig.Document(Intro, "v1", 2));
                break;
            case "ingest-error":
                await Assert.ThrowsAsync<InvalidOperationException>(() => rig.IngestAsync(RuntimeRig.Document(Intro, "v2", 2)));
                break;
            case "ingest-cancelled":
                await caller.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.IngestAsync(RuntimeRig.Document(Intro, "v2", 2), caller.Token));
                break;
            case "ingest-cancelled-during-the-call":
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.IngestAsync(RuntimeRig.Document(Intro, "v2", 2), caller.Token));
                break;
            case "retrieve":
                await rig.RetrieveAsync(request);
                break;
            case "retrieve-error":
                await Assert.ThrowsAsync<InvalidOperationException>(() => rig.RetrieveAsync(request));
                break;
            case "retrieve-cancelled":
                await caller.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.RetrieveAsync(request, caller.Token));
                break;
            case "retrieve-cancelled-during-the-call":
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.RetrieveAsync(request, caller.Token));
                break;
            case "store-get":
                await rig.InScopeAsync(services => services.GetRequiredService<IKnowledgeStore>().GetSourceAsync(Intro));
                break;
            case "store-search":
                var query = (await rig.Generator.GenerateAsync(["How do I begin?"]))[0];
                await rig.InScopeAsync(services => services.GetRequiredService<IKnowledgeStore>().SearchAsync(new SimilarityQuery(query, [Intro], 5)));
                break;
            case "store-delete":
                await rig.InScopeAsync(services => services.GetRequiredService<IKnowledgeStore>().DeleteSourceAsync(Intro));
                break;
            case "store-error":
                var stray = new KnowledgeChunk(1, new Embedding("A passage.", [1f], RuntimeRig.Model));
                var source = new KnowledgeSource(Intro, RuntimeRig.Origin, "v1", RuntimeRig.Start, RuntimeRig.Model);
                await Assert.ThrowsAsync<ArgumentException>(() => rig.InScopeAsync(services => services.GetRequiredService<IKnowledgeStore>().StoreVersionAsync(source, [stray])));
                break;
            case "store-cancelled":
                await caller.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.InScopeAsync(services => services.GetRequiredService<IKnowledgeStore>().GetSourceAsync(Intro, caller.Token)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), "Not a case of this test.");
        }
    }

    // The caller cancels while the generator works, and the generator observes the caller's token.
    private static OperationCanceledException CancelledBy(CancellationTokenSource caller)
    {
        caller.Cancel();
        return new OperationCanceledException(caller.Token);
    }

    // A vector would arrive as a sequence of numbers; every tag and logged value is a single string or number.
    private static void AssertIsNoVector(object? value) => Assert.False(value is IEnumerable and not string);

    private static void AssertHoldsNoContent(string? text)
    {
        if (text is null)
        {
            return;
        }

        Assert.DoesNotContain(DocumentMark, text, StringComparison.Ordinal);
        Assert.DoesNotContain(QueryMark, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Passage ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("xxxxxxxxxx", text, StringComparison.Ordinal);
    }

    /// <summary>A provider whose every call times out on its own, with the caller's token never cancelled.</summary>
    private sealed class TimingOutStore : IKnowledgeStore
    {
        public Task<KnowledgeSource?> GetSourceAsync(string sourceId, CancellationToken cancellationToken = default) => Task.FromException<KnowledgeSource?>(TimedOut());

        public Task<IngestOutcome> StoreVersionAsync(KnowledgeSource source, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken = default) => Task.FromException<IngestOutcome>(TimedOut());

        public Task<RetrievalResult> SearchAsync(SimilarityQuery query, CancellationToken cancellationToken = default) => Task.FromException<RetrievalResult>(TimedOut());

        public Task<bool> DeleteSourceAsync(string sourceId, CancellationToken cancellationToken = default) => Task.FromException<bool>(TimedOut());

        private static TaskCanceledException TimedOut() => new("The provider timed out.");
    }
}
