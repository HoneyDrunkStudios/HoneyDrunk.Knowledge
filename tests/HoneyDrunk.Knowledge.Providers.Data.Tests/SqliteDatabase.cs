using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HoneyDrunk.Knowledge.Providers.Data.Tests;

/// <summary>A throwaway SQLite file with the knowledge tables; each context opens its own connection.</summary>
internal sealed class SqliteDatabase : IDisposable
{
    private readonly string path = Path.Join(Path.GetTempPath(), "knowledge-" + Guid.NewGuid().ToString("N") + ".db");

    /// <summary>Initializes a new instance of the <see cref="SqliteDatabase"/> class and creates the tables.</summary>
    public SqliteDatabase()
    {
        this.ConnectionString = $"Data Source={this.path};Default Timeout=30;Pooling=False";
        using var context = this.CreateContext();
        context.Database.EnsureCreated();
    }

    /// <summary>Gets the connection string of the file.</summary>
    public string ConnectionString { get; }

    /// <summary>Points context options at this file.</summary>
    /// <param name="options">The options to configure.</param>
    /// <param name="interceptors">Interceptors to attach.</param>
    public void Configure(DbContextOptionsBuilder options, params IInterceptor[] interceptors)
    {
        options.UseSqlite(this.ConnectionString);
        if (interceptors.Length > 0)
        {
            options.AddInterceptors(interceptors);
        }
    }

    /// <summary>Creates a context over this file.</summary>
    /// <param name="interceptors">Interceptors to attach.</param>
    /// <returns>The context.</returns>
    public KnowledgeDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<KnowledgeDbContext>();
        this.Configure(options, interceptors);
        return new KnowledgeDbContext(options.Options);
    }

    /// <summary>Creates a store over this file, built the way the registration builds it.</summary>
    /// <param name="interceptors">Interceptors to attach to every context the store opens.</param>
    /// <returns>The store.</returns>
    public DataKnowledgeStore CreateStore(params IInterceptor[] interceptors) =>
        this.CreateStore(options => this.Configure(options, interceptors));

    /// <summary>Creates a store over a caller-configured context.</summary>
    /// <param name="configure">Configures each context the store opens.</param>
    /// <returns>The store.</returns>
    public DataKnowledgeStore CreateStore(Action<DbContextOptionsBuilder> configure) =>
        new(new KnowledgeDataService(new ContextFactory(configure)));

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            File.Delete(this.path);
        }
        catch (IOException)
        {
            // A connection is still closing on another thread; the temp folder is cleaned eventually.
        }
    }

    private sealed class ContextFactory(Action<DbContextOptionsBuilder> configure) : IDbContextFactory<KnowledgeDbContext>
    {
        public KnowledgeDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<KnowledgeDbContext>();
            configure(options);
            return new KnowledgeDbContext(options.Options);
        }
    }
}
