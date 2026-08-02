using System.Text.Json;

namespace HPD.Base;

internal sealed partial class VolatileRecordStore
{
    /// <summary>Gets the includes.</summary>
    public RecordIncludeExecutionCapability Includes { get; }

    /// <summary>Executes the execute include async operation.</summary>
    public ValueTask<OperationResult<RecordIncludeExecutionResult>> ExecuteIncludeAsync(RecordIncludeExecutionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        try
        {
            VolatileStoreState snapshot = Volatile.Read(ref _publishedState);
            var state = new VolatileIncludeState(request, snapshot, _options.Collections ?? [], Includes.MaxIncludes, cancellationToken);
            RecordPage roots = IncludePage(request.RootCollection, request.RootQuery with { Include = null }, state, null);
            RecordEnvelope[] items = ExpandIncludeBatch(roots.Items, request.RootCollection, request.IncludePlan, state, 1);
            return ValueTask.FromResult(OperationResults.Ok(new RecordIncludeExecutionResult
            {
                Page = roots with { Items = items }, SchemaGeneration = 0,
                DependencyEvidence = state.Dependencies.Select(static id => new BaseReadDependencyEvidence { CollectionId = id }).ToArray(),
            }));
        }
        catch (OperationCanceledException) { throw; }
        catch (VolatileIncludeLimitException)
        { return ValueTask.FromResult(IncludeError("base.include.limitExceeded", "Volatile include limits were exceeded.")); }
        catch
        { return ValueTask.FromResult(IncludeError("base.include.invalid", "Volatile include execution failed.")); }
    }

