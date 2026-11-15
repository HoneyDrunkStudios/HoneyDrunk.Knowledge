namespace HoneyDrunk.Knowledge.Abstractions;

/// <summary>What storing a version did.</summary>
public enum IngestOutcome
{
    /// <summary>The source had no current version; this one is now current.</summary>
    Added,

    /// <summary>The source had another current version; it is superseded and this one is now current.</summary>
    Superseded,

    /// <summary>The current version has the same version and embedding model; nothing was written.</summary>
    Unchanged,
}
