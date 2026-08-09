using HPD.Base.Sqlite;
using HPD.Base;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

internal sealed class SqliteSchemaInitializer
{
    private readonly HPDBaseSqliteOptions _options;
    private readonly SqliteNames _names;
    private readonly SqlitePhysicalModel _physical;
    private readonly string[] _projectionSchemaStatements;
    private readonly string[] _projectionSchemaTables;
    private readonly SqliteProjectionTableShape[] _projectionSchemaShapes;

    /// <summary>Initializes a new instance.</summary>
    public SqliteSchemaInitializer(HPDBaseSqliteOptions options, string[]? projectionSchemaStatements = null, string[]? projectionSchemaTables = null, SqliteProjectionTableShape[]? projectionSchemaShapes = null)
    {
        _options = options;
        _names = new SqliteNames(options);
        _physical = new SqlitePhysicalModel(options);
        _projectionSchemaStatements = projectionSchemaStatements?.ToArray() ?? [];
        _projectionSchemaTables = projectionSchemaTables?.Distinct(StringComparer.Ordinal).ToArray() ?? [];
        _projectionSchemaShapes = projectionSchemaShapes?.ToArray() ?? [];
    }

    /// <summary>Executes the initialize async operation.</summary>
    public ValueTask InitializeAsync(SqliteConnection connection, CancellationToken cancellationToken) =>
        HPDBaseSqliteTelemetry.TraceSchemaAsync(HPDBaseTelemetrySpans.SqliteSchemaInitialize, _options.StoreId, () => InitializeCoreAsync(connection, cancellationToken));

    internal string[] GetExecutionStatements()
    {
        var statements = new List<string>();
        statements.AddRange(_physical.Collections.Select(static collection => collection.CreateSql()));
        statements.AddRange(_physical.Relations.Select(static relation => relation.CreateSql()));
        statements.Add($"""
CREATE TABLE IF NOT EXISTS {_names.Collections} (
  collection_id TEXT NOT NULL PRIMARY KEY,
  schema_hash TEXT NULL,
  registered_at TEXT NOT NULL,
  native_name TEXT NOT NULL,
  mutation_mode INTEGER NOT NULL,
  next_append_position INTEGER NOT NULL DEFAULT 0 CHECK (next_append_position >= 0),
  purge_generation INTEGER NOT NULL DEFAULT 0 CHECK (purge_generation >= 0),
  descriptor_json TEXT NULL
);
CREATE TABLE IF NOT EXISTS {_names.ProviderState} (
  key TEXT NOT NULL PRIMARY KEY,
  value TEXT NOT NULL
);
INSERT OR IGNORE INTO {_names.ProviderState}(key, value) VALUES ('restore_epoch', '0');
CREATE TABLE IF NOT EXISTS {_names.SchemaIdentity} (
  singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1),
  store_instance_id TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS {_names.SchemaBaseline} (
  application_id TEXT NOT NULL PRIMARY KEY,
  store_instance_id TEXT NOT NULL,
  baseline_id TEXT NOT NULL,
  checksum TEXT NOT NULL,
  generation INTEGER NOT NULL,
  last_plan_id TEXT NOT NULL,
  applied_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS {_names.SchemaAssets} (
  application_id TEXT NOT NULL,
  logical_id TEXT NOT NULL,
  safe_summary TEXT NOT NULL,
  state INTEGER NOT NULL,
  PRIMARY KEY(application_id, logical_id)
);
CREATE TABLE IF NOT EXISTS {_names.SchemaHistory} (
  application_id TEXT NOT NULL,
  generation INTEGER NOT NULL,
  baseline_id TEXT NOT NULL,
  checksum TEXT NOT NULL,
  plan_id TEXT NOT NULL,
  classification INTEGER NOT NULL,
  outcome INTEGER NOT NULL,
  provider_version TEXT NOT NULL,
  structural_verification INTEGER NOT NULL,
  external_data_migration INTEGER NOT NULL,
  semantic_conversion INTEGER NOT NULL,
  external_attestation_id TEXT NULL,
  external_signer_id TEXT NULL,
  applied_at TEXT NOT NULL,
  PRIMARY KEY(application_id, generation)
);
CREATE TABLE IF NOT EXISTS {_names.SchemaLease} (
  application_id TEXT NOT NULL PRIMARY KEY,
  generation INTEGER NOT NULL,
  owner_token TEXT NULL,
  acquired_at TEXT NULL
);
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
CREATE TABLE IF NOT EXISTS {_names.OperationReceipts} (
  scope TEXT NOT NULL,
  operation TEXT NOT NULL,
  idempotency_key TEXT NOT NULL,
  fingerprint BLOB NOT NULL CHECK(length(fingerprint) = 32),
  structural_digest BLOB NOT NULL CHECK(length(structural_digest) = 32),
  result_json BLOB NOT NULL,
  result_format_version INTEGER NOT NULL,
  schema_generation INTEGER NOT NULL,
  store_instance_id TEXT NOT NULL,
  committed_at TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  PRIMARY KEY(scope, operation, idempotency_key)
) WITHOUT ROWID;
""");
        foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
        {
            statements.Add($"CREATE INDEX IF NOT EXISTS ix_{collection.Table}_updated ON {collection.Table}(updated_at, record_id);");
            statements.AddRange(collection.Indexes.Select(index => index.CreateSql(collection)));
        }
        foreach (SqlitePhysicalModel.RelationModel relation in _physical.Relations)
        {
            statements.Add($"CREATE INDEX IF NOT EXISTS {relation.SourceIndex} ON {relation.Table}(source_record_id, ordinal);");
            statements.Add($"CREATE INDEX IF NOT EXISTS {relation.TargetIndex} ON {relation.Table}(target_record_id, source_record_id);");
        }
        statements.Add($"CREATE INDEX IF NOT EXISTS {_names.MutationJournalScopeIndex} ON {_names.MutationJournal}(tenant_id, collection_id, record_id, position);");
        statements.AddRange(_projectionSchemaStatements);
        return statements.ToArray();
    }

