using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

/// <summary>Represents a sqlite record store.</summary>
public sealed partial class SqliteRecordStore
{
    /// <inheritdoc />
    public RecordIncludeExecutionCapability Includes { get; }

    /// <inheritdoc />
    public async ValueTask<OperationResult<RecordIncludeExecutionResult>> ExecuteIncludeAsync(
        RecordIncludeExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var acquisition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        acquisition.CancelAfter(request.AcquisitionTimeout);
        IAsyncDisposable lease;
        try { lease = await _schemaGenerationGate.AcquireSharedAsync(acquisition.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return IncludeFailure("base.include.snapshotUnsupported", "SQLite include snapshot acquisition timed out."); }

        await using (lease.ConfigureAwait(false))
        {
            try
            {
                using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                execution.CancelAfter(request.ExecutionTimeout);
                await using SqliteConnection connection = await OpenInitializedAsync(execution.Token).ConfigureAwait(false);
                await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(execution.Token).ConfigureAwait(false);
                var state = new IncludeState(request, connection, transaction, execution.Token);
                RecordPage roots = await ReadIncludePageAsync(request.RootCollection, request.RootQuery with { Include = null }, state).ConfigureAwait(false);
                RecordEnvelope[] expanded = await ExpandBatchAsync(roots.Items, request.RootCollection, request.IncludePlan, state, 1).ConfigureAwait(false);
                await transaction.CommitAsync(execution.Token).ConfigureAwait(false);
                return OperationResults.Ok(new RecordIncludeExecutionResult
                {
                    Page = roots with { Items = expanded },
                    SchemaGeneration = Volatile.Read(ref _schemaGeneration),
                    DependencyEvidence = state.Dependencies.Select(static id => new BaseReadDependencyEvidence { CollectionId = id }).ToArray(),
                });
            }
            catch (IncludeLimitException)
            { return IncludeFailure("base.include.limitExceeded", "SQLite include limits were exceeded."); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return IncludeFailure("base.include.limitExceeded", "SQLite include execution timed out."); }
            catch (OperationCanceledException) { throw; }
            catch
            { return IncludeFailure("base.include.invalid", "SQLite include execution failed."); }
        }
    }

    private async ValueTask<RecordEnvelope[]> ExpandBatchAsync(
        RecordEnvelope[] parents,
        CollectionDefinition parentCollection,
        RecordInclude[] includes,
        IncludeState state,
        int depth)
    {
        if (depth > Includes.MaxDepth) throw new IncludeLimitException();
        var results = parents.Select(_ => new RecordIncludeResult[includes.Length]).ToArray();
        for (int index = 0; index < includes.Length; index++)
        {
            state.Operation();
            RecordInclude include = includes[index];
            (RelationDefinition relation, bool inverse) = ResolveInclude(parentCollection.Id, include.NavigationId);
            CollectionDefinition targetDefinition = _physical.Collection(inverse ? relation.SourceCollectionId : relation.TargetCollectionId).Definition;
            Dictionary<string, string[]> related = await RelatedIdsBatchAsync(parents, relation, inverse, state).ConfigureAwait(false);
            string[] ids = related.Values.SelectMany(static value => value).Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Length > state.Request.MaxResultRows) throw new IncludeLimitException();
            RecordEnvelope[] targets = ids.Length == 0
                ? []
                : (await ReadIncludePageAsync(targetDefinition, TargetQuery(ids, include, state.Policy(targetDefinition.Id), state.Request.MaxResultRows), state).ConfigureAwait(false)).Items;
            RecordEnvelope[] expandedTargets = include.Includes is { Length: > 0 }
                ? await ExpandBatchAsync(targets, targetDefinition, include.Includes, state, depth + 1).ConfigureAwait(false)
                : targets;
            var byId = expandedTargets.ToDictionary(static target => target.Id.Value, StringComparer.Ordinal);
            Dictionary<string, int> sortedOrder = targets.Select((target, targetIndex) => (target.Id.Value, targetIndex)).ToDictionary(static item => item.Value, static item => item.targetIndex, StringComparer.Ordinal);
            for (int parentIndex = 0; parentIndex < parents.Length; parentIndex++)
            {
                string[] parentIds = related.GetValueOrDefault(parents[parentIndex].Id.Value) ?? [];
                IEnumerable<string> ordered = include.Sort is { Length: > 0 }
                    ? parentIds.Where(byId.ContainsKey).OrderBy(id => sortedOrder[id])
                    : parentIds.Where(byId.ContainsKey);
                int limit = include.Limit ?? 100;
                RecordEnvelope[] nested = ordered.Take(limit)
                    .Select(id => SelectIncludeFields(byId[id], targetDefinition, include, state.Policy(targetDefinition.Id).ReadMask)).ToArray();
                foreach (RecordEnvelope record in nested) state.Record(record);

                bool many = inverse ? relation.InverseMultiplicity == BaseRelationMultiplicity.Many : relation.LocalMultiplicity == BaseRelationMultiplicity.Many;
                results[parentIndex][index] = many
                    ? new RecordIncludeResult { NavigationId = include.NavigationId, Kind = RecordIncludeKind.Many, Records = nested }
                    : nested.FirstOrDefault() is { } single
                        ? new RecordIncludeResult { NavigationId = include.NavigationId, Kind = RecordIncludeKind.One, Record = single }
                        : new RecordIncludeResult { NavigationId = include.NavigationId, Kind = RecordIncludeKind.None };
            }
        }
        return parents.Select((parent, index) => parent with { Includes = results[index] }).ToArray();
    }

