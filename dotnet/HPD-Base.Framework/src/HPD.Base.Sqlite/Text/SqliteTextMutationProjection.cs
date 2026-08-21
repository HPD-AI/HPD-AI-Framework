namespace HPD.Base.Sqlite;

using System.Collections.Immutable;
using System.Text.Json;

internal sealed class SqliteTextMutationProjection : ISqliteAtomicMutationProjection, ISqliteAtomicMutationProjectionCatalog
{
    private readonly SqliteTextModel _model;
    private readonly SqliteProjectionStatement[] _statements;

    internal SqliteTextMutationProjection(SqliteTextModel model)
    {
        _model = model;
        _statements = model.Indexes.SelectMany(index => new[] { Upsert(index), Delete(index), DeleteFts(index), InsertFts(index), StageUpsert(index), StageDelete(index), Advance(index), AdvancePurge(index) }).ToArray();
    }

    public string Id => "hpd.base.text.sqlite";
    IReadOnlyList<SqliteProjectionStatement> ISqliteAtomicMutationProjectionCatalog.Statements => _statements;
    IReadOnlyList<string> ISqliteAtomicMutationProjectionCatalog.SchemaStatements => _model.SchemaStatements().ToArray();
    IReadOnlyList<string> ISqliteAtomicMutationProjectionCatalog.RequiredSchemaTables => [SqliteTextModel.StateTable, SqliteTextModel.RebuildReceiptTable, SqliteTextModel.RebuildProgressTable, SqliteTextModel.RebuildStageTable, .. _model.Indexes.SelectMany(static value => new[] { value.Table, value.FtsTable })];
    IReadOnlyList<SqliteProjectionTableShape> ISqliteAtomicMutationProjectionCatalog.RequiredSchemaShapes =>
    [
        new(SqliteTextModel.StateTable,
        [
            new("collection_id", "TEXT", true, true), new("index_id", "TEXT", true, true),
            new("generation", "INTEGER", true, false), new("purge_generation", "INTEGER", true, false),
            new("applied_position", "INTEGER", true, false), new("state", "TEXT", true, false),
            new("definition_checksum", "BLOB", true, false),
        ]),
        new(SqliteTextModel.RebuildReceiptTable,
        [new("scope", "TEXT", true, true), new("operation", "TEXT", true, true), new("idempotency_key", "TEXT", true, true), new("fingerprint", "BLOB", true, false), new("previous_generation", "INTEGER", true, false), new("published_generation", "INTEGER", true, false), new("visible_through", "INTEGER", true, false), new("record_count", "INTEGER", true, false), new("publication_checksum", "BLOB", true, false)]),
        new(SqliteTextModel.RebuildProgressTable,
        [new("scope", "TEXT", true, true), new("operation", "TEXT", true, true), new("idempotency_key", "TEXT", true, true), new("fingerprint", "BLOB", true, false), new("collection_id", "TEXT", true, false), new("index_id", "TEXT", true, false), new("expected_generation", "INTEGER", true, false), new("staging_generation", "INTEGER", true, false), new("source_head", "INTEGER", true, false), new("publication_head", "INTEGER", false, false), new("phase", "TEXT", true, false), new("last_record_id", "TEXT", false, false), new("record_count", "INTEGER", true, false), new("canonical_bytes", "INTEGER", true, false), new("rolling_checksum", "BLOB", true, false), new("scan_complete", "INTEGER", true, false)]),
        new(SqliteTextModel.RebuildStageTable,
        [new("scope", "TEXT", true, true), new("operation", "TEXT", true, true), new("idempotency_key", "TEXT", true, true), new("record_id", "TEXT", true, true), new("revision", "TEXT", false, false), new("journal_position", "INTEGER", true, false), new("content", "TEXT", false, false), new("deleted", "INTEGER", true, false)]),
        .. _model.Indexes.Select(static value => new SqliteProjectionTableShape(value.Table,
        [
            new("generation", "INTEGER", true, true), new("record_id", "TEXT", true, true), new("revision", "TEXT", true, false),
            new("journal_position", "INTEGER", true, false),
        ])),
    ];

