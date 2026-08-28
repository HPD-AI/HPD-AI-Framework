using System.Text.Json;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace HPD.Auth.LegacyImporter;

/// <summary>Opens and attests the single frozen legacy SQLite source schema.</summary>
internal sealed class LegacySqliteSource : IAsyncDisposable
{
    private readonly string _path;
    private readonly LegacySourceFileIdentity _initialIdentity;
    private readonly SqliteConnection _connection;

    private LegacySqliteSource(string path, LegacySourceFileIdentity identity, SqliteConnection connection)
    {
        _path = path;
        _initialIdentity = identity;
        _connection = connection;
    }

    internal static async ValueTask<LegacySqliteSource> OpenAsync(string sourcePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string path = Path.GetFullPath(sourcePath);
        RejectSidecars(path);
        LegacySourceFileIdentity identity = await LegacySourceFileIdentity.CaptureAsync(path, cancellationToken).ConfigureAwait(false);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 5,
        };
        var connection = new SqliteConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecutePragmaAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);
            await ExecutePragmaAsync(connection, "PRAGMA query_only=ON;", cancellationToken).ConfigureAwait(false);
            var source = new LegacySqliteSource(path, identity, connection);
            await source.VerifyCatalogAsync(cancellationToken).ConfigureAwait(false);
            await source.VerifyUnchangedAsync(cancellationToken).ConfigureAwait(false);
            return source;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async ValueTask VerifyUnchangedAsync(CancellationToken cancellationToken)
    {
        RejectSidecars(_path);
        LegacySourceFileIdentity current = await LegacySourceFileIdentity.CaptureAsync(_path, cancellationToken).ConfigureAwait(false);
        if (!_initialIdentity.SecurelyEquals(current))
            throw new LegacyImportException(LegacyImportFailure.SourceChanged, "The legacy source database changed during import.");
    }

    internal async IAsyncEnumerable<LegacyExtractedChunk> ReadChunksAsync(
        LegacyExtractionStatement statement,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(statement);
        LegacyExtractionStatement installed = LegacyExtractionPlan.Load().SingleOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Table, statement.Table)
            && StringComparer.Ordinal.Equals(candidate.CommandText, statement.CommandText))
            ?? throw new LegacyImportException(LegacyImportFailure.SourceSchemaMismatch, "The extraction statement is not installed.");

        await VerifyUnchangedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = installed.CommandText;
        command.CommandTimeout = 5;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = ImmutableArray.CreateBuilder<LegacyExtractedRow>(200);
        long chunkOrdinal = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = ImmutableArray.CreateBuilder<LegacySqliteValue>(reader.FieldCount);
            for (int ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                values.Add(LegacySqliteValue.Own(reader.GetValue(ordinal)));
            rows.Add(new LegacyExtractedRow(values.MoveToImmutable()));
            if (rows.Count == 200)
            {
                yield return new LegacyExtractedChunk(installed.Table, chunkOrdinal++, rows.MoveToImmutable());
                rows = ImmutableArray.CreateBuilder<LegacyExtractedRow>(200);
                await VerifyUnchangedAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        if (rows.Count != 0)
            yield return new LegacyExtractedChunk(installed.Table, chunkOrdinal, rows.ToImmutable());
        await VerifyUnchangedAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask VerifyCatalogAsync(CancellationToken cancellationToken)
    {
        using JsonDocument catalog = JsonDocument.Parse(LegacyImportAssets.ReadCatalog());
        JsonElement tables = catalog.RootElement.GetProperty("tables");
        var expectedNames = tables.EnumerateArray()
            .Select(table => table.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> actualNames = await ReadApplicationTablesAsync(cancellationToken).ConfigureAwait(false);
        if (!expectedNames.SetEquals(actualNames))
            Mismatch("The source table inventory does not match the frozen catalog.");

        foreach (JsonElement table in tables.EnumerateArray())
            await VerifyTableAsync(table, cancellationToken).ConfigureAwait(false);

        await VerifyNoUnexpectedObjectsAsync(tables, cancellationToken).ConfigureAwait(false);
        await VerifyMigrationAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private async ValueTask VerifyTableAsync(JsonElement expected, CancellationToken cancellationToken)
    {
        string table = expected.GetProperty("name").GetString()!;
        List<ColumnShape> actualColumns = await ReadColumnsAsync(table, cancellationToken).ConfigureAwait(false);
        JsonElement.ArrayEnumerator expectedColumns = expected.GetProperty("columns").EnumerateArray();
        int ordinal = 0;
        foreach (JsonElement column in expectedColumns)
        {
            if (ordinal >= actualColumns.Count)
                Mismatch($"Table '{table}' is missing a column.");
            ColumnShape actual = actualColumns[ordinal];
            string name = column.GetProperty("name").GetString()!;
            string type = column.GetProperty("type").GetString()!;
            bool nullable = column.GetProperty("nullable").GetBoolean();
            int expectedPk = PrimaryKeyOrdinal(expected, name);
            if (!StringComparer.Ordinal.Equals(actual.Name, name)
                || !StringComparer.OrdinalIgnoreCase.Equals(actual.Type, type)
                || actual.Nullable != nullable
                || actual.PrimaryKeyOrdinal != expectedPk
                || actual.Hidden != 0)
                Mismatch($"Column {table}.{name} does not match the frozen catalog.");
            ordinal++;
        }
        if (ordinal != actualColumns.Count)
            Mismatch($"Table '{table}' has unexpected columns.");

        await VerifyIndexesAsync(table, expected.GetProperty("indexes"), cancellationToken).ConfigureAwait(false);
        await VerifyForeignKeysAsync(table, expected.GetProperty("foreignKeys"), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<HashSet<string>> ReadApplicationTablesAsync(CancellationToken cancellationToken)
    {
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite\\_%' ESCAPE '\\' ORDER BY name COLLATE BINARY;";
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) names.Add(reader.GetString(0));
        return names;
    }

    private async ValueTask<List<ColumnShape>> ReadColumnsAsync(string table, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = $"PRAGMA table_xinfo({QuoteIdentifier(table)});";
        var columns = new List<ColumnShape>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            int pk = reader.GetInt32(5);
            bool nullable = reader.GetInt32(3) == 0 && pk == 0;
            columns.Add(new ColumnShape(reader.GetString(1), reader.GetString(2), nullable, pk, reader.GetInt32(6)));
        }
        return columns;
    }

    private async ValueTask VerifyIndexesAsync(string table, JsonElement expectedIndexes, CancellationToken cancellationToken)
    {
        var actual = new Dictionary<string, IndexShape>(StringComparer.Ordinal);
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list({QuoteIdentifier(table)});";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string name = reader.GetString(1);
            string origin = reader.GetString(3);
            if (origin == "pk" || name.StartsWith("sqlite_autoindex_", StringComparison.Ordinal)) continue;
            actual.Add(name, new IndexShape(reader.GetInt32(2) != 0, []));
        }
        await reader.DisposeAsync().ConfigureAwait(false);

        foreach (string name in actual.Keys.ToArray())
            actual[name] = actual[name] with { Columns = await ReadIndexColumnsAsync(name, cancellationToken).ConfigureAwait(false) };

        var expected = expectedIndexes.EnumerateArray().ToArray();
        if (actual.Count != expected.Length) Mismatch($"Table '{table}' has an unexpected index inventory.");
        foreach (JsonElement index in expected)
        {
            string name = index.GetProperty("name").GetString()!;
            string[] columns = index.GetProperty("columns").EnumerateArray().Select(value => value.GetString()!).ToArray();
            bool unique = index.GetProperty("unique").GetBoolean();
            if (!actual.TryGetValue(name, out IndexShape? shape) || shape.Unique != unique || !shape.Columns.SequenceEqual(columns, StringComparer.Ordinal))
                Mismatch($"Index '{name}' does not match the frozen catalog.");
        }
    }

    private async ValueTask<string[]> ReadIndexColumnsAsync(string index, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = $"PRAGMA index_xinfo({QuoteIdentifier(index)});";
        var columns = new List<(int Sequence, string Name)>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            if (reader.GetInt32(5) != 0 && reader.GetInt32(1) >= 0) columns.Add((reader.GetInt32(0), reader.GetString(2)));
        return columns.OrderBy(value => value.Sequence).Select(value => value.Name).ToArray();
    }

    private async ValueTask VerifyForeignKeysAsync(string table, JsonElement expectedForeignKeys, CancellationToken cancellationToken)
    {
        var actual = new List<ForeignKeyShape>();
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list({QuoteIdentifier(table)});";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            actual.Add(new ForeignKeyShape(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(6)));

        var expected = expectedForeignKeys.EnumerateArray().ToArray();
        int expectedColumnCount = expected.Sum(item => item.GetProperty("columns").GetArrayLength());
        if (actual.Count != expectedColumnCount) Mismatch($"Table '{table}' has an unexpected foreign-key inventory.");
        foreach (JsonElement foreignKey in expected)
        {
            string principalTable = foreignKey.GetProperty("principalTable").GetString()!;
            string onDelete = foreignKey.GetProperty("onDelete").GetString()!.ToUpperInvariant();
            string[] columns = foreignKey.GetProperty("columns").EnumerateArray().Select(value => value.GetString()!).ToArray();
            string[] principal = foreignKey.GetProperty("principalColumns").EnumerateArray().Select(value => value.GetString()!).ToArray();
            for (int i = 0; i < columns.Length; i++)
                if (!actual.Any(item => item.Sequence == i && item.Table == principalTable && item.From == columns[i] && item.To == principal[i] && item.OnDelete == onDelete))
                    Mismatch($"A foreign key on table '{table}' does not match the frozen catalog.");
        }
    }

    private async ValueTask VerifyNoUnexpectedObjectsAsync(JsonElement tables, CancellationToken cancellationToken)
    {
        var expectedIndexes = tables.EnumerateArray()
            .SelectMany(table => table.GetProperty("indexes").EnumerateArray())
            .Select(index => index.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT type,name FROM sqlite_schema WHERE type IN ('index','trigger','view') AND name NOT LIKE 'sqlite\\_%' ESCAPE '\\' ORDER BY type,name COLLATE BINARY;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            if (reader.GetString(0) != "index" || !expectedIndexes.Remove(reader.GetString(1)))
                Mismatch("The source contains an unexpected index, trigger, or view.");
        if (expectedIndexes.Count != 0) Mismatch("The source is missing a catalog index.");
    }

    private async ValueTask VerifyMigrationAsync(CancellationToken cancellationToken)
    {
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY rowid DESC LIMIT 1;";
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is not string migration || !StringComparer.Ordinal.Equals(migration, LegacyImportAssets.MigrationId))
            Mismatch("The source migration identity does not match the frozen catalog.");
    }

    private static int PrimaryKeyOrdinal(JsonElement table, string column)
    {
        int ordinal = 1;
        foreach (JsonElement value in table.GetProperty("primaryKey").EnumerateArray())
        {
            if (StringComparer.Ordinal.Equals(value.GetString(), column)) return ordinal;
            ordinal++;
        }
        return 0;
    }

    private static string QuoteIdentifier(string value) => '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

    private static async ValueTask ExecutePragmaAsync(SqliteConnection connection, string text, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = text;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void RejectSidecars(string path)
    {
        if (File.Exists(path + "-wal") || File.Exists(path + "-shm"))
            throw new LegacyImportException(LegacyImportFailure.SourceUnavailable, "The stopped legacy source must not have WAL or SHM sidecars.");
    }

    private static void Mismatch(string message) => throw new LegacyImportException(LegacyImportFailure.SourceSchemaMismatch, message);

    private sealed record ColumnShape(string Name, string Type, bool Nullable, int PrimaryKeyOrdinal, int Hidden);
    private sealed record IndexShape(bool Unique, string[] Columns);
    private sealed record ForeignKeyShape(int Id, int Sequence, string Table, string From, string To, string OnDelete);
}

internal enum LegacySqliteValueKind { Null, Integer, Real, Text, Blob }

internal sealed record LegacySqliteValue
{
    private LegacySqliteValue(LegacySqliteValueKind kind, long integer, double real, string? text, byte[]? blob)
    {
        Kind = kind;
        Integer = integer;
        Real = real;
        Text = text;
        Blob = blob;
    }

    internal LegacySqliteValueKind Kind { get; }
    internal long Integer { get; }
    internal double Real { get; }
    internal string? Text { get; }
    internal byte[]? Blob { get; }

    internal static LegacySqliteValue Own(object value) => value switch
    {
        DBNull => new(LegacySqliteValueKind.Null, 0, 0, null, null),
        long integer => new(LegacySqliteValueKind.Integer, integer, 0, null, null),
        int integer => new(LegacySqliteValueKind.Integer, integer, 0, null, null),
        double real => new(LegacySqliteValueKind.Real, 0, real, null, null),
        string text => new(LegacySqliteValueKind.Text, 0, 0, text, null),
        byte[] blob => new(LegacySqliteValueKind.Blob, 0, 0, null, blob.ToArray()),
        _ => throw new LegacyImportException(LegacyImportFailure.SourceSchemaMismatch, "A legacy value has an unsupported SQLite storage class."),
    };
}

internal sealed record LegacyExtractedRow(ImmutableArray<LegacySqliteValue> Values);

internal sealed record LegacyExtractedChunk(string Table, long ChunkOrdinal, ImmutableArray<LegacyExtractedRow> Rows);