    private async ValueTask InitializeCoreAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (!SqliteValidation.IsValidSchemaPrefix(_options.SchemaPrefix))
        {
            throw new InvalidOperationException("SQLite schema prefix must contain only ASCII letters, digits, and underscores.");
        }

        foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
        {
            await ExecuteAsync(connection, collection.CreateSql(), cancellationToken).ConfigureAwait(false);
        }
        foreach (SqlitePhysicalModel.RelationModel relation in _physical.Relations)
            await ExecuteAsync(connection, relation.CreateSql(), cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"""
CREATE TABLE IF NOT EXISTS {_names.Collections} (
  collection_id TEXT NOT NULL PRIMARY KEY,
  schema_hash TEXT NULL,
  registered_at TEXT NOT NULL,
  native_name TEXT NOT NULL,
  mutation_mode INTEGER NOT NULL,
  next_append_position INTEGER NOT NULL DEFAULT 0 CHECK (next_append_position >= 0),
  purge_generation INTEGER NOT NULL DEFAULT 0 CHECK (purge_generation >= 0),
  descriptor_json TEXT NULL
);
""", cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"""
CREATE TABLE IF NOT EXISTS {_names.ProviderState} (
  key TEXT NOT NULL PRIMARY KEY,
  value TEXT NOT NULL
);
INSERT OR IGNORE INTO {_names.ProviderState}(key, value) VALUES ('restore_epoch', '0');
""", cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"""
CREATE TABLE IF NOT EXISTS {_names.SchemaIdentity} (
  singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1),
  store_instance_id TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS {_names.SchemaBaseline} (
  application_id TEXT NOT NULL PRIMARY KEY,
  store_instance_id TEXT NOT NULL,
  baseline_id TEXT NOT NULL,
  checksum TEXT NOT NULL,
  generation INTEGER NOT NULL,
  last_plan_id TEXT NOT NULL,
  applied_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS {_names.SchemaAssets} (
  application_id TEXT NOT NULL,
  logical_id TEXT NOT NULL,
  safe_summary TEXT NOT NULL,
  state INTEGER NOT NULL,
  PRIMARY KEY(application_id, logical_id)
);
CREATE TABLE IF NOT EXISTS {_names.SchemaHistory} (
  application_id TEXT NOT NULL,
  generation INTEGER NOT NULL,
  baseline_id TEXT NOT NULL,
  checksum TEXT NOT NULL,
  plan_id TEXT NOT NULL,
  classification INTEGER NOT NULL,
  outcome INTEGER NOT NULL,
  provider_version TEXT NOT NULL,
  structural_verification INTEGER NOT NULL,
  external_data_migration INTEGER NOT NULL,
  semantic_conversion INTEGER NOT NULL,
  external_attestation_id TEXT NULL,
  external_signer_id TEXT NULL,
  applied_at TEXT NOT NULL,
  PRIMARY KEY(application_id, generation)
);
CREATE TABLE IF NOT EXISTS {_names.SchemaLease} (
  application_id TEXT NOT NULL PRIMARY KEY,
  generation INTEGER NOT NULL,
  owner_token TEXT NULL,
  acquired_at TEXT NULL
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
CREATE TABLE IF NOT EXISTS {_names.OperationReceipts} (
  scope TEXT NOT NULL,
  operation TEXT NOT NULL,
  idempotency_key TEXT NOT NULL,
  fingerprint BLOB NOT NULL CHECK(length(fingerprint) = 32),
  structural_digest BLOB NOT NULL CHECK(length(structural_digest) = 32),
  result_json BLOB NOT NULL,
  result_format_version INTEGER NOT NULL,
  schema_generation INTEGER NOT NULL,
  store_instance_id TEXT NOT NULL,
  committed_at TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  PRIMARY KEY(scope, operation, idempotency_key)
) WITHOUT ROWID;
""", cancellationToken).ConfigureAwait(false);