    public async ValueTask<OperationResult> ApplyAsync(ISqliteAtomicProjectionContext context, BaseAtomicMutationProjectionRequest request, CancellationToken cancellationToken = default)
    {
        foreach (BaseAtomicMutationProjectionFact mutation in request.Mutations)
        {
            foreach (SqliteTextModel.IndexModel index in _model.Indexes.Where(value => value.Definition.CollectionId == mutation.CollectionId))
            {
                string? normalized = mutation.After is null ? null : NormalizedText(mutation.After, index.Definition); if (mutation.After is not null && normalized is null) return Invalid();
                OperationResult<int> ftsDeleted = await context.ExecuteAsync(DeleteFtsId(index), [SqliteProjectionValue.Text("record", (mutation.After ?? mutation.Before!).Id.Value)], cancellationToken).ConfigureAwait(false); if (!ftsDeleted.Status.IsSuccess()) return Copy(ftsDeleted);
                OperationResult<int> applied = mutation.After is null
                    ? await context.ExecuteAsync(DeleteId(index), [SqliteProjectionValue.Text("record", mutation.Before!.Id.Value)], cancellationToken).ConfigureAwait(false)
                    : await context.ExecuteAsync(UpsertId(index),
                    [
                        SqliteProjectionValue.Text("record", mutation.After.Id.Value),
                        SqliteProjectionValue.Text("revision", mutation.After.Revision.Value),
                        SqliteProjectionValue.Integer("position", mutation.JournalPosition.Value),
                    ], cancellationToken).ConfigureAwait(false);
                if (!applied.Status.IsSuccess()) return Copy(applied);
                if (mutation.After is not null) { OperationResult<int> ftsInserted = await context.ExecuteAsync(InsertFtsId(index), [SqliteProjectionValue.Text("record", mutation.After.Id.Value), SqliteProjectionValue.Text("content", normalized!)], cancellationToken).ConfigureAwait(false); if (!ftsInserted.Status.IsSuccess()) return Copy(ftsInserted); }
                OperationResult<int> staged = mutation.After is null
                    ? await context.ExecuteAsync(StageDeleteId(index), [SqliteProjectionValue.Text("record", mutation.Before!.Id.Value), SqliteProjectionValue.Integer("position", mutation.JournalPosition.Value)], cancellationToken).ConfigureAwait(false)
                    : await context.ExecuteAsync(StageUpsertId(index), [SqliteProjectionValue.Text("record", mutation.After.Id.Value), SqliteProjectionValue.Text("revision", mutation.After.Revision.Value), SqliteProjectionValue.Integer("position", mutation.JournalPosition.Value), SqliteProjectionValue.Text("content", normalized!)], cancellationToken).ConfigureAwait(false);
                if (!staged.Status.IsSuccess()) return Copy(staged);
                OperationResult<int> advanced = await context.ExecuteAsync(AdvanceId(index), [SqliteProjectionValue.Integer("position", mutation.JournalPosition.Value)], cancellationToken).ConfigureAwait(false);
                if (!advanced.Status.IsSuccess()) return Copy(advanced);
            }
        }

        if (request.Purge is { } purge)
        {
            foreach (SqliteTextModel.IndexModel index in _model.Indexes.Where(value => value.Definition.CollectionId == purge.CollectionId))
            {
                OperationResult<int> advanced = await context.ExecuteAsync(AdvancePurgeId(index), [SqliteProjectionValue.Integer("purge", purge.PublishedGeneration)], cancellationToken).ConfigureAwait(false);
                if (!advanced.Status.IsSuccess()) return Copy(advanced);
            }
        }
        return OperationResults.NoContent();
    }

