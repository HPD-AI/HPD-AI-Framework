using HPD.Base.Sqlite;
using HPD.Base;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

internal sealed class SqliteSchemaInitializer
{
    private readonly HPDBaseSqliteOptions _options;
    private readonly SqliteNames _names;

    public SqliteSchemaInitializer(HPDBaseSqliteOptions options)
    {
        _options = options;
        _names = new SqliteNames(options);
    }

    public ValueTask InitializeAsync(SqliteConnection connection, CancellationToken cancellationToken) =>
        HPDBaseSqliteTelemetry.TraceSchemaAsync(HPDBaseTelemetrySpans.SqliteSchemaInitialize, _options.StoreId, () => InitializeCoreAsync(connection, cancellationToken));

    private async ValueTask InitializeCoreAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (!SqliteValidation.IsValidSchemaPrefix(_options.SchemaPrefix))
        {
            throw new InvalidOperationException("SQLite schema prefix must contain only ASCII letters, digits, and underscores.");
        }

        await ExecuteAsync(connection, $"""
CREATE TABLE IF NOT EXISTS {_names.Records} (
  collection_id TEXT NOT NULL,
  record_id TEXT NOT NULL,
  revision INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  payload_json TEXT NOT NULL,
  PRIMARY KEY (collection_id, record_id)
);
""", cancellationToken).ConfigureAwait(false);

        var missingRecordColumns = await GetMissingRecordColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
        if (missingRecordColumns.Length != 0)
        {
            HPDBaseSqliteTelemetry.RecordSchemaMissingParts(_options.StoreId, missingRecordColumns.Length);
            throw new InvalidOperationException("SQLite provider-owned schema is missing required parts: " + string.Join(", ", missingRecordColumns));
        }

        await ExecuteAsync(connection, $"""
CREATE INDEX IF NOT EXISTS {_names.RecordsUpdatedIndex}
  ON {_names.Records}(collection_id, updated_at, record_id);
""", cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"""
CREATE TABLE IF NOT EXISTS {_names.Collections} (
  collection_id TEXT NOT NULL PRIMARY KEY,
  schema_hash TEXT NULL,
  registered_at TEXT NOT NULL,
  native_name TEXT NOT NULL,
  read_only INTEGER NOT NULL DEFAULT 0,
  descriptor_json TEXT NULL
);
""", cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"""
CREATE TABLE IF NOT EXISTS {_names.ProviderState} (
  key TEXT NOT NULL PRIMARY KEY,
  value TEXT NOT NULL
);
""", cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"""
CREATE TABLE IF NOT EXISTS {_names.MutationJournal} (
  position INTEGER PRIMARY KEY AUTOINCREMENT,
  event_id TEXT NOT NULL UNIQUE,
  event_type TEXT NOT NULL,
  schema_version TEXT NOT NULL,
  occurred_at TEXT NOT NULL,
  tenant_id TEXT NULL,
  operation INTEGER NOT NULL,
  visibility INTEGER NOT NULL,
  collection_id TEXT NOT NULL,
  record_id TEXT NOT NULL,
  before_json TEXT NULL,
  after_json TEXT NULL
);
""", cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"""
CREATE INDEX IF NOT EXISTS {_names.MutationJournalScopeIndex}
  ON {_names.MutationJournal}(tenant_id, collection_id, record_id, position);
""", cancellationToken).ConfigureAwait(false);

        foreach (var collectionId in _options.CollectionIds.Concat((_options.Collections ?? []).Select(c => c.Id)).Distinct(StringComparer.Ordinal))
        {
            if (!SqliteValidation.IsValidIdText(collectionId))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
INSERT INTO {_names.Collections}(collection_id, schema_hash, registered_at, native_name, read_only, descriptor_json)
VALUES ($collection, NULL, $registered, $native, 0, NULL)
ON CONFLICT(collection_id) DO UPDATE SET native_name = excluded.native_name;
""";
            command.CommandTimeout = TimeoutSeconds();
            command.Parameters.AddWithValue("$collection", collectionId);
            command.Parameters.AddWithValue("$registered", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$native", _names.Records);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var missing = await GetMissingSchemaPartsAsync(connection, cancellationToken).ConfigureAwait(false);
        if (missing.Length != 0)
        {
            throw new InvalidOperationException("SQLite provider-owned schema is missing required parts: " + string.Join(", ", missing));
        }
    }

    public ValueTask<bool> HasRequiredSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken) =>
        HPDBaseSqliteTelemetry.TraceSchemaAsync(HPDBaseTelemetrySpans.SqliteSchemaValidate, _options.StoreId, async () =>
        {
            var missing = await GetMissingSchemaPartsAsync(connection, cancellationToken).ConfigureAwait(false);
            return missing.Length == 0;
        });

    public async ValueTask<string[]> GetMissingSchemaPartsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (var table in new[] { _names.Records, _names.Collections, _names.ProviderState, _names.MutationJournal })
        {
            if (!await ObjectExistsAsync(connection, "table", table, cancellationToken).ConfigureAwait(false))
            {
                missing.Add("table:" + table);
            }
        }

        if (missing.Count == 0)
        {
            missing.AddRange(await GetMissingRecordColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
            missing.AddRange(await GetMissingMutationJournalColumnsAsync(connection, cancellationToken).ConfigureAwait(false));

            if (!await ObjectExistsAsync(connection, "index", _names.RecordsUpdatedIndex, cancellationToken).ConfigureAwait(false))
            {
                missing.Add("index:" + _names.RecordsUpdatedIndex);
            }

            if (!await ObjectExistsAsync(connection, "index", _names.MutationJournalScopeIndex, cancellationToken).ConfigureAwait(false))
            {
                missing.Add("index:" + _names.MutationJournalScopeIndex);
            }
        }

        var result = missing.ToArray();
        HPDBaseSqliteTelemetry.RecordSchemaMissingParts(_options.StoreId, result.Length);
        return result;
    }

    private async ValueTask ExecuteAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandTimeout = TimeoutSeconds();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private int TimeoutSeconds() => Math.Max(1, (int)Math.Ceiling(_options.CommandTimeout.TotalSeconds));

    private async ValueTask<bool> ObjectExistsAsync(SqliteConnection connection, string type, string name, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = $type AND name = $name;";
        command.CommandTimeout = TimeoutSeconds();
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$name", name);
        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! > 0;
    }

    private async ValueTask<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        command.CommandTimeout = TimeoutSeconds();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private async ValueTask<string[]> GetMissingRecordColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (var column in new[] { "collection_id", "record_id", "revision", "created_at", "updated_at", "payload_json" })
        {
            if (!await ColumnExistsAsync(connection, _names.Records, column, cancellationToken).ConfigureAwait(false))
            {
                missing.Add("column:" + _names.Records + "." + column);
            }
        }

        return missing.ToArray();
    }

    private async ValueTask<string[]> GetMissingMutationJournalColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (var column in new[]
        {
            "position",
            "event_id",
            "event_type",
            "schema_version",
            "occurred_at",
            "tenant_id",
            "operation",
            "visibility",
            "collection_id",
            "record_id",
            "before_json",
            "after_json"
        })
        {
            if (!await ColumnExistsAsync(connection, _names.MutationJournal, column, cancellationToken).ConfigureAwait(false))
                missing.Add("column:" + _names.MutationJournal + "." + column);
        }

        return missing.ToArray();
    }
}
