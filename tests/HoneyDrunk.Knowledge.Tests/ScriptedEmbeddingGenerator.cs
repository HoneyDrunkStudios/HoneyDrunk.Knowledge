using HoneyDrunk.AI.Abstractions.Embeddings;
using HoneyDrunk.AI.Providers.InMemory;

namespace HoneyDrunk.Knowledge.Tests;

/// <summary>
/// Wraps <see cref="InMemoryEmbeddingGenerator"/> so a test can count calls, rewrite what a given call
/// returns (another model id, a missing or extra embedding, other text, other vectors), run an action
/// before a call or make a call fail. Calls are numbered from 1 over the generator's life.
/// </summary>
/// <param name="modelId">The model id the wrapped generator stamps on every embedding.</param>
internal sealed class ScriptedEmbeddingGenerator(string modelId) : IEmbeddingGenerator
{
    private readonly InMemoryEmbeddingGenerator inner = new(modelId);
    private readonly List<int> batchSizes = [];
    private readonly List<Embedding> returned = [];

    /// <summary>Gets the number of calls made.</summary>
    public int Calls
    {
        get
        {
            lock (this.batchSizes)
            {
                return this.batchSizes.Count;
            }
        }
    }

    /// <summary>Gets the number of texts sent in each call, in order.</summary>
    public IReadOnlyList<int> BatchSizes
    {
        get
        {
            lock (this.batchSizes)
            {
                return [.. this.batchSizes];
            }
        }
    }

    /// <summary>Gets every embedding returned, after any rewrite, in order.</summary>
    public IReadOnlyList<Embedding> Returned
    {
        get
        {
            lock (this.batchSizes)
            {
                return [.. this.returned];
            }
        }
    }

    /// <summary>Gets the call that throws instead of answering, if any.</summary>
    public int? FailOnCall { get; init; }

    /// <summary>Gets a rewrite of what a call returns: the call number and the embeddings, to the embeddings returned.</summary>
    public Func<int, IReadOnlyList<Embedding>, IReadOnlyList<Embedding>>? Rewrite { get; init; }

    /// <summary>Gets an action run before a call is answered, given the call number.</summary>
    public Func<int, Task>? BeforeCall { get; init; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Embedding>> GenerateAsync(IEnumerable<string> values, EmbeddingOptions? options = null, CancellationToken cancellationToken = default)
    {
        var texts = values.ToArray();
        int call;
        lock (this.batchSizes)
        {
            this.batchSizes.Add(texts.Length);
            call = this.batchSizes.Count;
        }

        if (this.BeforeCall is not null)
        {
            await this.BeforeCall(call).ConfigureAwait(false);
        }

        if (call == this.FailOnCall)
        {
            throw new InvalidOperationException("The scripted generator failed.");
        }

        var embeddings = await this.inner.GenerateAsync(texts, options, cancellationToken).ConfigureAwait(false);
        var answer = this.Rewrite is null ? embeddings : this.Rewrite(call, embeddings);
        lock (this.batchSizes)
        {
            this.returned.AddRange(answer);
        }

        return answer;
    }
}