    private (RelationDefinition Relation, bool Inverse) ResolveInclude(string collectionId, string navigationId)
    {
        foreach (SqlitePhysicalModel.CollectionModel collection in _physical.Collections)
        foreach (SqlitePhysicalModel.FieldModel field in collection.Fields)
        {
            if (field.Definition.Relation is not { } relation) continue;
            if (relation.SourceCollectionId == collectionId &&
                (relation.Id == navigationId || relation.SourceFieldId == navigationId)) return (relation, false);
            if (relation.TargetCollectionId == collectionId && relation.InverseNavigationId == navigationId) return (relation, true);
        }
        throw new InvalidOperationException();
    }

    private async ValueTask<Dictionary<string, string[]>> RelatedIdsBatchAsync(RecordEnvelope[] parents, RelationDefinition relation, bool inverse, IncludeState state)
    {
        var result = parents.ToDictionary(static parent => parent.Id.Value, static _ => new List<(int Ordinal, string Id)>(), StringComparer.Ordinal);
        SqlitePhysicalModel.RelationModel? many = _physical.Relations.SingleOrDefault(item => item.Definition.Id == relation.Id);
        if (!inverse && many is null)
        {
            SqlitePhysicalModel.FieldModel field = _physical.Collection(relation.SourceCollectionId).Fields.Single(item => item.Definition.Id == relation.SourceFieldId);
            foreach (RecordEnvelope parent in parents)
                if (PayloadId(parent.Payload, field.Definition.Name) is { } id) result[parent.Id.Value].Add((0, id));
            return result.ToDictionary(static pair => pair.Key, static pair => pair.Value.Select(static item => item.Id).ToArray(), StringComparer.Ordinal);
        }
        if (parents.Length == 0) return [];
        await using SqliteCommand command = state.Connection.CreateCommand();
        command.Transaction = state.Transaction;
        command.CommandTimeout = TimeoutSeconds();
        if (many is not null)
        {
            command.CommandText = !inverse
                ? $"SELECT source_record_id, target_record_id, ordinal FROM {many.Table} WHERE source_record_id IN ({BindParentIds(command, parents)}) ORDER BY source_record_id, ordinal;"
                : $"SELECT target_record_id, source_record_id, 0 FROM {many.Table} WHERE target_record_id IN ({BindParentIds(command, parents)}) ORDER BY target_record_id, source_record_id;";
        }
        else
        {
            SqlitePhysicalModel.CollectionModel source = _physical.Collection(relation.SourceCollectionId);
            SqlitePhysicalModel.FieldModel field = source.Fields.Single(item => item.Definition.Id == relation.SourceFieldId);
            command.CommandText = $"SELECT {field.Column}, record_id, 0 FROM {source.Table} WHERE {field.Column} IN ({BindParentIds(command, parents)}) ORDER BY {field.Column}, record_id;";
        }
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(state.CancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(state.CancellationToken).ConfigureAwait(false))
            if (result.TryGetValue(reader.GetString(0), out List<(int Ordinal, string Id)>? values)) values.Add((reader.GetInt32(2), reader.GetString(1)));
        return result.ToDictionary(static pair => pair.Key, static pair => pair.Value.OrderBy(static item => item.Ordinal).Select(static item => item.Id).ToArray(), StringComparer.Ordinal);
    }

