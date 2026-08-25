using System.Collections.Immutable;
using System.Text.Json;
using HPD.Base.Sqlite;

namespace HPD.Base.Sqlite;

internal sealed class SqliteVecMutationProjection : ISqliteAtomicMutationProjection, ISqliteAtomicMutationProjectionCatalog
{
    private readonly SqliteVecModel _model;
    private readonly SqliteProjectionStatement[] _statements;
    internal SqliteVecMutationProjection(SqliteVecModel model)
    {
        _model = model;
        _statements = model.Indexes.SelectMany(index => new[] { Upsert(index), Delete(index), Advance(index), AdvancePurge(index) }).ToArray();
    }
    public string Id => "hpd.base.vector.sqlitevec";
    IReadOnlyList<SqliteProjectionStatement> ISqliteAtomicMutationProjectionCatalog.Statements => _statements;
    IReadOnlyList<string> ISqliteAtomicMutationProjectionCatalog.SchemaStatements => _model.SchemaStatements().ToArray();
    IReadOnlyList<string> ISqliteAtomicMutationProjectionCatalog.RequiredSchemaTables => [SqliteVecModel.StateTable, .. _model.Indexes.Select(static index => index.Table)];
    IReadOnlyList<SqliteProjectionTableShape> ISqliteAtomicMutationProjectionCatalog.RequiredSchemaShapes =>
    [
        new(SqliteVecModel.StateTable,
        [
            new("collection_id", "TEXT", true, true), new("index_id", "TEXT", true, true),
            new("generation", "INTEGER", true, false), new("purge_generation", "INTEGER", true, false),
            new("applied_position", "INTEGER", true, false), new("state", "TEXT", true, false),
        ]),
        .. _model.Indexes.Select(static index => new SqliteProjectionTableShape(index.Table,
        [
            new("record_id", "TEXT", true, true), new("revision", "TEXT", true, false),
            new("journal_position", "INTEGER", true, false), new("vector", "BLOB", true, false),
            .. index.Filters.SelectMany(static filter => new[]
            {
                new SqliteProjectionColumnShape(filter.PresenceColumn, "INTEGER", true, false),
                new SqliteProjectionColumnShape(filter.ValueColumn, filter.SqlType, false, false),
            }),
        ])),
    ];

    public async ValueTask<OperationResult> ApplyAsync(ISqliteAtomicProjectionContext context, BaseAtomicMutationProjectionRequest request, CancellationToken cancellationToken = default)
    {
        foreach (BaseAtomicMutationProjectionFact mutation in request.Mutations)
        {
            foreach (SqliteVecModel.IndexModel index in _model.Indexes.Where(item => item.Definition.CollectionId == mutation.CollectionId))
            {
                if (mutation.After is null)
                {
                    OperationResult<int> deleted = await context.ExecuteAsync(DeleteId(index), [SqliteProjectionValue.Text("record", mutation.Before!.Id.Value)], cancellationToken).ConfigureAwait(false);
                    if (!deleted.Status.IsSuccess()) return Copy(deleted);
                    OperationResult<int> deleteAdvanced = await context.ExecuteAsync(AdvanceId(index), [SqliteProjectionValue.Integer("position", mutation.JournalPosition.Value)], cancellationToken).ConfigureAwait(false);
                    if (!deleteAdvanced.Status.IsSuccess()) return Copy(deleteAdvanced);
                    continue;
                }
                BaseAtomicProjectionField? vectorField = mutation.After.Fields.Cast<BaseAtomicProjectionField?>().SingleOrDefault(field => field!.Value.StableFieldId == index.Definition.VectorFieldId);
                if (vectorField is null || vectorField.Value.Value.Kind == BaseAtomicProjectionValueKind.Null)
                {
                    FieldDefinition declared = (index.Collection.Fields ?? []).Single(field => field.Id == index.Definition.VectorFieldId);
                    if (declared.Presence == BaseFieldPresence.Required && declared.Nullability == BaseFieldNullability.NonNullable) return Failed("A required vector projection field is unavailable.");
                    OperationResult<int> removed = await context.ExecuteAsync(DeleteId(index), [SqliteProjectionValue.Text("record", mutation.After.Id.Value)], cancellationToken).ConfigureAwait(false);
                    if (!removed.Status.IsSuccess()) return Copy(removed);
                    OperationResult<int> optionalAdvanced = await context.ExecuteAsync(AdvanceId(index), [SqliteProjectionValue.Integer("position", mutation.JournalPosition.Value)], cancellationToken).ConfigureAwait(false);
                    if (!optionalAdvanced.Status.IsSuccess()) return Copy(optionalAdvanced);
                    continue;
                }
                float[] vector;
                try { vector = JsonSerializer.Deserialize(vectorField.Value.Value.CanonicalJsonUtf8.AsSpan(), SqliteVecJsonContext.Default.SingleArray) ?? []; }
                catch (JsonException) { return Failed("A vector projection value is invalid."); }
                if (vector.Length != index.Definition.Dimensions || vector.Any(static value => !float.IsFinite(value)) || index.Definition.Function == BaseVectorFunction.CosineSimilarity && vector.All(static value => value == 0F)) return Failed("A vector projection value is invalid.");
                var parameters = ImmutableArray.CreateBuilder<SqliteProjectionValue>(4 + index.Filters.Length * 2);
                parameters.Add(SqliteProjectionValue.Text("record", mutation.After.Id.Value));
                parameters.Add(SqliteProjectionValue.Text("revision", mutation.After.Revision.Value));
                parameters.Add(SqliteProjectionValue.Integer("position", mutation.JournalPosition.Value));
                parameters.Add(SqliteProjectionValue.Bytes("vector", FloatBytes(vector)));
                foreach (SqliteVecModel.FilterModel filter in index.Filters)
                {
                    BaseAtomicProjectionField? field = mutation.After.Fields.Cast<BaseAtomicProjectionField?>().SingleOrDefault(item => item!.Value.StableFieldId == filter.Definition.Id);
                    parameters.Add(SqliteProjectionValue.Boolean("p_" + parameters.Count, field is not null));
                    parameters.Add(field is null || field.Value.Value.Kind == BaseAtomicProjectionValueKind.Null ? SqliteProjectionValue.Null("v_" + parameters.Count) : Value("v_" + parameters.Count, field.Value.Value));
                }
                OperationResult<int> upserted = await context.ExecuteAsync(UpsertId(index), parameters.MoveToImmutable(), cancellationToken).ConfigureAwait(false);
                if (!upserted.Status.IsSuccess()) return Copy(upserted);
                OperationResult<int> advanced = await context.ExecuteAsync(AdvanceId(index), [SqliteProjectionValue.Integer("position", mutation.JournalPosition.Value)], cancellationToken).ConfigureAwait(false);
                if (!advanced.Status.IsSuccess()) return Copy(advanced);
            }
        }
        if (request.Purge is { } purge)
        {
            foreach (SqliteVecModel.IndexModel index in _model.Indexes.Where(item => item.Definition.CollectionId == purge.CollectionId))
            {
                OperationResult<int> advanced = await context.ExecuteAsync(AdvancePurgeId(index), [SqliteProjectionValue.Integer("purge", purge.PublishedGeneration)], cancellationToken).ConfigureAwait(false);
                if (!advanced.Status.IsSuccess()) return Copy(advanced);
            }
        }
        return OperationResults.NoContent();
    }