    private static SqliteProjectionStatement Upsert(SqliteTextModel.IndexModel index) => new(UpsertId(index), $"INSERT INTO {index.Table}(generation,record_id,revision,journal_position) VALUES ((SELECT generation FROM {SqliteTextModel.StateTable} WHERE collection_id='{Escape(index.Definition.CollectionId)}' AND index_id='{Escape(index.Definition.Id)}'),$record,$revision,$position) ON CONFLICT(generation,record_id) DO UPDATE SET revision=excluded.revision,journal_position=excluded.journal_position;", ["record", "revision", "position"], 1);
    private static SqliteProjectionStatement Delete(SqliteTextModel.IndexModel index) => new(DeleteId(index), $"DELETE FROM {index.Table} WHERE generation=(SELECT generation FROM {SqliteTextModel.StateTable} WHERE collection_id='{Escape(index.Definition.CollectionId)}' AND index_id='{Escape(index.Definition.Id)}') AND record_id=$record;", ["record"], 1);
    private static SqliteProjectionStatement DeleteFts(SqliteTextModel.IndexModel index) => new(DeleteFtsId(index), $"DELETE FROM {index.FtsTable} WHERE generation=(SELECT generation FROM {SqliteTextModel.StateTable} WHERE collection_id='{Escape(index.Definition.CollectionId)}' AND index_id='{Escape(index.Definition.Id)}') AND record_id=$record;", ["record"], 1);
    private static SqliteProjectionStatement InsertFts(SqliteTextModel.IndexModel index) => new(InsertFtsId(index), $"INSERT INTO {index.FtsTable}(generation,record_id,content) VALUES ((SELECT generation FROM {SqliteTextModel.StateTable} WHERE collection_id='{Escape(index.Definition.CollectionId)}' AND index_id='{Escape(index.Definition.Id)}'),$record,$content);", ["record", "content"], 1);
    private static SqliteProjectionStatement StageUpsert(SqliteTextModel.IndexModel index) => new(StageUpsertId(index), $"INSERT INTO {SqliteTextModel.RebuildStageTable}(scope,operation,idempotency_key,record_id,revision,journal_position,content,deleted) SELECT scope,operation,idempotency_key,$record,$revision,$position,$content,0 FROM {SqliteTextModel.RebuildProgressTable} WHERE collection_id='{Escape(index.Definition.CollectionId)}' AND index_id='{Escape(index.Definition.Id)}' AND phase IN ('scan','catchup') ON CONFLICT(scope,operation,idempotency_key,record_id) DO UPDATE SET revision=excluded.revision,journal_position=excluded.journal_position,content=excluded.content,deleted=0 WHERE excluded.journal_position>={SqliteTextModel.RebuildStageTable}.journal_position;", ["record", "revision", "position", "content"], 64);
    private static SqliteProjectionStatement StageDelete(SqliteTextModel.IndexModel index) => new(StageDeleteId(index), $"INSERT INTO {SqliteTextModel.RebuildStageTable}(scope,operation,idempotency_key,record_id,revision,journal_position,content,deleted) SELECT scope,operation,idempotency_key,$record,NULL,$position,NULL,1 FROM {SqliteTextModel.RebuildProgressTable} WHERE collection_id='{Escape(index.Definition.CollectionId)}' AND index_id='{Escape(index.Definition.Id)}' AND phase IN ('scan','catchup') ON CONFLICT(scope,operation,idempotency_key,record_id) DO UPDATE SET revision=NULL,journal_position=excluded.journal_position,content=NULL,deleted=1 WHERE excluded.journal_position>={SqliteTextModel.RebuildStageTable}.journal_position;", ["record", "position"], 64);
    private static SqliteProjectionStatement Advance(SqliteTextModel.IndexModel index) => new(AdvanceId(index), $"UPDATE {SqliteTextModel.StateTable} SET applied_position=MAX(applied_position,$position) WHERE collection_id='{index.Definition.CollectionId.Replace("'", "''", StringComparison.Ordinal)}' AND index_id='{index.Definition.Id.Replace("'", "''", StringComparison.Ordinal)}';", ["position"], 1);
    private static SqliteProjectionStatement AdvancePurge(SqliteTextModel.IndexModel index) => new(AdvancePurgeId(index), $"UPDATE {SqliteTextModel.StateTable} SET purge_generation=$purge WHERE collection_id='{index.Definition.CollectionId.Replace("'", "''", StringComparison.Ordinal)}' AND index_id='{index.Definition.Id.Replace("'", "''", StringComparison.Ordinal)}';", ["purge"], 1);
    private static string UpsertId(SqliteTextModel.IndexModel index) => index.Definition.Id + ".text.upsert";
    private static string DeleteId(SqliteTextModel.IndexModel index) => index.Definition.Id + ".text.delete";
    private static string DeleteFtsId(SqliteTextModel.IndexModel index) => index.Definition.Id + ".text.fts.delete";
    private static string InsertFtsId(SqliteTextModel.IndexModel index) => index.Definition.Id + ".text.fts.insert";
    private static string StageUpsertId(SqliteTextModel.IndexModel index) => index.Definition.Id + ".text.rebuild.stage.upsert";
    private static string StageDeleteId(SqliteTextModel.IndexModel index) => index.Definition.Id + ".text.rebuild.stage.delete";
    private static string AdvanceId(SqliteTextModel.IndexModel index) => index.Definition.Id + ".text.advance";
    private static string AdvancePurgeId(SqliteTextModel.IndexModel index) => index.Definition.Id + ".text.advancePurge";
    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static OperationResult Copy(OperationResult<int> value) => new() { Status = value.Status, Error = value.Error, Warnings = value.Warnings, Diagnostics = value.Diagnostics };
    internal static string? NormalizedText(BaseAtomicProjectionRecord record, BaseTextIndexDefinition index)
    {
        try
        {
            long total = 0; var normalized = new List<string>();
            foreach (BaseTextIndexFieldDefinition declared in index.Fields)
            {
                BaseAtomicProjectionField? field = record.Fields.Cast<BaseAtomicProjectionField?>().SingleOrDefault(value => value!.Value.StableFieldId == declared.StableFieldId);
                if (field is null || field.Value.Value.Kind == BaseAtomicProjectionValueKind.Null) continue;
                using JsonDocument document = JsonDocument.Parse(field.Value.Value.CanonicalJsonUtf8.ToArray());
                ImmutableArray<string> tokens = BaseTextAnalyzer.Analyze(document.RootElement.GetString());
                total = checked(total + tokens.Sum(static token => (long)System.Text.Encoding.UTF8.GetByteCount(token)));
                normalized.AddRange(tokens);
            }
            return total <= index.Limits.MaximumNormalizedBytesPerRecord ? string.Join(' ', normalized) : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or OverflowException) { return null; }
    }
    private static OperationResult Invalid() => new() { Status = OperationStatus.ValidationFailed, Error = new BaseError { Code = BaseTextErrorCodes.BudgetExceeded, Message = "The indexed text exceeds its installed limit.", Category = ErrorCategory.Validation } };
}