    private RecordEnvelope[] ExpandIncludeBatch(RecordEnvelope[] parents, CollectionDefinition parentCollection, RecordInclude[] includes, VolatileIncludeState state, int depth)
    {
        if (depth > Includes.MaxDepth) throw new VolatileIncludeLimitException();
        state.Include(includes.Length);
        var results = parents.Select(_ => new RecordIncludeResult[includes.Length]).ToArray();
        for (int index = 0; index < includes.Length; index++)
        {
            RecordInclude include = includes[index];
            (RelationDefinition relation, bool inverse) = ResolveVolatileInclude(parentCollection.Id, include.NavigationId);
            CollectionDefinition target = state.Collection(inverse ? relation.SourceCollectionId : relation.TargetCollectionId);
            Dictionary<string, string[]> relatedIds = VolatileRelatedIdsBatch(parents, relation, inverse, state);
            string[] uniqueIds = relatedIds.Values.SelectMany(static ids => ids).Distinct(StringComparer.Ordinal).ToArray();
            if (uniqueIds.Length > state.Request.MaxResultRows) throw new VolatileIncludeLimitException();
            RecordIncludeSourcePolicy policy = state.Policy(target.Id);
            FilterExpression? filter = CombineVolatile(policy.Filter, include.Filter);
            RecordQuery targetQuery = new()
            {
                Filter = LowerVolatileFilter(target, filter),
                Sort = include.Sort?.Select(sort => sort with { Field = LowerVolatileField(target, sort.Field) }).ToArray(),
                Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = state.Request.MaxResultRows },
                Count = QueryCountMode.None,
            };
            RecordEnvelope[] records = uniqueIds.Length == 0
                ? []
                : IncludePage(target, targetQuery, state, uniqueIds.ToHashSet(StringComparer.Ordinal)).Items;
            RecordEnvelope[] expanded = include.Includes is { Length: > 0 }
                ? ExpandIncludeBatch(records, target, include.Includes, state, depth + 1)
                : records;
            RecordEnvelope[] selected = expanded.Select(record => SelectVolatileInclude(record, target, include, policy.ReadMask)).ToArray();
            Dictionary<string, RecordEnvelope> byId = selected.ToDictionary(static record => record.Id.Value, StringComparer.Ordinal);
            Dictionary<string, int>? sortOrder = include.Sort is { Length: > 0 }
                ? selected.Select((record, position) => (record.Id.Value, position)).ToDictionary(static item => item.Value, static item => item.position, StringComparer.Ordinal)
                : null;
            bool many = inverse ? relation.InverseMultiplicity == BaseRelationMultiplicity.Many : relation.LocalMultiplicity == BaseRelationMultiplicity.Many;
            for (int parentIndex = 0; parentIndex < parents.Length; parentIndex++)
            {
                string[] ids = relatedIds.GetValueOrDefault(parents[parentIndex].Id.Value) ?? [];
                IEnumerable<RecordEnvelope> matches = ids.Distinct(StringComparer.Ordinal).Where(byId.ContainsKey).Select(id => byId[id]);
                if (sortOrder is not null) matches = matches.OrderBy(record => sortOrder[record.Id.Value]);
                RecordEnvelope[] limited = matches.Take(include.Limit ?? state.Request.MaxResultRows).ToArray();
                foreach (RecordEnvelope record in limited) state.Record(record);
                results[parentIndex][index] = many
                    ? new RecordIncludeResult { NavigationId = include.NavigationId, Kind = RecordIncludeKind.Many, Records = limited }
                    : limited.FirstOrDefault() is { } one
                        ? new RecordIncludeResult { NavigationId = include.NavigationId, Kind = RecordIncludeKind.One, Record = one }
                        : new RecordIncludeResult { NavigationId = include.NavigationId, Kind = RecordIncludeKind.None };
            }
        }
        return parents.Select((parent, index) => parent with { Includes = results[index] }).ToArray();
    }

    private RecordPage IncludePage(CollectionDefinition collection, RecordQuery query, VolatileIncludeState state, HashSet<string>? restrictedIds)
    {
        StoredRecord[] records = GetCollectionOrNull(state.Snapshot, collection.Id)?.RecordsById.Values.OrderBy(static record => record.Sequence).ThenBy(static record => record.Id.Value, StringComparer.Ordinal).ToArray() ?? [];
        var filtered = records.Where(record => (restrictedIds is null || restrictedIds.Contains(record.Id.Value)) && (query.Filter is null || MatchesFilter(record, query.Filter))).ToList();
        var sorted = ApplySort<RecordPage>(filtered, query.Sort); if (sorted.Result is not null) throw new InvalidOperationException();
        var page = ApplyPage<RecordPage>(sorted.Value!, query, out PageInfo pageInfo); if (page.Result is not null) throw new InvalidOperationException();
        state.Dependencies.Add(collection.Id);
        return new RecordPage { Items = ApplySelect(page.Value!, query.Select).Select(RecordCloneHelpers.CloneEnvelope).ToArray(), Page = pageInfo, Count = query.Count == QueryCountMode.None ? null : new CountInfo { Mode = query.Count, Total = filtered.Count, IsExact = true } };
    }

    private (RelationDefinition, bool) ResolveVolatileInclude(string collectionId, string navigationId)
    {
        foreach (CollectionDefinition collection in _options.Collections ?? []) foreach (FieldDefinition field in collection.Fields ?? []) if (field.Relation is { } relation)
        { if (relation.SourceCollectionId == collectionId && (relation.Id == navigationId || relation.SourceFieldId == navigationId)) return (relation, false); if (relation.TargetCollectionId == collectionId && relation.InverseNavigationId == navigationId) return (relation, true); }
        throw new InvalidOperationException();
    }
    private static Dictionary<string, string[]> VolatileRelatedIdsBatch(RecordEnvelope[] parents, RelationDefinition relation, bool inverse, VolatileIncludeState state)
    {
        CollectionDefinition source = state.Collection(relation.SourceCollectionId); FieldDefinition field = (source.Fields ?? []).Single(item => item.Id == relation.SourceFieldId);
        if (!inverse)
            return parents.ToDictionary(static parent => parent.Id.Value, parent => PayloadIds(parent.Payload, field.Name), StringComparer.Ordinal);
        var parentIds = parents.Select(static parent => parent.Id.Value).ToHashSet(StringComparer.Ordinal);
        var values = parents.ToDictionary(static parent => parent.Id.Value, static _ => new List<string>(), StringComparer.Ordinal);
        IEnumerable<StoredRecord> records = GetCollectionOrNull(state.Snapshot, source.Id)?.RecordsById.Values ?? Enumerable.Empty<StoredRecord>();
        foreach (StoredRecord record in records.OrderBy(static record => record.Sequence).ThenBy(static record => record.Id.Value, StringComparer.Ordinal))
            foreach (string targetId in PayloadIds(record.Payload, field.Name))
                if (parentIds.Contains(targetId)) values[targetId].Add(record.Id.Value);
        return values.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }
    private static string[] PayloadIds(RecordPayload payload, string name)
    { if (payload.Fields is not { } fields || !fields.TryGetValue(name, out JsonElement value)) return []; if (value.ValueKind == JsonValueKind.String) return [value.GetString()!]; if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.String).Select(static item => item.GetString()!).ToArray(); return []; }
    private static FilterExpression? CombineVolatile(params FilterExpression?[] values) { FilterExpression[] present = values.Where(static value => value is not null).Cast<FilterExpression>().ToArray(); return present.Length switch { 0 => null, 1 => present[0], _ => new FilterExpression { Kind = FilterNodeKind.And, Children = present } }; }
    private static FilterExpression? LowerVolatileFilter(CollectionDefinition collection, FilterExpression? filter) => filter is null ? null : filter with { Field = filter.Field is null ? null : LowerVolatileField(collection, filter.Field), Children = filter.Children?.Select(child => LowerVolatileFilter(collection, child)!).ToArray() };
    private static string LowerVolatileField(CollectionDefinition collection, string id) => id is "id" or "createdAt" or "updatedAt" or "revision" ? id : (collection.Fields ?? []).Single(field => field.Id == id || field.Name == id).Name;
    private static RecordEnvelope SelectVolatileInclude(RecordEnvelope record, CollectionDefinition collection, RecordInclude include, FieldMask? mask)
    { IEnumerable<FieldDefinition> allowed = collection.Fields ?? []; allowed = mask?.Mode switch { FieldMaskMode.DenyAll => [], FieldMaskMode.IncludeOnly => allowed.Where(field => (mask.Include ?? []).Contains(field.Id)), FieldMaskMode.Exclude => allowed.Where(field => !(mask.Exclude ?? []).Contains(field.Id)), _ => allowed }; if (include.SelectFieldIds is { } selected) allowed = allowed.Where(field => selected.Contains(field.Id)); HashSet<string> names = allowed.Select(static field => field.Name).ToHashSet(StringComparer.Ordinal); return record with { Payload = record.Payload with { Fields = (record.Payload.Fields ?? []).Where(pair => names.Contains(pair.Key)).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal) } }; }
    private static OperationResult<RecordIncludeExecutionResult> IncludeError(string code, string message) => OperationResults.StoreError<RecordIncludeExecutionResult>(new BaseError { Code = code, Message = message, Category = ErrorCategory.Store });
    private sealed class VolatileIncludeState(RecordIncludeExecutionRequest request, VolatileStoreState snapshot, CollectionDefinition[] collections, int maxIncludes, CancellationToken cancellationToken)
    { private int _records; private int _bytes; private int _includes; internal RecordIncludeExecutionRequest Request => request; internal VolatileStoreState Snapshot => snapshot; internal HashSet<string> Dependencies { get; } = new(StringComparer.Ordinal); internal CollectionDefinition Collection(string id) => collections.Single(collection => collection.Id == id); internal RecordIncludeSourcePolicy Policy(string id) => request.SourcePolicies.Single(policy => policy.CollectionId == id); internal void Include(int count) { cancellationToken.ThrowIfCancellationRequested(); _includes += count; if (_includes > maxIncludes) throw new VolatileIncludeLimitException(); } internal void Record(RecordEnvelope record) { cancellationToken.ThrowIfCancellationRequested(); _records++; _bytes += (record.Payload.Fields ?? []).Sum(pair => pair.Key.Length * 2 + pair.Value.GetRawText().Length * 2); if (_records > request.MaxResultRows || _bytes > request.MaxResultBytes) throw new VolatileIncludeLimitException(); } }
    private sealed class VolatileIncludeLimitException : Exception;
}