        var malformedColumns = new List<string>();
        malformedColumns.AddRange(await GetMissingCollectionStateAsync(connection, cancellationToken).ConfigureAwait(false));
        foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
            malformedColumns.AddRange(await GetMissingRecordColumnsAsync(connection, collection, cancellationToken).ConfigureAwait(false));
        malformedColumns.AddRange(await GetMissingMutationJournalColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
        malformedColumns.AddRange(await GetMissingSchemaAuthorityColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
        foreach (SqlitePhysicalModel.RelationModel relation in _physical.Relations)
            malformedColumns.AddRange(await GetMissingRelationColumnsAsync(connection, relation, cancellationToken).ConfigureAwait(false));
        if (malformedColumns.Count != 0)
            throw new InvalidOperationException("SQLite provider-owned schema is missing required parts: " + string.Join(", ", malformedColumns));

        foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
        {
            await ExecuteAsync(connection,
                $"CREATE INDEX IF NOT EXISTS ix_{collection.Table}_updated ON {collection.Table}(updated_at, record_id);",
                cancellationToken).ConfigureAwait(false);
            foreach (SqlitePhysicalModel.IndexModel index in collection.Indexes)
                await ExecuteAsync(connection, index.CreateSql(collection), cancellationToken).ConfigureAwait(false);
        }
        foreach (SqlitePhysicalModel.RelationModel relation in _physical.Relations)
        {
            await ExecuteAsync(connection, $"CREATE INDEX IF NOT EXISTS {relation.SourceIndex} ON {relation.Table}(source_record_id, ordinal);", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, $"CREATE INDEX IF NOT EXISTS {relation.TargetIndex} ON {relation.Table}(target_record_id, source_record_id);", cancellationToken).ConfigureAwait(false);
        }

        await ExecuteAsync(connection, $"""
CREATE INDEX IF NOT EXISTS {_names.MutationJournalScopeIndex}
  ON {_names.MutationJournal}(tenant_id, collection_id, record_id, position);
""", cancellationToken).ConfigureAwait(false);

        foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
        {
            string collectionId = collection.Definition.Id;
            if (!SqliteValidation.IsValidIdText(collectionId))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
INSERT INTO {_names.Collections}(collection_id, schema_hash, registered_at, native_name, mutation_mode, next_append_position, purge_generation, descriptor_json)
VALUES ($collection, NULL, $registered, $native, $mode, 0, 0, NULL)
ON CONFLICT(collection_id) DO UPDATE SET native_name = excluded.native_name, mutation_mode = excluded.mutation_mode;
""";
            command.CommandTimeout = TimeoutSeconds();
            command.Parameters.AddWithValue("$collection", collectionId);
            command.Parameters.AddWithValue("$registered", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$native", collection.Table);
            command.Parameters.AddWithValue("$mode", (int)collection.Definition.MutationMode);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (string statement in _projectionSchemaStatements)
            await ExecuteAsync(connection, statement, cancellationToken).ConfigureAwait(false);

        var missing = await GetMissingSchemaPartsAsync(connection, cancellationToken).ConfigureAwait(false);
        if (missing.Length != 0)
        {
            throw new InvalidOperationException("SQLite provider-owned schema is missing required parts: " + string.Join(", ", missing));
        }
    }

    /// <summary>Executes the has required schema async operation.</summary>
    public ValueTask<bool> HasRequiredSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken) =>
        HPDBaseSqliteTelemetry.TraceSchemaAsync(HPDBaseTelemetrySpans.SqliteSchemaValidate, _options.StoreId, async () =>
        {
            var missing = await GetMissingSchemaPartsAsync(connection, cancellationToken).ConfigureAwait(false);
            return missing.Length == 0;
        });

    /// <summary>Executes the get missing schema parts async operation.</summary>
    public async ValueTask<string[]> GetMissingSchemaPartsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (var table in new[] { _names.Collections, _names.ProviderState, _names.MutationJournal, _names.OperationReceipts, _names.SchemaIdentity, _names.SchemaBaseline, _names.SchemaAssets, _names.SchemaHistory, _names.SchemaLease }
            .Concat(_physical.Collections.Select(static collection => collection.Table))
            .Concat(_physical.Relations.Select(static relation => relation.Table))
            .Concat(_projectionSchemaTables))
        {
            if (!await ObjectExistsAsync(connection, "table", table, cancellationToken).ConfigureAwait(false))
            {
                missing.Add("table:" + table);
            }
        }

        if (missing.Count == 0)
        {
            foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
            {
                missing.AddRange(await GetMissingRecordColumnsAsync(connection, collection, cancellationToken).ConfigureAwait(false));
                missing.AddRange(await GetMalformedRecordColumnsAsync(connection, collection, cancellationToken).ConfigureAwait(false));
            }
            foreach (SqlitePhysicalModel.RelationModel relation in _physical.Relations)
            {
                missing.AddRange(await GetMissingRelationColumnsAsync(connection, relation, cancellationToken).ConfigureAwait(false));
                missing.AddRange(await GetMalformedRelationColumnsAsync(connection, relation, cancellationToken).ConfigureAwait(false));
            }
            foreach (SqliteProjectionTableShape shape in _projectionSchemaShapes)
                missing.AddRange(await GetMalformedProjectionColumnsAsync(connection, shape, cancellationToken).ConfigureAwait(false));
            missing.AddRange(await GetMissingMutationJournalColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
            missing.AddRange(await GetMissingReceiptColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
            missing.AddRange(await GetMalformedMutationJournalColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
            missing.AddRange(await GetMalformedReceiptColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
            missing.AddRange(await GetMissingSchemaAuthorityColumnsAsync(connection, cancellationToken).ConfigureAwait(false));
            missing.AddRange(await GetMissingCollectionStateAsync(connection, cancellationToken).ConfigureAwait(false));

            foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
            {
                if (!await ObjectExistsAsync(connection, "index", $"ix_{collection.Table}_updated", cancellationToken).ConfigureAwait(false))
                    missing.Add("index:ix_" + collection.Table + "_updated");
                else if (!await IndexMatchesAsync(connection, $"ix_{collection.Table}_updated", false, ["updated_at", "record_id"], [false, false], cancellationToken).ConfigureAwait(false))
                    missing.Add("index-shape:ix_" + collection.Table + "_updated");
                foreach (SqlitePhysicalModel.IndexModel index in collection.Indexes)
                    if (!await ObjectExistsAsync(connection, "index", index.Name, cancellationToken).ConfigureAwait(false))
                        missing.Add("index:" + index.Name);
                    else if (!await IndexMatchesAsync(connection, index.Name, index.Definition.Unique || index.Definition.Kind == IndexKind.Unique, index.Parts.Select(static part => part.Column).ToArray(), index.Definition.Parts!.Select(static part => part.Direction == IndexSortDirection.Desc).ToArray(), cancellationToken).ConfigureAwait(false))
                        missing.Add("index-shape:" + index.Name);
            }
            foreach (SqlitePhysicalModel.RelationModel relation in _physical.Relations)
            {
                if (!await ObjectExistsAsync(connection, "index", relation.SourceIndex, cancellationToken).ConfigureAwait(false)) missing.Add("index:" + relation.SourceIndex);
                else if (!await IndexMatchesAsync(connection, relation.SourceIndex, false, ["source_record_id", "ordinal"], [false, false], cancellationToken).ConfigureAwait(false)) missing.Add("index-shape:" + relation.SourceIndex);
                if (!await ObjectExistsAsync(connection, "index", relation.TargetIndex, cancellationToken).ConfigureAwait(false)) missing.Add("index:" + relation.TargetIndex);
                else if (!await IndexMatchesAsync(connection, relation.TargetIndex, false, ["target_record_id", "source_record_id"], [false, false], cancellationToken).ConfigureAwait(false)) missing.Add("index-shape:" + relation.TargetIndex);
            }

            if (!await ObjectExistsAsync(connection, "index", _names.MutationJournalScopeIndex, cancellationToken).ConfigureAwait(false))
            {
                missing.Add("index:" + _names.MutationJournalScopeIndex);
            }
            else if (!await IndexMatchesAsync(connection, _names.MutationJournalScopeIndex, false, ["tenant_id", "collection_id", "record_id", "position"], [false, false, false, false], cancellationToken).ConfigureAwait(false))
            {
                missing.Add("index-shape:" + _names.MutationJournalScopeIndex);
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

    private async ValueTask<Dictionary<string, ColumnShape>> GetColumnShapesAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ColumnShape>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        command.CommandTimeout = TimeoutSeconds();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result[reader.GetString(1)] = new ColumnShape(reader.GetString(2).ToUpperInvariant(), reader.GetInt64(3) != 0, reader.GetInt64(5) != 0);
        return result;
    }

    private async ValueTask<bool> IndexMatchesAsync(SqliteConnection connection, string index, bool unique, string[] columns, bool[] descending, CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT [sql] FROM sqlite_master WHERE type = 'index' AND name = $name;";
            command.CommandTimeout = TimeoutSeconds();
            command.Parameters.AddWithValue("$name", index);
            string? sql = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (sql is null || sql.Contains("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase) != unique) return false;
        }
        var actual = new List<(string Column, bool Descending)>();
        await using var info = connection.CreateCommand();
        info.CommandText = $"PRAGMA index_xinfo({index});";
        info.CommandTimeout = TimeoutSeconds();
        await using var reader = await info.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            if (reader.GetInt64(5) != 0) actual.Add((reader.GetString(2), reader.GetInt64(3) != 0));
        return actual.Select(static item => item.Column).SequenceEqual(columns, StringComparer.Ordinal) &&
            actual.Select(static item => item.Descending).SequenceEqual(descending);
    }

    private async ValueTask<string[]> GetMalformedRecordColumnsAsync(SqliteConnection connection, SqlitePhysicalModel.CollectionModel collection, CancellationToken cancellationToken)
    {
        Dictionary<string, ColumnShape> shapes = await GetColumnShapesAsync(connection, collection.Table, cancellationToken).ConfigureAwait(false);
        var malformed = new List<string>();
        Check(shapes, malformed, collection.Table, "record_id", "TEXT", false, true);
        Check(shapes, malformed, collection.Table, "revision", "INTEGER", true, false);
        Check(shapes, malformed, collection.Table, "created_at", "TEXT", true, false);
        Check(shapes, malformed, collection.Table, "updated_at", "TEXT", true, false);
        Check(shapes, malformed, collection.Table, "append_position", "INTEGER", true, false);
        Check(shapes, malformed, collection.Table, "latest_mutation_position", "INTEGER", true, false);
        foreach (SqlitePhysicalModel.FieldModel field in collection.Fields)
        {
            if (field.PresenceColumn is not null) Check(shapes, malformed, collection.Table, field.PresenceColumn, "INTEGER", true, false);
            Check(shapes, malformed, collection.Table, field.Column, field.SqlType, field.PresenceColumn is null, false);
        }
        if (collection.HasExtensionJson) Check(shapes, malformed, collection.Table, "extension_json", "TEXT", false, false);
        return malformed.ToArray();
    }

    private async ValueTask<string[]> GetMalformedRelationColumnsAsync(SqliteConnection connection, SqlitePhysicalModel.RelationModel relation, CancellationToken cancellationToken)
    {
        Dictionary<string, ColumnShape> shapes = await GetColumnShapesAsync(connection, relation.Table, cancellationToken).ConfigureAwait(false);
        var malformed = new List<string>();
        Check(shapes, malformed, relation.Table, "source_record_id", "TEXT", true, true);
        Check(shapes, malformed, relation.Table, "target_record_id", "TEXT", true, false);
        Check(shapes, malformed, relation.Table, "ordinal", "INTEGER", true, true);
        return malformed.ToArray();
    }

    private async ValueTask<string[]> GetMalformedProjectionColumnsAsync(SqliteConnection connection, SqliteProjectionTableShape projection, CancellationToken cancellationToken)
    {
        Dictionary<string, ColumnShape> shapes = await GetColumnShapesAsync(connection, projection.Table, cancellationToken).ConfigureAwait(false);
        var malformed = new List<string>();
        foreach (SqliteProjectionColumnShape expected in projection.Columns)
        {
            if (!shapes.ContainsKey(expected.Name)) malformed.Add("column:" + projection.Table + "." + expected.Name);
            else Check(shapes, malformed, projection.Table, expected.Name, expected.Type, expected.NotNull, expected.PrimaryKey);
        }
        return malformed.ToArray();
    }

    private static void Check(Dictionary<string, ColumnShape> shapes, List<string> malformed, string table, string column, string type, bool notNull, bool primaryKey)
    {
        if (shapes.TryGetValue(column, out ColumnShape shape) && (shape.Type != type || shape.NotNull != notNull || shape.PrimaryKey != primaryKey))
            malformed.Add("column-shape:" + table + "." + column);
    }

    private readonly record struct ColumnShape(string Type, bool NotNull, bool PrimaryKey);

    private async ValueTask<string[]> GetMissingRecordColumnsAsync(SqliteConnection connection, SqlitePhysicalModel.CollectionModel collection, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        IEnumerable<string> columns = new[] { "record_id", "revision", "created_at", "updated_at", "append_position" }
            .Concat(collection.Fields.SelectMany(static field => field.PresenceColumn is null ? [field.Column] : new[] { field.PresenceColumn, field.Column }))
            .Concat(collection.HasExtensionJson ? ["extension_json"] : []);
        foreach (var column in columns)
        {
            if (!await ColumnExistsAsync(connection, collection.Table, column, cancellationToken).ConfigureAwait(false))
            {
                missing.Add("column:" + collection.Table + "." + column);
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

    private async ValueTask<string[]> GetMalformedMutationJournalColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        Dictionary<string, ColumnShape> shapes = await GetColumnShapesAsync(connection, _names.MutationJournal, cancellationToken).ConfigureAwait(false);
        var malformed = new List<string>();
        Check(shapes, malformed, _names.MutationJournal, "position", "INTEGER", false, true);
        foreach (string column in new[] { "event_id", "event_type", "schema_version", "occurred_at", "collection_id", "record_id" })
            Check(shapes, malformed, _names.MutationJournal, column, "TEXT", true, false);
        Check(shapes, malformed, _names.MutationJournal, "tenant_id", "TEXT", false, false);
        Check(shapes, malformed, _names.MutationJournal, "operation", "INTEGER", true, false);
        Check(shapes, malformed, _names.MutationJournal, "visibility", "INTEGER", true, false);
        Check(shapes, malformed, _names.MutationJournal, "before_json", "TEXT", false, false);
        Check(shapes, malformed, _names.MutationJournal, "after_json", "TEXT", false, false);
        return malformed.ToArray();
    }

    private async ValueTask<string[]> GetMissingReceiptColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (string column in new[] { "scope", "operation", "idempotency_key", "fingerprint", "structural_digest", "result_json", "result_format_version", "schema_generation", "store_instance_id", "committed_at", "expires_at" })
            if (!await ColumnExistsAsync(connection, _names.OperationReceipts, column, cancellationToken).ConfigureAwait(false))
                missing.Add("column:" + _names.OperationReceipts + "." + column);
        return missing.ToArray();
    }

    private async ValueTask<string[]> GetMalformedReceiptColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        Dictionary<string, ColumnShape> shapes = await GetColumnShapesAsync(connection, _names.OperationReceipts, cancellationToken).ConfigureAwait(false);
        var malformed = new List<string>();
        foreach (string column in new[] { "scope", "operation", "idempotency_key" })
            Check(shapes, malformed, _names.OperationReceipts, column, "TEXT", true, true);
        foreach (string column in new[] { "fingerprint", "structural_digest", "result_json" })
            Check(shapes, malformed, _names.OperationReceipts, column, "BLOB", true, false);
        Check(shapes, malformed, _names.OperationReceipts, "result_format_version", "INTEGER", true, false);
        Check(shapes, malformed, _names.OperationReceipts, "schema_generation", "INTEGER", true, false);
        foreach (string column in new[] { "store_instance_id", "committed_at", "expires_at" })
            Check(shapes, malformed, _names.OperationReceipts, column, "TEXT", true, false);
        return malformed.ToArray();
    }

    private async ValueTask<string[]> GetMissingCollectionStateAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (string column in new[]
        {
            "collection_id", "schema_hash", "registered_at", "native_name",
            "mutation_mode", "next_append_position", "purge_generation", "descriptor_json"
        })
            if (!await ColumnExistsAsync(connection, _names.Collections, column, cancellationToken).ConfigureAwait(false))
                missing.Add("column:" + _names.Collections + "." + column);
        if (missing.Count != 0) return missing.ToArray();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT value FROM {_names.ProviderState} WHERE key = 'restore_epoch';";
        command.CommandTimeout = TimeoutSeconds();
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is not string text || !long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long epoch) || epoch < 0)
            missing.Add("state:" + _names.ProviderState + ".restore_epoch");
        return missing.ToArray();
    }

    private async ValueTask<string[]> GetMissingSchemaAuthorityColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (string column in new[] { "application_id", "store_instance_id", "baseline_id", "checksum", "generation", "last_plan_id", "applied_at" })
            if (!await ColumnExistsAsync(connection, _names.SchemaBaseline, column, cancellationToken).ConfigureAwait(false)) missing.Add("column:" + _names.SchemaBaseline + "." + column);
        foreach (string column in new[] { "application_id", "generation", "baseline_id", "checksum", "plan_id", "classification", "outcome", "provider_version", "structural_verification", "external_data_migration", "semantic_conversion", "external_attestation_id", "external_signer_id", "applied_at" })
            if (!await ColumnExistsAsync(connection, _names.SchemaHistory, column, cancellationToken).ConfigureAwait(false)) missing.Add("column:" + _names.SchemaHistory + "." + column);
        return missing.ToArray();
    }

    private async ValueTask<string[]> GetMissingRelationColumnsAsync(SqliteConnection connection, SqlitePhysicalModel.RelationModel relation, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (string column in new[] { "source_record_id", "target_record_id", "ordinal" })
            if (!await ColumnExistsAsync(connection, relation.Table, column, cancellationToken).ConfigureAwait(false))
                missing.Add("column:" + relation.Table + "." + column);
        return missing.ToArray();
    }
}
