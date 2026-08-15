using System.Collections.Immutable;

namespace HPD.Base.Sqlite;

/// <summary>Contributes provider-owned derived state inside the canonical SQLite mutation transaction.</summary>
public interface ISqliteAtomicMutationProjection
{
    /// <summary>Gets the stable infrastructure identifier.</summary>
    string Id { get; }
    /// <summary>Applies immutable canonical facts through a restricted statement catalog.</summary>
    ValueTask<OperationResult> ApplyAsync(ISqliteAtomicProjectionContext context, BaseAtomicMutationProjectionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Executes only prevalidated contributor-owned statements in the current SQLite transaction.</summary>
public interface ISqliteAtomicProjectionContext
{
    /// <summary>Gets the bound schema generation.</summary>
    long SchemaGeneration { get; }
    /// <summary>Executes one catalog statement with closed immutable parameters.</summary>
    ValueTask<OperationResult<int>> ExecuteAsync(string statementId, ImmutableArray<SqliteProjectionValue> parameters, CancellationToken cancellationToken = default);
}

/// <summary>Names the closed SQLite projection parameter kinds.</summary>
public enum SqliteProjectionValueKind
{
    /// <summary>A database null.</summary>
    Null,
    /// <summary>A signed 64-bit integer.</summary>
    Integer,
    /// <summary>A Boolean encoded as zero or one.</summary>
    Boolean,
    /// <summary>Bounded UTF-8 text.</summary>
    Text,
    /// <summary>Bounded copied bytes.</summary>
    Bytes,
}

/// <summary>Contains one named immutable closed SQLite projection parameter.</summary>
public readonly struct SqliteProjectionValue
{
    private readonly object? _value;
    private SqliteProjectionValue(string name, SqliteProjectionValueKind kind, object? value) { ArgumentException.ThrowIfNullOrWhiteSpace(name); Name = new string(name.AsSpan()); Kind = kind; _value = value; }
    /// <summary>Gets the parameter name without a prefix marker.</summary>
    public string Name { get; }
    /// <summary>Gets the closed value kind.</summary>
    public SqliteProjectionValueKind Kind { get; }
    /// <summary>Creates a null parameter.</summary>
    public static SqliteProjectionValue Null(string name) => new(name, SqliteProjectionValueKind.Null, null);
    /// <summary>Creates an integer parameter.</summary>
    public static SqliteProjectionValue Integer(string name, long value) => new(name, SqliteProjectionValueKind.Integer, value);
    /// <summary>Creates a Boolean parameter.</summary>
    public static SqliteProjectionValue Boolean(string name, bool value) => new(name, SqliteProjectionValueKind.Boolean, value);
    /// <summary>Creates a bounded owned text parameter.</summary>
    public static SqliteProjectionValue Text(string name, string value) { ArgumentNullException.ThrowIfNull(value); if (System.Text.Encoding.UTF8.GetByteCount(value) > 65_536) throw new ArgumentOutOfRangeException(nameof(value)); return new(name, SqliteProjectionValueKind.Text, new string(value.AsSpan())); }
    /// <summary>Creates a bounded owned byte parameter.</summary>
    public static SqliteProjectionValue Bytes(string name, ReadOnlySpan<byte> value) { if (value.Length > 262_144) throw new ArgumentOutOfRangeException(nameof(value)); return new(name, SqliteProjectionValueKind.Bytes, value.ToArray()); }
    internal object Value => Kind switch { SqliteProjectionValueKind.Null => DBNull.Value, SqliteProjectionValueKind.Boolean => (bool)_value! ? 1L : 0L, SqliteProjectionValueKind.Bytes => ((byte[])_value!).ToArray(), _ => _value! };
}

internal interface ISqliteAtomicMutationProjectionCatalog
{
    IReadOnlyList<SqliteProjectionStatement> Statements { get; }
    IReadOnlyList<string> SchemaStatements { get; }
    IReadOnlyList<string> RequiredSchemaTables { get; }
    IReadOnlyList<SqliteProjectionTableShape> RequiredSchemaShapes { get; }
}

internal sealed record SqliteProjectionStatement(string Id, string Sql, string[] ParameterNames, int MaximumAffectedRows);
internal sealed record SqliteProjectionTableShape(string Table, IReadOnlyList<SqliteProjectionColumnShape> Columns);
internal sealed record SqliteProjectionColumnShape(string Name, string Type, bool NotNull, bool PrimaryKey);
