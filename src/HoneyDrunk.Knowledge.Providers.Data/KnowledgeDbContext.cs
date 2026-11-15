using HoneyDrunk.Data.EntityFramework.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace HoneyDrunk.Knowledge.Providers.Data;

/// <summary>
/// The knowledge tables in the <c>knowledge</c> schema. The context is internal: passages leave this package only
/// through <c>IKnowledgeStore</c>. A context whose options turn on sensitive data logging is refused when it is
/// created, because Entity Framework would then write passages and vectors into logs.
/// </summary>
internal sealed class KnowledgeDbContext : HoneyDrunkDbContext
{
    /// <summary>The message of the refusal; it names the rule only.</summary>
    internal const string SensitiveDataLoggingRefused =
        "Sensitive data logging is on for the knowledge provider's context. It would write stored passages and vectors into logs. Remove EnableSensitiveDataLogging from the options passed to AddKnowledgeDataProvider.";

    /// <summary>Initializes a new instance of the <see cref="KnowledgeDbContext"/> class.</summary>
    /// <param name="options">The context options.</param>
    /// <exception cref="InvalidOperationException">The options turn on sensitive data logging.</exception>
    public KnowledgeDbContext(DbContextOptions<KnowledgeDbContext> options)
        : base(RefuseSensitiveDataLogging(options))
    {
    }

    /// <summary>Gets the source versions.</summary>
    public DbSet<KnowledgeSourceVersionEntity> SourceVersions => this.Set<KnowledgeSourceVersionEntity>();

    /// <summary>Gets the chunks.</summary>
    public DbSet<KnowledgeChunkEntity> Chunks => this.Set<KnowledgeChunkEntity>();

    /// <inheritdoc />
    protected override string? Schema => "knowledge";

    /// <inheritdoc />
    protected override void ApplyConfigurations(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new KnowledgeSourceVersionEntityConfiguration());
        modelBuilder.ApplyConfiguration(new KnowledgeChunkEntityConfiguration());
    }

    private static DbContextOptions<KnowledgeDbContext> RefuseSensitiveDataLogging(DbContextOptions<KnowledgeDbContext> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.FindExtension<CoreOptionsExtension>()?.IsSensitiveDataLoggingEnabled == true)
        {
            throw new InvalidOperationException(SensitiveDataLoggingRefused);
        }

        return options;
    }
}
