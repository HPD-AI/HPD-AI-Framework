using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HPD.Base.Sqlite;

internal sealed class SqliteDiagnosticContributor : IBaseDiagnosticContributor
{
    private readonly HPDBaseSqliteOptions _options;
    private readonly ILogger<SqliteDiagnosticContributor> _logger;

    /// <summary>Initializes a new instance.</summary>
    public SqliteDiagnosticContributor(
        IOptions<HPDBaseSqliteOptions> options,
        ILogger<SqliteDiagnosticContributor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Gets the ID.</summary>
    public string Id => _options.DiagnosticRefId;

    /// <summary>Executes the get diagnostics async operation.</summary>
    public async ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<DiagnosticDescriptor>
        {
            new DiagnosticDescriptor
            {
                Id = _options.DiagnosticRefId,
                Code = "base.sqlite.ready",
                Severity = DiagnosticSeverity.Info,
                TargetRef = _options.StoreId,
                Message = "HPD.BASE SQLite store is registered.",
                PublicMessage = "SQLite store is registered.",
                Category = DiagnosticCategory.Store,
                Visibility = VisibilityLevel.Admin,
                EmittedAt = DateTimeOffset.UtcNow
            }
        };

        try
        {
            var factory = new SqliteConnectionFactory(_options);
            await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
            var journalMode = await factory.GetJournalModeAsync(connection, cancellationToken).ConfigureAwait(false);
            var sqliteVersion = await ScalarAsync(connection, "SELECT sqlite_version();", cancellationToken).ConfigureAwait(false);
            var foreignKeys = await ScalarAsync(connection, "PRAGMA foreign_keys;", cancellationToken).ConfigureAwait(false);
            var busyTimeout = await ScalarAsync(connection, "PRAGMA busy_timeout;", cancellationToken).ConfigureAwait(false);
            var synchronous = await ScalarAsync(connection, "PRAGMA synchronous;", cancellationToken).ConfigureAwait(false);
            var missing = await new SqliteSchemaInitializer(_options).GetMissingSchemaPartsAsync(connection, cancellationToken).ConfigureAwait(false);
            if (missing.Length != 0)
            {
                HPDBaseSqliteLog.SchemaDiagnosticWarning(_logger, SqliteErrorCodes.SchemaMissing);
            }

            diagnostics.Add(new DiagnosticDescriptor
            {
                Id = _options.DiagnosticRefId + ".configuration",
                Code = "base.sqlite.configuration",
                Severity = missing.Length == 0 ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
                TargetRef = _options.StoreId,
                Message = $"SQLite version '{sqliteVersion ?? "unknown"}', schema prefix '{_options.SchemaPrefix}', journal mode '{journalMode ?? "unknown"}', foreign_keys '{foreignKeys ?? "unknown"}', busy_timeout '{busyTimeout ?? "unknown"}', synchronous '{synchronous ?? "unknown"}', missing schema parts: {missing.Length}.",
                PublicMessage = "SQLite provider configuration is available to administrators.",
                Category = DiagnosticCategory.Store,
                Visibility = VisibilityLevel.Admin,
                EmittedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(new DiagnosticDescriptor
            {
                Id = _options.DiagnosticRefId + ".unavailable",
                Code = "base.sqlite.unavailable",
                Severity = DiagnosticSeverity.Error,
                TargetRef = _options.StoreId,
                Message = "SQLite diagnostics could not open the configured database.",
                PublicMessage = "SQLite store diagnostics are unavailable.",
                Category = DiagnosticCategory.Store,
                Visibility = VisibilityLevel.Admin,
                EmittedAt = DateTimeOffset.UtcNow
            });
        }

        return diagnostics.ToArray();
    }

    private static async ValueTask<string?> ScalarAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString();
    }
}
