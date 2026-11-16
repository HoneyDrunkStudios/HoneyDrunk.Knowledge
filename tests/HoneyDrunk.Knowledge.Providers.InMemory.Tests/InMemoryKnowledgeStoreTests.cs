using HoneyDrunk.Knowledge.Abstractions;
using HoneyDrunk.Knowledge.Providers.Contract;

namespace HoneyDrunk.Knowledge.Providers.InMemory.Tests;

/// <summary>The store contract against the in-memory provider, which is its reference.</summary>
public sealed class InMemoryKnowledgeStoreTests : KnowledgeStoreContract
{
    private readonly InMemoryKnowledgeStore store = new();

    /// <inheritdoc />
    protected override IKnowledgeStore CreateStore() => this.store;
}
