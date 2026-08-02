using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal sealed partial class VolatileRecordStore
{
    /// <summary>Gets the relational reads.</summary>
    public RelationalReadCapability RelationalReads { get; } = new()
    {
        Supported = true,
        JoinKinds = [BaseJoinKind.Inner, BaseJoinKind.Left, BaseJoinKind.Semi, BaseJoinKind.Anti],
        AggregateKinds = Enum.GetValues<BaseAggregateKind>(),
        ComparisonOperators =
        [
            FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.LessThan,
            FilterOperator.LessThanOrEqual, FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual,
        ],
        ValueKinds = Enum.GetValues<QueryValueKind>(),
        MaxSources = 8,
        MaxJoins = 8,
        MaxPredicateNodes = 256,
        MaxGroupKeys = 8,
        MaxAggregates = 16,
        MaxProjectionFields = 64,
        MaxSortFields = 8,
        MaxResultRows = 1_000,
        MaxResultBytes = 1_048_576,
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
        try
        {
            return ValueTask.FromResult(OperationResults.Ok(ExecuteRead(request, cancellationToken)));
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return ValueTask.FromResult(OperationResults.StoreError<BaseRelationalReadExecutionResult>(new BaseError
            {
                Code = "base.volatile.relational.executionFailed",
                Message = "The volatile relational read failed.",
                Category = ErrorCategory.Store,
            }));
        }
    }

    private BaseRelationalReadExecutionResult ExecuteRead(
        BaseRelationalReadExecutionRequest request,
        CancellationToken cancellationToken)
    {
        VolatileStoreState snapshot = Volatile.Read(ref _publishedState);
        var parameters = request.ParameterValues.ToDictionary(static value => value.ParameterId, static value => value.Value, StringComparer.Ordinal);
        var policies = request.SourcePolicies.ToDictionary(static value => value.SourceId, StringComparer.Ordinal);
        var collections = (_options.Collections ?? []).ToDictionary(static value => value.Id, StringComparer.Ordinal);
        Dictionary<string, QueryValueKind> fieldKinds = request.Plan.Sources.SelectMany(source =>
                (collections[source.CollectionId].Fields ?? []).Select(field => new
                {
                    Key = source.Id + "\0" + field.Name,
                    Kind = VolatileFieldKind(field),
                }))
            .ToDictionary(static item => item.Key, static item => item.Kind, StringComparer.Ordinal);
        BaseRelationalReadPlan plan = LowerPlan(request.Plan, collections);
        var rows = new List<RelationalContext> { new(fieldKinds) };

        BaseRelationalReadSource root = plan.Sources[0];
        rows = SourceRecords(snapshot, root, policies[root.Id], collections[root.CollectionId], cancellationToken)
            .Select(record => new RelationalContext(fieldKinds) { Records = { [root.Id] = record } }).ToList();
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
                output = [BuildOutput(plan, new RelationalContext(fieldKinds), [], parameters)];
            else
                output = rows.GroupBy(row => Key(plan.GroupKeys.Select(operand => Value(operand, row, parameters))))
                    .Select(group => BuildOutput(plan, group.First(), group.ToArray(), parameters)).ToList();
        }
        else output = rows.Select(row => BuildOutput(plan, row, [row], parameters)).ToList();

        if (plan.Having is not null)
            output = output.Where(item => Predicate(plan.Having, item.Context, parameters, item.Aggregates)).ToList();
        if (plan.Distinct)
            output = output.DistinctBy(static item => Key(item.Row.Fields.OrderBy(static field => field.FieldId, StringComparer.Ordinal).Select(static field => field.Value))).ToList();
        output.Sort((left, right) => plan.Sort.Length == 0
            ? CompareProjected(left.Row, right.Row)
            : CompareSort(plan.Sort, left, right, parameters));

        int total = output.Count;
        int page = plan.Page?.Page ?? 1;
        int perPage = plan.Page?.PerPage ?? request.MaxResultRows;
        int offset = checked((page - 1) * perPage);
        BaseRelationalRow[] resultRows = output.Skip(offset).Take(perPage).Select(static item => item.Row).ToArray();
        int bytes = resultRows.Sum(EstimateBytes);
        if (resultRows.Length > request.MaxResultRows || bytes > request.MaxResultBytes)
            throw new InvalidOperationException("Result limit exceeded.");
        return new BaseRelationalReadExecutionResult
        {
            Result = new BaseRelationalReadResult
            {
                Rows = resultRows,
                Page = new PageInfo { Limit = perPage, HasMore = offset + resultRows.Length < total },
                Count = total,
                SchemaGeneration = plan.SchemaGeneration,
            },
            DependencyEvidence = plan.Sources.Select(static source => new BaseReadDependencyEvidence { CollectionId = source.CollectionId }).ToArray(),
        };
    }

    private static StoredRecord[] SourceRecords(
        VolatileStoreState state, BaseRelationalReadSource source, BaseRelationalReadSourcePolicy policy,
        CollectionDefinition collection, CancellationToken cancellationToken)
    {
        FilterExpression? filter = policy.Filter is null ? null : LowerFilter(policy.Filter, collection);
        IEnumerable<StoredRecord> records = GetCollectionOrNull(state, source.CollectionId)?.RecordsById.Values
            ?? Enumerable.Empty<StoredRecord>();
        return records
            .Where(record => { cancellationToken.ThrowIfCancellationRequested(); return filter is null || MatchesFilter(record, filter); })
            .OrderBy(static record => record.Sequence).ThenBy(static record => record.Id.Value, StringComparer.Ordinal).ToArray();
    }

    private static FilterExpression LowerFilter(FilterExpression filter, CollectionDefinition collection)
    {
        string? field = filter.Field;
        if (field is not null)
            field = (collection.Fields ?? []).Single(definition => string.Equals(definition.Id, field, StringComparison.Ordinal)).Name;
        return filter with { Field = field, Children = filter.Children?.Select(child => LowerFilter(child, collection)).ToArray() };
    }

    private static BaseRelationalReadPlan LowerPlan(
        BaseRelationalReadPlan plan,
        IReadOnlyDictionary<string, CollectionDefinition> collections)
    {
        var sourceCollections = plan.Sources.ToDictionary(static source => source.Id, source => collections[source.CollectionId], StringComparer.Ordinal);
        BaseRelationalOperand Lower(BaseRelationalOperand operand)
        {
            if (operand.Kind != BaseRelationalOperandKind.SourceField) return operand;
            string storedName = (sourceCollections[operand.SourceId!].Fields ?? [])
                .Single(field => string.Equals(field.Id, operand.FieldId, StringComparison.Ordinal)).Name;
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
        };
    }

    private static RelationalOutput BuildOutput(
        BaseRelationalReadPlan plan, RelationalContext context, RelationalContext[] group,
        IReadOnlyDictionary<string, QueryValue> parameters)
    {
        var aggregates = new Dictionary<string, QueryValue>(StringComparer.Ordinal);
        foreach (BaseRelationalReadAggregate aggregate in plan.Aggregates)
            aggregates[aggregate.Id] = Aggregate(aggregate, context, group, parameters);
        var fields = plan.Projection.Select(projection => new BaseRelationalFieldValue
        {
            FieldId = projection.FieldId,
            Value = Value(projection.Operand, context, parameters, aggregates),
        }).ToArray();
        return new RelationalOutput(context, aggregates, new BaseRelationalRow { Fields = fields });
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
        if (operand.Kind is not (BaseRelationalOperandKind.SourceField or BaseRelationalOperandKind.RecordId)) return true;
        StoredRecord? record = context.Records.GetValueOrDefault(operand.SourceId!);
        return record is not null && (operand.Kind == BaseRelationalOperandKind.RecordId || TryReadField(record.Payload, operand.FieldId!, out _));
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
        return FieldValue(record, operand.FieldId!, context.FieldKinds[operand.SourceId! + "\0" + operand.FieldId!]);
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
            _ => new QueryValue { Kind = QueryValueKind.String, String = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText() },
        };
    }

    private static QueryValueKind VolatileFieldKind(FieldDefinition field) => field.Format == "date-time"
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

    private static int CompareSort(BaseRelationalReadSort[] sort, RelationalOutput left, RelationalOutput right, IReadOnlyDictionary<string, QueryValue> parameters)
    {
        foreach (BaseRelationalReadSort item in sort)
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
        return CompareProjected(left.Row, right.Row);
    }

    private static int CompareProjected(BaseRelationalRow left, BaseRelationalRow right)
    {
        int length = Math.Min(left.Fields.Length, right.Fields.Length);
        for (int index = 0; index < length; index++)
        {
            int comparison = CompareValues(left.Fields[index].Value, right.Fields[index].Value);
            if (comparison != 0) return comparison;
        }
        return left.Fields.Length.CompareTo(right.Fields.Length);
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
            (value.Array is null ? "" : Key(value.Array));
        return ((int)value.Kind).ToString(CultureInfo.InvariantCulture) + ":" + text.Length.ToString(CultureInfo.InvariantCulture) + ":" + text;
    }
    private static int EstimateBytes(BaseRelationalRow row) => row.Fields.Sum(static field => Encoding.UTF8.GetByteCount(field.FieldId) + Encoding.UTF8.GetByteCount(Key(field.Value)));

    private sealed class RelationalContext(IReadOnlyDictionary<string, QueryValueKind> fieldKinds)
    {
        internal IReadOnlyDictionary<string, QueryValueKind> FieldKinds { get; } = fieldKinds;
        internal Dictionary<string, StoredRecord?> Records { get; } = new(StringComparer.Ordinal);
        internal RelationalContext Clone() { var clone = new RelationalContext(FieldKinds); foreach (var pair in Records) clone.Records[pair.Key] = pair.Value; return clone; }
    }
    private sealed record RelationalOutput(RelationalContext Context, IReadOnlyDictionary<string, QueryValue> Aggregates, BaseRelationalRow Row);
}