    private static SqliteProjectionStatement Upsert(SqliteVecModel.IndexModel index)
    {
        var names = new List<string> { "record", "revision", "position", "vector" };
        for (int i = 0; i < index.Filters.Length; i++) { names.Add("p_" + names.Count); names.Add("v_" + names.Count); }
        string columns = string.Concat(index.Filters.Select(filter => $", {filter.PresenceColumn}, {filter.ValueColumn}"));
        string values = string.Concat(names.Skip(4).Select(name => ", $" + name));
        string updates = string.Concat(index.Filters.Select(filter => $", {filter.PresenceColumn}=excluded.{filter.PresenceColumn}, {filter.ValueColumn}=excluded.{filter.ValueColumn}"));
        return new(UpsertId(index), $"INSERT INTO {index.Table}(record_id,revision,journal_position,vector{columns}) VALUES ($record,$revision,$position,$vector{values}) ON CONFLICT(record_id) DO UPDATE SET revision=excluded.revision,journal_position=excluded.journal_position,vector=excluded.vector{updates};", names.ToArray(), 1);
    }
    private static SqliteProjectionStatement Delete(SqliteVecModel.IndexModel index) => new(DeleteId(index), $"DELETE FROM {index.Table} WHERE record_id=$record;", ["record"], 1);
    private static SqliteProjectionStatement Advance(SqliteVecModel.IndexModel index) => new(AdvanceId(index), $"UPDATE {SqliteVecModel.StateTable} SET applied_position=MAX(applied_position,$position) WHERE collection_id='{index.Definition.CollectionId.Replace("'", "''", StringComparison.Ordinal)}' AND index_id='{index.Definition.Id.Replace("'", "''", StringComparison.Ordinal)}';", ["position"], 1);
    private static SqliteProjectionStatement AdvancePurge(SqliteVecModel.IndexModel index) => new(AdvancePurgeId(index), $"UPDATE {SqliteVecModel.StateTable} SET purge_generation=$purge WHERE collection_id='{index.Definition.CollectionId.Replace("'", "''", StringComparison.Ordinal)}' AND index_id='{index.Definition.Id.Replace("'", "''", StringComparison.Ordinal)}';", ["purge"], 1);
    private static string UpsertId(SqliteVecModel.IndexModel index) => index.Definition.Id + ".upsert";
    private static string DeleteId(SqliteVecModel.IndexModel index) => index.Definition.Id + ".delete";
    private static string AdvanceId(SqliteVecModel.IndexModel index) => index.Definition.Id + ".advance";
    private static string AdvancePurgeId(SqliteVecModel.IndexModel index) => index.Definition.Id + ".advancePurge";
    private static byte[] FloatBytes(float[] values) { byte[] bytes = new byte[values.Length * sizeof(float)]; Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length); return bytes; }
    private static SqliteProjectionValue Value(string name, BaseAtomicProjectionValue value)
    {
        using JsonDocument document = JsonDocument.Parse(value.CanonicalJsonUtf8.ToArray()); JsonElement element = document.RootElement;
        return value.Kind switch { BaseAtomicProjectionValueKind.Boolean => SqliteProjectionValue.Boolean(name, element.GetBoolean()), BaseAtomicProjectionValueKind.Integer => SqliteProjectionValue.Integer(name, element.GetInt64()), BaseAtomicProjectionValueKind.String => SqliteProjectionValue.Text(name, element.GetString()!), _ => throw new InvalidOperationException("A declared vector filter value is not portable.") };
    }
    private static OperationResult Copy(OperationResult<int> result) => new() { Status = result.Status, Error = result.Error, Warnings = result.Warnings, Diagnostics = result.Diagnostics };
    private static OperationResult Failed(string message) => new() { Status = OperationStatus.ValidationFailed, Error = new BaseError { Code = "base.vector.invalid", Message = message, Category = ErrorCategory.Validation } };
}
