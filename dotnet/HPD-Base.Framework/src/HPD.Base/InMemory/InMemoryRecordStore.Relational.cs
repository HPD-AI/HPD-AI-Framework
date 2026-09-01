using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal sealed partial class InMemoryRecordStore
{
    /// <summary>Gets the relational reads.</summary>
    public RelationalReadCapability RelationalReads { get; } = CreateRelationalCapability();

    internal static RelationalReadCapability CreateRelationalCapability() => new()
    {
        Supported = true,
        JoinKinds = [BaseJoinKind.Inner, BaseJoinKind.Left, BaseJoinKind.Semi, BaseJoinKind.Anti],
        AggregateKinds = Enum.GetValues<BaseAggregateKind>(),
        ComparisonOperators =
        [
            FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.LessThan,
            FilterOperator.LessThanOrEqual, FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual,
            FilterOperator.Contains, FilterOperator.StartsWith, FilterOperator.EndsWith,
        ],
        ValueKinds = Enum.GetValues<QueryValueKind>(),
        CanonicalJsonValues = true,
        IndependentAggregateBranches = true,
        SingleSnapshotCompoundReads = true,
        MaxCompoundBranches = 32,
        MaxCompoundOperations = 256,
        MaxSources = 32,
        MaxJoins = 8,
        MaxPredicateNodes = 256,
        MaxGroupKeys = 8,
        MaxAggregates = 32,
        MaxProjectionFields = 64,
        MaxSortFields = 8,
        MaxResultRows = 1_000,
        MaxResultBytes = 16_777_216,
        SnapshotConsistency = true,
        CompleteDependencyEvidence = true,
    };

    /// <summary>Executes the execute read async operation.</summary>
    public ValueTask<OperationResult<BaseRelationalReadExecutionResult>> ExecuteReadAsync(
        BaseRelationalReadExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (_lifecycleMaintenance is not null)
            return ValueTask.FromResult(LifecycleMaintenanceRequired<BaseRelationalReadExecutionResult>());
        try
        {
            return ValueTask.FromResult(OperationResults.Ok(ExecuteRead(request, cancellationToken)));
        }
        catch (OperationCanceledException) { throw; }
        catch (InMemoryRelationalLimitException)
        {
            return ValueTask.FromResult(OperationResults.StoreError<BaseRelationalReadExecutionResult>(new BaseError
            {
                Code = "base.relational.read.limitExceeded",
                Message = "The InMemory relational result limits were exceeded.",
                Category = ErrorCategory.Store,
            }));
        }
        catch
        {
            return ValueTask.FromResult(OperationResults.StoreError<BaseRelationalReadExecutionResult>(new BaseError
            {
                Code = "base.inmemory.relational.executionFailed",
                Message = "The InMemory relational read failed.",
                Category = ErrorCategory.Store,
            }));
        }
    }

    private BaseRelationalReadExecutionResult ExecuteRead(
        BaseRelationalReadExecutionRequest request,
        CancellationToken cancellationToken)
    {
        InMemoryStoreState snapshot = Volatile.Read(ref _publishedState);
        var parameters = request.ParameterValues.ToDictionary(static value => value.ParameterId, static value => value.Value, StringComparer.Ordinal);
        var policies = request.SourcePolicies.ToDictionary(static value => value.SourceId, StringComparer.Ordinal);
        var collections = (_options.Collections ?? []).ToDictionary(static value => value.Id, StringComparer.Ordinal);
        Dictionary<string, QueryValueKind> fieldKinds = request.Plan.Sources.SelectMany(source =>
                (collections[source.CollectionId].Fields ?? []).Select(field => new
                {
                    Key = source.Id + "\0" + field.WireName,
                    Kind = InMemoryFieldKind(field),
                }))
            .ToDictionary(static item => item.Key, static item => item.Kind, StringComparer.Ordinal);
        BaseRelationalReadPlan plan = LowerPlan(request.Plan, collections);
        if (plan.Topology == BaseRelationalReadTopology.CompoundCount)
            return ExecuteCompoundRead(request, plan, snapshot, parameters, policies, collections, fieldKinds, cancellationToken);
        var rows = new List<RelationalContext> { new(fieldKinds, snapshot, _options.ExportedSubjects) };

        BaseRelationalReadSource root = plan.Sources[0];
        rows = SourceRecords(snapshot, root, policies[root.Id], collections[root.CollectionId], cancellationToken)
            .Select(record => new RelationalContext(fieldKinds, snapshot, _options.ExportedSubjects) { Records = { [root.Id] = record } }).ToList();
        for (int index = 0; index < plan.Joins.Length; index++)
        {
            BaseRelationalReadJoin join = plan.Joins[index];
            BaseRelationalReadSource source = plan.Sources[index + 1];
            StoredRecord[] candidates = SourceRecords(snapshot, source, policies[source.Id], collections[source.CollectionId], cancellationToken);
            var next = new List<RelationalContext>();
            foreach (RelationalContext left in rows)
            {
                StoredRecord[] matches = candidates.Where(candidate =>
                {
                    var candidateContext = left.Clone(); candidateContext.Records[source.Id] = candidate;
                    return Present(join.Left, candidateContext) && Present(join.Right, candidateContext) &&
                        ValuesEqual(Value(join.Left, candidateContext, parameters), Value(join.Right, candidateContext, parameters));
                }).ToArray();
                if (join.Kind == BaseJoinKind.Semi) { if (matches.Length != 0) next.Add(left); continue; }
                if (join.Kind == BaseJoinKind.Anti) { if (matches.Length == 0) next.Add(left); continue; }
                if (matches.Length == 0 && join.Kind == BaseJoinKind.Left)
                { var empty = left.Clone(); empty.Records[source.Id] = null; next.Add(empty); continue; }
                foreach (StoredRecord match in matches) { var combined = left.Clone(); combined.Records[source.Id] = match; next.Add(combined); }
            }
            rows = next;
        }

        if (plan.Predicate is not null)
            rows = rows.Where(row => Predicate(plan.Predicate, row, parameters, null)).ToList();

        List<RelationalOutput> output;
        if (plan.GroupKeys.Length != 0 || plan.Aggregates.Length != 0)
        {
            if (plan.GroupKeys.Length == 0 && rows.Count == 0)
                output = [BuildOutputAuthority(plan, new RelationalContext(fieldKinds, snapshot, _options.ExportedSubjects), [], parameters)];
            else
                output = rows.GroupBy(row => Key(plan.GroupKeys.Select(operand => Value(operand, row, parameters))))
                    .Select(group => BuildOutputAuthority(plan, group.First(), group.ToArray(), parameters)).ToList();
        }
        else output = rows.Select(row => BuildOutputAuthority(plan, row, [row], parameters)).ToList();

        if (plan.Having is not null)
            output = output.Where(item => Predicate(plan.Having, item.Context, parameters, item.Aggregates)).ToList();
        if (plan.Distinct)
            output = DistinctOutputs(plan, output, parameters);
        output.Sort((left, right) => plan.Sort.Length == 0
            ? CompareProjected(plan, left, right, parameters)
            : CompareSort(plan, left, right, parameters));

        int total = output.Count;
        int perPage = plan.Window?.Kind == BaseRegisteredReadWindowKind.Offset
            ? plan.Window.Limit!.Value
            : plan.Window?.PerPage ?? request.MaxResultRows;
        int offset = plan.Window?.Kind == BaseRegisteredReadWindowKind.Offset
            ? plan.Window.Offset!.Value
            : checked(((plan.Window?.Page ?? 1) - 1) * perPage);
        if (!TryMaterializeBoundedPage(
                output.Skip(offset).Take(perPage),
                request.MaxResultRows,
                request.MaxResultBytes,
                item => ProjectRow(plan, item, parameters),
                EstimateBytes,
                out BaseRelationalRow[] resultRows))
            throw new InMemoryRelationalLimitException();
        return new BaseRelationalReadExecutionResult
        {
            Result = new BaseRelationalReadResult
            {
                Rows = resultRows,
                Page = plan.Window?.Kind == BaseRegisteredReadWindowKind.Offset
                    ? new PageInfo { Offset = offset, Limit = perPage, HasMore = total > offset && total - offset > resultRows.Length }
                    : new PageInfo { Page = plan.Window?.Page ?? 1, PerPage = perPage, HasMore = total > offset && total - offset > resultRows.Length },
                Count = total,
            },
            DependencyEvidence = DependencyEvidence(plan, snapshot),
            SnapshotAuthority = SnapshotAuthority(request, snapshot),
        };
    }

    private BaseRelationalReadExecutionResult ExecuteCompoundRead(
        BaseRelationalReadExecutionRequest request, BaseRelationalReadPlan plan, InMemoryStoreState snapshot,
        IReadOnlyDictionary<string, QueryValue> parameters, IReadOnlyDictionary<string, BaseRelationalReadSourcePolicy> policies,
        IReadOnlyDictionary<string, CollectionDefinition> collections, IReadOnlyDictionary<string, QueryValueKind> fieldKinds,
        CancellationToken cancellationToken)
    {
        var rows = new List<BaseRelationalRow>(plan.CompoundCountBranches.Length); long bytes = 0;
        foreach (BaseRelationalCompoundCountBranch branch in plan.CompoundCountBranches)
        {
            long count = 0;
            foreach (StoredRecord record in SourceRecords(snapshot, branch.Source, policies[branch.Source.Id], collections[branch.Source.CollectionId], cancellationToken))
            {
                var context = new RelationalContext(fieldKinds, snapshot, _options.ExportedSubjects);
                context.Records[branch.Source.Id] = record;
                if (branch.Predicate is null || Predicate(branch.Predicate, context, parameters, null)) count++;
            }
            var row = new BaseRelationalRow
            {
                Fields =
                [
                    new() { FieldId = branch.DiscriminatorOutputFieldId, Value = new QueryValue { Kind = QueryValueKind.String, String = branch.Discriminator } },
                    new() { FieldId = branch.CountOutputFieldId, Value = new QueryValue { Kind = QueryValueKind.Integer, Integer = count } },
                ],
            };
            if (!TryAccumulateRelationalResultBytes(bytes, EstimateBytes(row), out bytes) || bytes > request.MaxResultBytes)
                throw new InMemoryRelationalLimitException();
            rows.Add(row);
        }
        BaseReadDependencyEvidence[] dependencies = plan.Sources.Select(static source => source.CollectionId).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).Select(static id => new BaseReadDependencyEvidence { CollectionId = id }).ToArray();
        BaseRelationalCompoundBranchEvidence[] evidence = plan.CompoundCountBranches.Select((branch, ordinal) => new BaseRelationalCompoundBranchEvidence
        {
            BranchId = branch.Id, BranchChecksum = branch.BranchChecksum, RowOrdinal = ordinal, SchemaGeneration = plan.SchemaGeneration,
        }).ToArray();
        if (!BaseRelationalReadEvidenceAccounting.TryMeasure(dependencies, evidence, out long evidenceBytes)
            || !TryAccumulateRelationalResultBytes(bytes, evidenceBytes, out bytes) || bytes > request.MaxResultBytes)
            throw new InMemoryRelationalLimitException();
        return new BaseRelationalReadExecutionResult
        {
            Result = new BaseRelationalReadResult
            {
                Rows = rows.ToArray(), Page = new PageInfo { Page = 1, PerPage = rows.Count, Limit = rows.Count, HasMore = false }, Count = rows.Count,
            },
            DependencyEvidence = dependencies,
            CompoundBranches = evidence,
            SnapshotAuthority = SnapshotAuthority(request, snapshot),
        };
    }

    private BaseRelationalReadSnapshotAuthority SnapshotAuthority(
        BaseRelationalReadExecutionRequest request,
        InMemoryStoreState snapshot)
    {
        BaseSchemaAuthorityChecksum schemaChecksum = BaseSchemaAuthorityChecksum.Create(
            request.LogicalSchemaChecksum.ToArray());
        BaseRelationalCollectionSnapshotAuthority[] collections = request.Plan.Sources
            .Select(static source => source.CollectionId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(id => new BaseRelationalCollectionSnapshotAuthority
            {
                CollectionId = id,
                CollectionGeneration = snapshot.Collections.GetValueOrDefault(id)?.PurgeGeneration ?? 0,
            }).ToArray();
        return BaseRelationalReadSnapshotAuthorityContract.Create(
            request.ApplicationId,
            _options.StoreId,
            _options.StoreId,
            restoreEpoch: 0,
            schemaGeneration: request.Plan.SchemaGeneration,
            schemaChecksum,
            collections);
    }

    internal static bool TryAccumulateRelationalResultBytes(long current, long additional, out long total)
    {
        try
        {
            total = checked(current + additional);
            return current >= 0 && additional >= 0;
        }
        catch (OverflowException)
        {
            total = 0;
            return false;
        }
    }

    internal static bool TryMaterializeBoundedPage<TSource, TResult>(
        IEnumerable<TSource> source,
        int maximumRows,
        long maximumBytes,
        Func<TSource, TResult> project,
        Func<TResult, long> estimateBytes,
        out TResult[] result)
    {
        var admitted = new List<TResult>(maximumRows);
        long bytes = 0;
        foreach (TSource item in source)
        {
            if (admitted.Count >= maximumRows)
            {
                result = [];
                return false;
            }
            TResult current = project(item);
            long currentBytes = estimateBytes(current);
            if (!TryAccumulateRelationalResultBytes(bytes, currentBytes, out bytes) || bytes > maximumBytes)
            {
                result = [];
                return false;
            }
            admitted.Add(current);
        }
        result = admitted.ToArray();
        return true;
    }

    private sealed class InMemoryRelationalLimitException : Exception;

    private static BaseReadDependencyEvidence[] DependencyEvidence(BaseRelationalReadPlan plan, InMemoryStoreState snapshot)
    {
        BaseRelationalOperand? subject = plan.Projection.Select(static value => value.Operand)
            .SingleOrDefault(static value => value.Kind == BaseRelationalOperandKind.SubjectReference);
        return plan.Sources.Select(source =>
        {
            if (subject is null || !string.Equals(subject.SourceId, source.Id, StringComparison.Ordinal))
                return new BaseReadDependencyEvidence { CollectionId = source.CollectionId };
            if (!snapshot.SubjectContracts.TryGetValue(SubjectContractKey(subject.SubjectContractId!, subject.SubjectContractVersion!.Value), out InMemorySubjectContractState? state))
                throw new InvalidOperationException();
            return new BaseReadDependencyEvidence
            {
                CollectionId = source.CollectionId,
                SubjectContractId = state.ContractId,
                SubjectContractVersion = state.ContractVersion,
                SubjectStateGeneration = state.StateGeneration,
            };
        }).ToArray();
    }

    private static StoredRecord[] SourceRecords(
        InMemoryStoreState state, BaseRelationalReadSource source, BaseRelationalReadSourcePolicy policy,
        CollectionDefinition collection, CancellationToken cancellationToken)
    {
        FilterExpression? filter = policy.Filter is null ? null : LowerFilter(policy.Filter, collection);
        IEnumerable<StoredRecord> records = GetCollectionOrNull(state, source.CollectionId)?.RecordsById.Values
            ?? Enumerable.Empty<StoredRecord>();
        return records
            .Where(record => { cancellationToken.ThrowIfCancellationRequested(); return filter is null || MatchesFilter(record, filter); })
            .OrderBy(static record => record.AppendPosition).ThenBy(static record => record.Id.Value, StringComparer.Ordinal).ToArray();
    }

    private static FilterExpression LowerFilter(FilterExpression filter, CollectionDefinition collection)
    {
        string? field = filter.Field;
        if (field is not null)
            field = (collection.Fields ?? []).Single(definition => string.Equals(definition.Id, field, StringComparison.Ordinal)).WireName;
        return filter with { Field = field, Children = filter.Children?.Select(child => LowerFilter(child, collection)).ToArray() };
    }

    private static BaseRelationalReadPlan LowerPlan(
        BaseRelationalReadPlan plan,
        IReadOnlyDictionary<string, CollectionDefinition> collections)
    {
        var sourceCollections = plan.Sources.ToDictionary(static source => source.Id, source => collections[source.CollectionId], StringComparer.Ordinal);
        BaseRelationalOperand Lower(BaseRelationalOperand operand)
        {
            if (operand.Kind is not (BaseRelationalOperandKind.SourceField or BaseRelationalOperandKind.StoredSubjectReference)) return operand;
            string storedName = (sourceCollections[operand.SourceId!].Fields ?? [])
                .Single(field => string.Equals(field.Id, operand.FieldId, StringComparison.Ordinal)).WireName;
            return operand with { FieldId = storedName };
        }
        BaseRelationalPredicate? Predicate(BaseRelationalPredicate? predicate) => predicate is null ? null : predicate with
        {
            Left = predicate.Left is null ? null : Lower(predicate.Left),
            Right = predicate.Right is null ? null : Lower(predicate.Right),
            Children = predicate.Children?.Select(child => Predicate(child)!).ToArray(),
        };
        return plan with
        {
            Joins = plan.Joins.Select(join => join with { Left = Lower(join.Left), Right = Lower(join.Right) }).ToArray(),
            Predicate = Predicate(plan.Predicate),
            GroupKeys = plan.GroupKeys.Select(Lower).ToArray(),
            Aggregates = plan.Aggregates.Select(aggregate => aggregate with { Operand = aggregate.Operand is null ? null : Lower(aggregate.Operand) }).ToArray(),
            Having = Predicate(plan.Having),
            Projection = plan.Projection.Select(projection => projection with { Operand = Lower(projection.Operand) }).ToArray(),
            Sort = plan.Sort.Select(sort => sort with { Operand = Lower(sort.Operand) }).ToArray(),
            CompoundCountBranches = plan.CompoundCountBranches.Select(branch => branch with { Predicate = Predicate(branch.Predicate) }).ToArray(),
        };
    }

    private static RelationalOutput BuildOutputAuthority(
        BaseRelationalReadPlan plan, RelationalContext context, RelationalContext[] group,
        IReadOnlyDictionary<string, QueryValue> parameters)
    {
        var aggregates = new Dictionary<string, QueryValue>(StringComparer.Ordinal);
        foreach (BaseRelationalReadAggregate aggregate in plan.Aggregates)
            aggregates[aggregate.Id] = Aggregate(aggregate, context, group, parameters);
        return new RelationalOutput(context, aggregates);
    }

    private static BaseRelationalRow ProjectRow(
        BaseRelationalReadPlan plan,
        RelationalOutput output,
        IReadOnlyDictionary<string, QueryValue> parameters) => new()
        {
            Fields = plan.Projection.Select(projection => new BaseRelationalFieldValue
            {
                FieldId = projection.FieldId,
                Value = Value(projection.Operand, output.Context, parameters, output.Aggregates),
            }).ToArray(),
        };

    private static List<RelationalOutput> DistinctOutputs(
        BaseRelationalReadPlan plan,
        List<RelationalOutput> candidates,
        IReadOnlyDictionary<string, QueryValue> parameters)
    {
        var admitted = new List<RelationalOutput>(candidates.Count);
        var buckets = new Dictionary<string, List<RelationalOutput>>(StringComparer.Ordinal);
        foreach (RelationalOutput candidate in candidates)
        {
            BaseRelationalRow row = ProjectRow(plan, candidate, parameters);
            string key = Key(row.Fields.OrderBy(static field => field.FieldId, StringComparer.Ordinal)
                .Select(static field => field.Value));
            string digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            if (!buckets.TryGetValue(digest, out List<RelationalOutput>? bucket))
                buckets[digest] = bucket = [];
            if (bucket.Any(existing => CompareProjected(plan, existing, candidate, parameters) == 0))
                continue;
            bucket.Add(candidate);
            admitted.Add(candidate);
        }
        return admitted;
    }

    private static QueryValue Aggregate(
        BaseRelationalReadAggregate aggregate, RelationalContext context, RelationalContext[] group,
        IReadOnlyDictionary<string, QueryValue> parameters)
    {
        QueryValue[] values = aggregate.Operand is null ? [] : group.Select(row => Value(aggregate.Operand, row, parameters)).Where(static value => value.Kind != QueryValueKind.Null).ToArray();
        return aggregate.Kind switch
        {
            BaseAggregateKind.Count => Integer(aggregate.Operand is null ? group.LongLength : values.LongLength),
            BaseAggregateKind.CountDistinct => Integer(values.Select(Key).Distinct(StringComparer.Ordinal).LongCount()),
            BaseAggregateKind.Sum => NumericResult(OperandKind(aggregate.Operand!, context, parameters), values.Sum(Numeric)),
            BaseAggregateKind.Average => values.Length == 0 ? Null() : AverageResult(OperandKind(aggregate.Operand!, context, parameters), values.Average(Numeric)),
            BaseAggregateKind.Minimum => values.Length == 0 ? Null() : values.Aggregate(static (best, next) => CompareValues(next, best) < 0 ? next : best),
            BaseAggregateKind.Maximum => values.Length == 0 ? Null() : values.Aggregate(static (best, next) => CompareValues(next, best) > 0 ? next : best),
            BaseAggregateKind.Any => Boolean(values.Any(static value => value.Boolean == true)),
            BaseAggregateKind.All => Boolean(values.All(static value => value.Boolean == true)),
            _ => throw new InvalidOperationException(),
        };
    }

    private static QueryValueKind OperandKind(
        BaseRelationalOperand operand,
        RelationalContext context,
        IReadOnlyDictionary<string, QueryValue> parameters) => operand.Kind switch
    {
        BaseRelationalOperandKind.RecordId => QueryValueKind.Id,
        BaseRelationalOperandKind.RecordRevision => QueryValueKind.String,
        BaseRelationalOperandKind.SourceField => context.FieldKinds[operand.SourceId! + "\0" + operand.FieldId!],
        BaseRelationalOperandKind.Parameter => parameters[operand.ParameterId!].Kind,
        BaseRelationalOperandKind.Literal => operand.Literal!.Kind,
        _ => throw new InvalidOperationException(),
    };

    private static QueryValue NumericResult(QueryValueKind kind, decimal value) => kind switch
    {
        QueryValueKind.Integer => Integer(checked((long)value)),
        QueryValueKind.Number => Real((double)value),
        _ => Number(value),
    };

    private static QueryValue AverageResult(QueryValueKind kind, decimal value) =>
        kind == QueryValueKind.Number ? Real((double)value) : Number(value);

    private static bool Predicate(
        BaseRelationalPredicate predicate, RelationalContext context,
        IReadOnlyDictionary<string, QueryValue> parameters, IReadOnlyDictionary<string, QueryValue>? aggregates)
    {
        return predicate.Kind switch
        {
            FilterNodeKind.True => true,
            FilterNodeKind.False => false,
            FilterNodeKind.Not => !Predicate(predicate.Children![0], context, parameters, aggregates),
            FilterNodeKind.And => predicate.Children!.All(child => Predicate(child, context, parameters, aggregates)),
            FilterNodeKind.Or => predicate.Children!.Any(child => Predicate(child, context, parameters, aggregates)),
            FilterNodeKind.IsNull => Present(predicate.Left!, context) && Value(predicate.Left!, context, parameters, aggregates).Kind == QueryValueKind.Null,
            FilterNodeKind.IsDefined => Present(predicate.Left!, context),
            FilterNodeKind.Compare => Present(predicate.Left!, context) && Present(predicate.Right!, context) && Compare(Value(predicate.Left!, context, parameters, aggregates), Value(predicate.Right!, context, parameters, aggregates), predicate.Operator),
            FilterNodeKind.In => Present(predicate.Left!, context) && Present(predicate.Right!, context) && Value(predicate.Right!, context, parameters, aggregates).Array?.Any(item => ValuesEqual(Value(predicate.Left!, context, parameters, aggregates), item)) == true,
            FilterNodeKind.Between => Between(predicate, context, parameters, aggregates),
            _ => throw new InvalidOperationException(),
        };
    }

    private static bool Between(BaseRelationalPredicate predicate, RelationalContext context, IReadOnlyDictionary<string, QueryValue> parameters, IReadOnlyDictionary<string, QueryValue>? aggregates)
    {
        if (!Present(predicate.Left!, context) || !Present(predicate.Right!, context)) return false;
        QueryValue value = Value(predicate.Left!, context, parameters, aggregates);
        QueryValue[] bounds = Value(predicate.Right!, context, parameters, aggregates).Array ?? [];
        return bounds.Length == 2 && CompareValues(value, bounds[0]) >= 0 && CompareValues(value, bounds[1]) <= 0;
    }

    private static bool Present(BaseRelationalOperand operand, RelationalContext context)
    {
        if (operand.Kind is not (BaseRelationalOperandKind.SourceField or BaseRelationalOperandKind.RecordId or BaseRelationalOperandKind.RecordRevision)) return true;
        StoredRecord? record = context.Records.GetValueOrDefault(operand.SourceId!);
        return record is not null && (operand.Kind is BaseRelationalOperandKind.RecordId or BaseRelationalOperandKind.RecordRevision || TryReadField(record.Payload, operand.FieldId!, out _));
    }

    private static bool Compare(QueryValue left, QueryValue right, FilterOperator operation)
    {
        int comparison = CompareValues(left, right);
        return operation switch
        {
            FilterOperator.Equal => comparison == 0,
            FilterOperator.NotEqual => comparison != 0,
            FilterOperator.LessThan => comparison < 0,
            FilterOperator.LessThanOrEqual => comparison <= 0,
            FilterOperator.GreaterThan => comparison > 0,
            FilterOperator.GreaterThanOrEqual => comparison >= 0,
            FilterOperator.Contains => left.Kind == QueryValueKind.String && right.Kind == QueryValueKind.String
                && (left.String ?? string.Empty).Contains(right.String ?? string.Empty, StringComparison.Ordinal),
            FilterOperator.StartsWith => left.Kind == QueryValueKind.String && right.Kind == QueryValueKind.String
                && (left.String ?? string.Empty).StartsWith(right.String ?? string.Empty, StringComparison.Ordinal),
            FilterOperator.EndsWith => left.Kind == QueryValueKind.String && right.Kind == QueryValueKind.String
                && (left.String ?? string.Empty).EndsWith(right.String ?? string.Empty, StringComparison.Ordinal),
            _ => throw new InvalidOperationException(),
        };
    }

    private static QueryValue Value(
        BaseRelationalOperand operand, RelationalContext context,
        IReadOnlyDictionary<string, QueryValue> parameters,
        IReadOnlyDictionary<string, QueryValue>? aggregates = null)
    {
        if (operand.Kind == BaseRelationalOperandKind.Parameter) return parameters[operand.ParameterId!];
        if (operand.Kind == BaseRelationalOperandKind.Literal) return operand.Literal!;
        if (operand.Kind == BaseRelationalOperandKind.Aggregate) return aggregates![operand.AggregateId!];
        StoredRecord? record = context.Records.GetValueOrDefault(operand.SourceId!);
        if (record is null) return Null();
        if (operand.Kind == BaseRelationalOperandKind.RecordId) return new QueryValue { Kind = QueryValueKind.Id, Id = record.Id.Value };
        if (operand.Kind == BaseRelationalOperandKind.RecordRevision) return new QueryValue { Kind = QueryValueKind.String, String = record.Metadata.Revision?.Value ?? throw new InvalidOperationException() };
        if (operand.Kind == BaseRelationalOperandKind.SubjectReference)
            return SubjectReferenceValue(operand, record, context.State, context.Subjects);
        if (operand.Kind == BaseRelationalOperandKind.StoredSubjectReference)
            return StoredSubjectReferenceValue(operand, record, context.Subjects);
        return FieldValue(record, operand.FieldId!, context.FieldKinds[operand.SourceId! + "\0" + operand.FieldId!]);
    }

    private static QueryValue StoredSubjectReferenceValue(
        BaseRelationalOperand operand,
        StoredRecord record,
        IReadOnlyList<BaseExportedSubjectDefinition> subjects)
    {
        if (!TryReadField(record.Payload, operand.FieldId!, out JsonElement element)
            || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return Null();
        BaseExportedSubjectDefinition definition = subjects.Single(subject =>
            string.Equals(subject.Id, operand.SubjectContractId, StringComparison.Ordinal)
            && subject.Version == operand.SubjectContractVersion);
        (BaseSubjectId subjectId, BaseSubjectAuthorityEpoch epoch, BaseSubjectIncarnation incarnation) =
            BaseSubjectReferenceEncoding.DecodeElement(
                element, definition.SubjectIdKind, definition.MaximumSubjectIdUtf8Bytes);
        return new QueryValue
        {
            Kind = QueryValueKind.SubjectReference,
            SubjectId = new string(subjectId.Value.AsSpan()),
            SubjectIdKind = definition.SubjectIdKind,
            SubjectIdMaximumUtf8Bytes = definition.MaximumSubjectIdUtf8Bytes,
            SubjectAuthorityEpoch = epoch.ToBase64Url(),
            SubjectIncarnation = incarnation.ToBase64Url(),
        };
    }

    private static QueryValue SubjectReferenceValue(BaseRelationalOperand operand, StoredRecord record, InMemoryStoreState state, IReadOnlyList<BaseExportedSubjectDefinition> subjects)
    {
        BaseExportedSubjectDefinition definition = subjects.Single(subject =>
            string.Equals(subject.Id, operand.SubjectContractId, StringComparison.Ordinal)
            && subject.Version == operand.SubjectContractVersion);
        if (!string.Equals(definition.ValidationPlan.PrivateCollectionId, record.CollectionId, StringComparison.Ordinal)
            || !state.SubjectContracts.TryGetValue(SubjectContractKey(definition.Id, definition.Version), out InMemorySubjectContractState? contract)
            || state.SubjectLifetimes.Values.SingleOrDefault(candidate =>
                string.Equals(candidate.ContractId, definition.Id, StringComparison.Ordinal)
                && candidate.ContractVersion == definition.Version
                && string.Equals(candidate.PrivateCollectionId, record.CollectionId, StringComparison.Ordinal)
                && candidate.PrivateRecordId == record.Id) is not InMemorySubjectLifetimeState lifetime
            || !string.Equals(lifetime.PrivateCollectionId, record.CollectionId, StringComparison.Ordinal)
            || lifetime.PrivateRecordId != record.Id)
            throw new InvalidOperationException();
        return new QueryValue
        {
            Kind = QueryValueKind.SubjectReference,
            SubjectId = lifetime.SubjectId.Value,
            SubjectIdKind = definition.SubjectIdKind,
            SubjectIdMaximumUtf8Bytes = definition.MaximumSubjectIdUtf8Bytes,
            SubjectAuthorityEpoch = contract.AuthorityEpoch.ToBase64Url(),
            SubjectIncarnation = lifetime.Incarnation.ToBase64Url(),
        };
    }

    private static QueryValue FieldValue(StoredRecord record, string storedName, QueryValueKind kind)
    {
        if (!TryReadField(record.Payload, storedName, out JsonElement element)) return Null();
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return Null();
        return kind switch
        {
            QueryValueKind.Id => new QueryValue { Kind = kind, Id = element.GetString() },
            QueryValueKind.Boolean => Boolean(element.GetBoolean()),
            QueryValueKind.Integer => Integer(element.GetInt64()),
            QueryValueKind.Number => Real(element.GetDouble()),
            QueryValueKind.Decimal => new QueryValue { Kind = kind, Decimal = element.GetDecimal().ToString(CultureInfo.InvariantCulture) },
            QueryValueKind.DateTime => new QueryValue { Kind = kind, DateTime = element.GetDateTimeOffset() },
            QueryValueKind.CanonicalJson => new QueryValue
            {
                Kind = kind,
                CanonicalJsonUtf8 = ImmutableArray.Create(BaseStrictUtf8.Encode(element.GetRawText())),
            },
            _ => new QueryValue { Kind = QueryValueKind.String, String = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText() },
        };
    }

    private static QueryValueKind InMemoryFieldKind(FieldDefinition field) => field.ScalarKind is BaseScalarKind.Guid or BaseScalarKind.RecordId
        ? QueryValueKind.Id
        : field.ScalarKind == BaseScalarKind.CanonicalJson
        ? QueryValueKind.CanonicalJson
        : field.ScalarKind == BaseScalarKind.ModuleGeneration
        ? QueryValueKind.String
        : field.Format == "date-time"
        ? QueryValueKind.DateTime
        : field.Type switch
    {
        "boolean" => QueryValueKind.Boolean,
        "integer" => QueryValueKind.Integer,
        "number" => QueryValueKind.Number,
        "decimal" => QueryValueKind.Decimal,
        "id" => QueryValueKind.Id,
        "dateTime" => QueryValueKind.DateTime,
        _ => QueryValueKind.String,
    };

    private static int CompareSort(BaseRelationalReadPlan plan, RelationalOutput left, RelationalOutput right, IReadOnlyDictionary<string, QueryValue> parameters)
    {
        foreach (BaseRelationalReadSort item in plan.Sort)
        {
            QueryValue leftValue = Value(item.Operand, left.Context, parameters, left.Aggregates);
            QueryValue rightValue = Value(item.Operand, right.Context, parameters, right.Aggregates);
            bool leftNull = leftValue.Kind == QueryValueKind.Null;
            bool rightNull = rightValue.Kind == QueryValueKind.Null;
            if (leftNull != rightNull && item.Nulls != QueryNullOrder.Unspecified)
                return leftNull == (item.Nulls == QueryNullOrder.First) ? -1 : 1;
            int comparison = CompareValues(leftValue, rightValue);
            if (comparison != 0) return item.Direction == QuerySortDirection.Desc ? -comparison : comparison;
        }
        return CompareProjected(plan, left, right, parameters);
    }

    private static int CompareProjected(
        BaseRelationalReadPlan plan,
        RelationalOutput left,
        RelationalOutput right,
        IReadOnlyDictionary<string, QueryValue> parameters)
    {
        foreach (BaseRelationalReadProjection projection in plan.Projection)
        {
            int comparison = CompareValues(
                Value(projection.Operand, left.Context, parameters, left.Aggregates),
                Value(projection.Operand, right.Context, parameters, right.Aggregates));
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    private static int CompareValues(QueryValue left, QueryValue right)
    {
        if (left.Kind == QueryValueKind.Null || right.Kind == QueryValueKind.Null) return left.Kind.CompareTo(right.Kind);
        if (NumericKind(left.Kind) && NumericKind(right.Kind)) return Numeric(left).CompareTo(Numeric(right));
        if (left.Kind is QueryValueKind.String or QueryValueKind.Id && right.Kind is QueryValueKind.String or QueryValueKind.Id)
            return string.CompareOrdinal(left.String ?? left.Id, right.String ?? right.Id);
        return string.CompareOrdinal(Key(left), Key(right));
    }
    private static bool ValuesEqual(QueryValue left, QueryValue right) => CompareValues(left, right) == 0;
    private static bool NumericKind(QueryValueKind kind) => kind is QueryValueKind.Integer or QueryValueKind.Number or QueryValueKind.Decimal;
    private static decimal Numeric(QueryValue value) => value.Kind switch
    { QueryValueKind.Integer => value.Integer!.Value, QueryValueKind.Number => (decimal)value.Number!.Value, QueryValueKind.Decimal => decimal.Parse(value.Decimal!, CultureInfo.InvariantCulture), _ => throw new InvalidOperationException() };
    private static QueryValue Null() => new() { Kind = QueryValueKind.Null };
    private static QueryValue Integer(long value) => new() { Kind = QueryValueKind.Integer, Integer = value };
    private static QueryValue Number(decimal value) => new() { Kind = QueryValueKind.Decimal, Decimal = value.ToString(CultureInfo.InvariantCulture) };
    private static QueryValue Real(double value) => new() { Kind = QueryValueKind.Number, Number = value };
    private static QueryValue Boolean(bool value) => new() { Kind = QueryValueKind.Boolean, Boolean = value };
    private static string Key(IEnumerable<QueryValue> values) => string.Concat(values.Select(value =>
    {
        string item = Key(value);
        return item.Length.ToString(CultureInfo.InvariantCulture) + ":" + item;
    }));
    private static string Key(QueryValue value)
    {
        string text = value.String ?? value.Boolean?.ToString() ?? value.Integer?.ToString(CultureInfo.InvariantCulture) ??
            value.Number?.ToString("R", CultureInfo.InvariantCulture) ?? value.Decimal ??
            value.DateTime?.ToString("O", CultureInfo.InvariantCulture) ?? value.Id ??
            (value.Array is null ? (value.CanonicalJsonUtf8.IsDefault ? "" : Convert.ToBase64String(value.CanonicalJsonUtf8.AsSpan())) : Key(value.Array));
        return ((int)value.Kind).ToString(CultureInfo.InvariantCulture) + ":" + text.Length.ToString(CultureInfo.InvariantCulture) + ":" + text;
    }
    private static long EstimateBytes(BaseRelationalRow row)
    {
        long bytes = 0;
        foreach (BaseRelationalFieldValue field in row.Fields)
            bytes = checked(bytes + Encoding.UTF8.GetByteCount(field.FieldId) +
                (field.Value.Kind == QueryValueKind.CanonicalJson
                    ? field.Value.CanonicalJsonUtf8.Length
                    : Encoding.UTF8.GetByteCount(Key(field.Value))));
        return bytes;
    }

    private sealed class RelationalContext(IReadOnlyDictionary<string, QueryValueKind> fieldKinds, InMemoryStoreState state, IReadOnlyList<BaseExportedSubjectDefinition> subjects)
    {
        internal IReadOnlyDictionary<string, QueryValueKind> FieldKinds { get; } = fieldKinds;
        internal InMemoryStoreState State { get; } = state;
        internal IReadOnlyList<BaseExportedSubjectDefinition> Subjects { get; } = subjects;
        internal Dictionary<string, StoredRecord?> Records { get; } = new(StringComparer.Ordinal);
        internal RelationalContext Clone() { var clone = new RelationalContext(FieldKinds, State, Subjects); foreach (var pair in Records) clone.Records[pair.Key] = pair.Value; return clone; }
    }
    private sealed record RelationalOutput(RelationalContext Context, IReadOnlyDictionary<string, QueryValue> Aggregates);
}
