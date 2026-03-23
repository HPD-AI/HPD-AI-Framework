using HPD.Auth.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HPD.Auth.Infrastructure.Data;

/// <summary>
/// Design-time and test factory for <see cref="HPDAuthDbContext"/> using
/// SQLite in-memory databases.
///
/// Serves two purposes:
///
/// 1. <b>IDesignTimeDbContextFactory</b>: Satisfies the EF Core tooling contract so
///    that <c>dotnet ef</c> commands (migrations, scaffolding) can instantiate the
///    DbContext without a running host.
///
/// 2. <b>Test helper</b>: Tests call <see cref="CreateInMemory"/> to get an isolated
///    in-memory database instance. Each call with a unique <paramref name="databaseName"/>
///    produces a completely isolated database, ensuring tests do not share state.
///
/// Uses SQLite in-memory mode (<c>DataSource=file:…?mode=memory&amp;cache=shared</c>)
/// instead of the EF Core in-memory provider because ASP.NET Identity's passkey entity
/// uses <c>ComplexProperty().ToJson()</c>, which the EF in-memory provider does not
/// support (see https://github.com/dotnet/efcore/issues/31464).
/// </summary>
public sealed class HPDAuthDbContextFactory : IDesignTimeDbContextFactory<HPDAuthDbContext>
{
    /// <summary>
    /// IDesignTimeDbContextFactory implementation.
    /// Called by <c>dotnet ef</c> CLI tooling.
    /// </summary>
    public HPDAuthDbContext CreateDbContext(string[] args)
    {
        return CreateInMemory("DesignTimeDb");
    }

    /// <summary>
    /// Creates a new <see cref="HPDAuthDbContext"/> backed by SQLite in-memory.
    /// Suitable for unit tests and local development.
    /// </summary>
    /// <param name="databaseName">
    /// Name of the in-memory database. Use a unique name per test to ensure isolation.
    /// Instances sharing the same name share the same in-memory store within the same process.
    /// </param>
    /// <param name="tenantContext">
    /// Tenant context to inject. Defaults to <see cref="SingleTenantContext"/>
    /// (InstanceId = Guid.Empty) if not provided.
    /// </param>
    /// <returns>A configured <see cref="HPDAuthDbContext"/> with schema created.</returns>
    public static HPDAuthDbContext CreateInMemory(
        string databaseName,
        ITenantContext? tenantContext = null)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"file:{databaseName}?mode=memory&cache=shared",
            ForeignKeys = true
        }.ToString();

        // Open a keep-alive connection so SQLite doesn't discard the in-memory DB
        // when the DbContext connection closes.
        EnsureKeepAliveConnection(connectionString);

        var options = new DbContextOptionsBuilder<HPDAuthDbContext>()
            .UseSqlite(connectionString)
            .Options;

        var context = tenantContext ?? new SingleTenantContext();
        return new HPDAuthDbContext(options, context);
    }

    /// <summary>
    /// Creates a new <see cref="HPDAuthDbContext"/> with a randomly-named in-memory
    /// database, guaranteeing test isolation without manual name management.
    /// </summary>
    public static HPDAuthDbContext CreateIsolated(ITenantContext? tenantContext = null)
    {
        return CreateInMemory(Guid.NewGuid().ToString(), tenantContext);
    }

    private static readonly Dictionary<string, SqliteConnection> _keepAliveConnections = new();
    private static readonly object _lock = new();

    private static void EnsureKeepAliveConnection(string connectionString)
    {
        lock (_lock)
        {
            if (!_keepAliveConnections.ContainsKey(connectionString))
            {
                var conn = new SqliteConnection(connectionString);
                conn.Open();
                _keepAliveConnections[connectionString] = conn;
            }
        }
    }
}
