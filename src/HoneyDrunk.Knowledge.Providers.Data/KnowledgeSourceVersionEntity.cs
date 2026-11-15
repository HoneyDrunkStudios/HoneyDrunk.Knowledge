using HoneyDrunk.Knowledge.Abstractions;

namespace HoneyDrunk.Knowledge.Providers.Data;

/// <summary>One version of one source, as ingested under one embedding model.</summary>
internal sealed class KnowledgeSourceVersionEntity
{
    /// <summary>Gets or sets the version id: a version 7 GUID as 32 lower-case hexadecimal digits.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the host's id for the source.</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Gets or sets where the host says the source comes from.</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>Gets or sets the host's version string.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Gets or sets the model that made every vector of this version.</summary>
    public string EmbeddingModelId { get; set; } = string.Empty;

    /// <summary>Gets or sets the length of every vector of this version.</summary>
    public int Dimensions { get; set; }

    /// <summary>Gets or sets the number of chunks the version was stored with.</summary>
    public int ChunkCount { get; set; }

    /// <summary>
    /// Gets or sets the ingest instant, which the contract calls <c>LastUpdatedAt</c>, in UTC. A version is
    /// written once, so this is also the row's creation stamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the instant a newer version replaced this one, in UTC, or <see langword="null"/> while it is
    /// current. Set once; never before <see cref="CreatedAt"/>.
    /// </summary>
    public DateTimeOffset? SupersededAt { get; set; }

    /// <summary>Gets the version's chunks; empty once the version is superseded.</summary>
    public List<KnowledgeChunkEntity> Chunks { get; } = [];

    /// <summary>Describes the version as the contract's source record.</summary>
    /// <returns>The source.</returns>
    public KnowledgeSource ToSource() => new(this.SourceId, this.Origin, this.Version, this.CreatedAt, this.EmbeddingModelId);
}
