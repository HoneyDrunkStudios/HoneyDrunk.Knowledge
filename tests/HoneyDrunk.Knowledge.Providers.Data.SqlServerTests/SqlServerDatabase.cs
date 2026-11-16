using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HoneyDrunk.Knowledge.Providers.Data.SqlServerTests;

/// <summary>
/// A throwaway SQL Server database built from the table scripts the package ships. The server comes from the
/// environment; the database is always new, so an existing database is never a target.
/// </summary>
internal sealed class SqlServerDatabase : IDisposable
{
    /// <summary>The environment variable that names the server.</summary>
    public const string Variable = "HONEYDRUNK_KNOWLEDGE_TEST_SQLSERVER";

    /// <summary>The shipped scripts, in the order a host deploys them.</summary>
    public static readonly IReadOnlyList<string> Scripts = ["knowledge.sql", "knowledge.SourceVersion.sql", "knowledge.Chunk.sql"];

    private readonly string name = "KnowledgeProviderTest_" + Guid.NewGuid().ToString("N");
    private readonly string master;

    /// <summary>Initializes a new instance of the <see cref="SqlServerDatabase"/> class and deploys the scripts.</summary>
    public SqlServerDatabase()
    {
        var server = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(server))
        {
            throw new InvalidOperationException(
                $"Set {Variable} to a SQL Server connection string that names no database, for example 'Server=(localdb)\\KnowledgeProviderTests;Integrated Security=true;Encrypt=false' after 'sqllocaldb create KnowledgeProviderTests -s'.");
        }

        var builder = new SqlConnectionStringBuilder(server);
        if (!string.IsNullOrEmpty(builder.InitialCatalog) || !string.IsNullOrEmpty(builder.AttachDBFilename))
        {
            throw new InvalidOperationException($"{Variable} must not name a database. These tests create and drop their own; an existing database is never a target.");
        }

        // Read every script before the database exists, so a missing file leaves nothing behind.
        var batches = Scripts.SelectMany(script => Batches(File.ReadAllText(Path.Join(AppContext.BaseDirectory, "Sql", script)))).ToArray();
        this.master = new SqlConnectionStringBuilder(server) { InitialCatalog = "master", Pooling = false }.ConnectionString;
        this.ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = this.name, Pooling = false }.ConnectionString;
        using (var admin = this.Context(this.master, retry: false))
        {
            // The name is a GUID generated above, never outside input.
            var create = "CREATE DATABASE " + this.name;
            admin.Database.ExecuteSqlRaw(create);
        }

        try
        {
            using var context = this.CreateContext();

            // ExecuteSqlRaw reads braces as placeholders; the scripts have none of their own.
            var statements = batches.Select(batch => batch.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal));
            foreach (var statement in statements)
            {
                context.Database.ExecuteSqlRaw(statement);
            }
        }
        catch
        {
            this.Dispose();
            throw;
        }
    }

    /// <summary>Gets the connection string of the test database.</summary>
    public string ConnectionString { get; }

    /// <summary>Creates a context over the test database, configured as a host composes it, with retries on.</summary>
    /// <param name="interceptors">Interceptors to attach.</param>
    /// <returns>The context.</returns>
    public KnowledgeDbContext CreateContext(params IInterceptor[] interceptors) => this.Context(this.ConnectionString, retry: true, interceptors);

    /// <summary>
    /// Creates a store over the test database, built as a host composes it:
    /// <c>UseSqlServer(connection, sql =&gt; sql.EnableRetryOnFailure())</c>.
    /// </summary>
    /// <returns>The store.</returns>
    public DataKnowledgeStore CreateStore() => new(new KnowledgeDataService(new ContextFactory(() => this.CreateContext())));

    /// <summary>Runs one statement against the test database.</summary>
    /// <param name="sql">The statement.</param>
    /// <returns>A task.</returns>
    public async Task ExecuteAsync(string sql)
    {
        await using var context = this.Context(this.ConnectionString, retry: false);
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>Runs a query whose one column is text and returns its rows.</summary>
    /// <param name="sql">The query; its column must be named <c>Value</c> or be its only column.</param>
    /// <returns>The rows.</returns>
    public async Task<IReadOnlyList<string>> QueryAsync(string sql)
    {
        await using var context = this.Context(this.ConnectionString, retry: false);
        return await context.Database.SqlQueryRaw<string>(sql).ToListAsync();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        using var admin = this.Context(this.master, retry: false);
        var drop = "ALTER DATABASE " + this.name + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + this.name;
        admin.Database.ExecuteSqlRaw(drop);
    }

    private static IEnumerable<string> Batches(string script)
    {
        var batch = new List<string>();
        foreach (var line in script.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (string.Equals(line.Trim(), "GO", StringComparison.OrdinalIgnoreCase))
            {
                if (batch.Any(text => !string.IsNullOrWhiteSpace(text)))
                {
                    yield return string.Join('\n', batch);
                }

                batch.Clear();
            }
            else
            {
                batch.Add(line);
            }
        }

        if (batch.Any(text => !string.IsNullOrWhiteSpace(text)))
        {
            yield return string.Join('\n', batch);
        }
    }

    private KnowledgeDbContext Context(string connectionString, bool retry, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<KnowledgeDbContext>().UseSqlServer(connectionString, sql =>
        {
            if (retry)
            {
                sql.EnableRetryOnFailure();
            }
        });
        if (interceptors.Length > 0)
        {
            options.AddInterceptors(interceptors);
        }

        return new KnowledgeDbContext(options.Options);
    }

    private sealed class ContextFactory(Func<KnowledgeDbContext> create) : IDbContextFactory<KnowledgeDbContext>
    {
        public KnowledgeDbContext CreateDbContext() => create();
    }
}
