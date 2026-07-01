using HPD.Base.Health;
using HPD.Base.Runtime.Health;
using HPD.Base.Sqlite.Configuration;
using HPD.Base.Sqlite.Internal;
using Microsoft.Extensions.Options;

namespace HPD.Base.Sqlite.Health;

internal sealed class SqliteDiagnosticContributor : IBaseDiagnosticContributor
{
    private readonly HPDBaseSqliteOptions _options;

    public SqliteDiagnosticContributor(IOptions<HPDBaseSqliteOptions> options)
    {
        _options = options.Value;
    }

    public string Id => _options.DiagnosticRefId;

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