    private static string BindParentIds(SqliteCommand command, RecordEnvelope[] parents)
    {
        var names = new string[parents.Length];
        for (int index = 0; index < parents.Length; index++)
        { names[index] = "$parent" + index; command.Parameters.AddWithValue(names[index], parents[index].Id.Value); }
        return string.Join(",", names);
    }

    private async ValueTask<RecordPage> ReadIncludePageAsync(CollectionDefinition collection, RecordQuery canonical, IncludeState state)
    {
        RecordQuery stored = LowerIncludeQuery(collection, canonical);
        SqliteQueryPlan plan = new SqliteQueryPlanner(_options, _physical.Collection(collection.Id)).Plan(stored);
        if (!plan.Supported) throw new InvalidOperationException();
        long? total = null;
        if (stored.Count != QueryCountMode.None)
        {
            await using SqliteCommand count = state.Connection.CreateCommand(); count.Transaction = state.Transaction; count.CommandTimeout = TimeoutSeconds(); count.CommandText = plan.CountSql; plan.Bind(count);
            total = Convert.ToInt64(await count.ExecuteScalarAsync(state.CancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }
        await using SqliteCommand command = state.Connection.CreateCommand(); command.Transaction = state.Transaction; command.CommandTimeout = TimeoutSeconds(); command.CommandText = plan.SelectSql; plan.Bind(command);
        var rows = new List<RecordEnvelope>(); await using SqliteDataReader reader = await command.ExecuteReaderAsync(state.CancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(state.CancellationToken).ConfigureAwait(false)) rows.Add(_physical.Collection(collection.Id).ReadEnvelope(reader, _options.StoreId));
        int requested = plan.PageInfo.PerPage ?? plan.PageInfo.Limit ?? _options.DefaultPageSize; bool more = rows.Count > requested; if (more) rows.RemoveAt(rows.Count - 1);
        state.Dependencies.Add(collection.Id);
        return new RecordPage { Items = rows.ToArray(), Page = plan.PageInfo with { HasMore = more }, Count = stored.Count == QueryCountMode.None ? null : new CountInfo { Mode = stored.Count, Total = total, IsExact = true } };
    }

    private RecordQuery TargetQuery(string[] ids, RecordInclude include, RecordIncludeSourcePolicy policy, int maximum)
    {
        FilterExpression membership = new() { Kind = FilterNodeKind.In, Field = "id", Values = ids.Select(static id => new QueryValue { Kind = QueryValueKind.Id, Id = id }).ToArray() };
        FilterExpression? filter = Combine(membership, policy.Filter, include.Filter);
        int limit = Math.Min(maximum, _options.MaxPageSize);
        return new RecordQuery { Filter = filter, Sort = include.Sort, Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = limit }, Count = QueryCountMode.None };
    }

    private static FilterExpression? Combine(params FilterExpression?[] filters)
    { FilterExpression[] present = filters.Where(static item => item is not null).Cast<FilterExpression>().ToArray(); return present.Length switch { 0 => null, 1 => present[0], _ => new FilterExpression { Kind = FilterNodeKind.And, Children = present } }; }
    private RecordQuery LowerIncludeQuery(CollectionDefinition collection, RecordQuery query) => query with
    {
        Filter = LowerIncludeFilter(collection, query.Filter),
        Sort = query.Sort?.Select(sort => sort with { Field = LowerIncludeField(collection, sort.Field) }).ToArray(),
        Select = query.Select?.Select(field => LowerIncludeField(collection, field)).ToArray(),
        Include = null,
    };
    private FilterExpression? LowerIncludeFilter(CollectionDefinition collection, FilterExpression? filter) => filter is null ? null : filter with
    {
        Field = filter.Field is null ? null : LowerIncludeField(collection, filter.Field),
        Children = filter.Children?.Select(child => LowerIncludeFilter(collection, child)!).ToArray(),
    };
    private static string LowerIncludeField(CollectionDefinition collection, string id) => id is "id" or "createdAt" or "updatedAt" or "revision" ? id : (collection.Fields ?? []).Single(field => field.Id == id || field.Name == id).Name;
    private static string? PayloadId(RecordPayload payload, string name) => payload.Fields is { } fields && fields.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static RecordEnvelope SelectIncludeFields(RecordEnvelope record, CollectionDefinition collection, RecordInclude include, FieldMask? mask)
    {
        IEnumerable<FieldDefinition> allowed = collection.Fields ?? [];
        allowed = mask?.Mode switch { FieldMaskMode.DenyAll => [], FieldMaskMode.IncludeOnly => allowed.Where(field => (mask.Include ?? []).Contains(field.Id)), FieldMaskMode.Exclude => allowed.Where(field => !(mask.Exclude ?? []).Contains(field.Id)), _ => allowed };
        if (include.SelectFieldIds is { } requested) allowed = allowed.Where(field => requested.Contains(field.Id));
        HashSet<string> names = allowed.Select(static field => field.Name).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, JsonElement> fields = (record.Payload.Fields ?? []).Where(pair => names.Contains(pair.Key)).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        return record with { Payload = record.Payload with { Fields = fields } };
    }
    private static OperationResult<RecordIncludeExecutionResult> IncludeFailure(string code, string message)
    {
        var error = new BaseError
        {
            Code = code,
            Message = message,
            Category = code == "base.include.snapshotUnsupported" ? ErrorCategory.Capability : ErrorCategory.Store,
        };
        return code == "base.include.snapshotUnsupported"
            ? OperationResults.CapabilityUnavailable<RecordIncludeExecutionResult>(error)
            : OperationResults.StoreError<RecordIncludeExecutionResult>(error);
    }

    private sealed class IncludeState(RecordIncludeExecutionRequest request, SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        private int _operations; private int _records; private int _bytes;
        internal RecordIncludeExecutionRequest Request => request; internal SqliteConnection Connection => connection; internal SqliteTransaction Transaction => transaction; internal CancellationToken CancellationToken => cancellationToken;
        internal HashSet<string> Dependencies { get; } = new(StringComparer.Ordinal);
        internal RecordIncludeSourcePolicy Policy(string collectionId) => request.SourcePolicies.Single(policy => policy.CollectionId == collectionId);
        internal void Operation() { if (++_operations > 1_024) throw new IncludeLimitException(); }
        internal void Record(RecordEnvelope record) { _records++; _bytes += (record.Payload.Fields ?? []).Sum(pair => pair.Key.Length * 2 + pair.Value.GetRawText().Length * 2); if (_records > request.MaxResultRows || _bytes > request.MaxResultBytes) throw new IncludeLimitException(); }
    }
    private sealed class IncludeLimitException : Exception;
}
