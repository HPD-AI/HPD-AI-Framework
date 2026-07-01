using HPD.Base.Sqlite.Configuration;
using HPD.Base.Sqlite.Observability;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite.Internal;

internal sealed class SqliteConnectionFactory
{
    private readonly HPDBaseSqliteOptions _options;
    private static int s_batteriesInitialized;

    public SqliteConnectionFactory(HPDBaseSqliteOptions options)
    {
        _options = options;
    }

    public ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken) =>
        HPDBaseSqliteTelemetry.TraceConnectionOpenAsync(_options.StoreId, () => OpenCoreAsync(cancellationToken));

    private async ValueTask<SqliteConnection> OpenCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InitializeBatteries(_options);

        var connection = new SqliteConnection(BuildConnectionString(_options));
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public string BuildConnectionString() => BuildConnectionString(_options);

    public static void InitializeBatteries(HPDBaseSqliteOptions options)
    {
        if (options.InitializeSQLitePCLRaw && Interlocked.Exchange(ref s_batteriesInitialized, 1) == 0)
        {
            SQLitePCL.Batteries_V2.Init();
        }
    }

    private static string BuildConnectionString(HPDBaseSqliteOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return options.ConnectionString;
        }

        var memory = string.IsNullOrWhiteSpace(options.DataSource);
        var dataSource = memory ? "hpd_base_" + SanitizeStoreId(options.StoreId) : options.DataSource!;
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Mode = memory ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
            Cache = memory ? SqliteCacheMode.Shared : SqliteCacheMode.Default,
            DefaultTimeout = Math.Max(1, (int)Math.Ceiling(options.CommandTimeout.TotalSeconds))
        };
        return builder.ToString();
    }

    private async ValueTask ApplyPragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, $"PRAGMA busy_timeout={Math.Max(0, (int)_options.BusyTimeout.TotalMilliseconds)};", cancellationToken).ConfigureAwait(false);
        if (_options.EnableWal && !IsMemoryDatabase())
        {
            try
            {
                await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException)
            {
                // WAL is a best-effort concurrency setting for file-backed databases.
            }
        }
    }

    public bool IsMemoryDatabase()
    {
        var cs = BuildConnectionString(_options);
        var builder = new SqliteConnectionStringBuilder(cs);
        return string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
            || builder.Mode == SqliteOpenMode.Memory;
    }

    public async ValueTask<string?> GetJournalModeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        command.CommandTimeout = TimeoutSeconds();
        return (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString();
    }

    private async ValueTask ExecuteAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandTimeout = TimeoutSeconds();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private int TimeoutSeconds() => Math.Max(1, (int)Math.Ceiling(_options.CommandTimeout.TotalSeconds));

    private static string SanitizeStoreId(string storeId)
    {
        var sanitized = new string(storeId.Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "sqlite" : sanitized;
    }
}
