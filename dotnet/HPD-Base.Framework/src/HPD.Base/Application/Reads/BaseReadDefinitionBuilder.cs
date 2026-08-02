namespace HPD.Base;

/// <summary>Exposes a closed typed operand to the read-definition builder.</summary>
public interface IBaseReadOperand
{
    /// <summary>Gets the canonical operand.</summary>
    BaseRelationalOperand Operand { get; }
}

/// <summary>Represents one typed operand while defining a registered read.</summary>
public sealed class BaseReadOperand<TValue> : IBaseReadOperand
{
    internal BaseReadOperand(BaseRelationalOperand operand) => Operand = operand;
    /// <inheritdoc />
    public BaseRelationalOperand Operand { get; }

    /// <summary>Builds an equality predicate.</summary>
    public BaseReadPredicate Equal(BaseReadOperand<TValue> other) =>
        Compare(other, FilterOperator.Equal);

    /// <summary>Builds an inequality predicate.</summary>
    public BaseReadPredicate NotEqual(BaseReadOperand<TValue> other) => Compare(other, FilterOperator.NotEqual);

    /// <summary>Builds a less-than predicate.</summary>
    public BaseReadPredicate LessThan(BaseReadOperand<TValue> other) => Ordered(other, FilterOperator.LessThan);

    /// <summary>Builds a less-than-or-equal predicate.</summary>
    public BaseReadPredicate LessThanOrEqual(BaseReadOperand<TValue> other) => Ordered(other, FilterOperator.LessThanOrEqual);

    /// <summary>Builds a greater-than predicate.</summary>
    public BaseReadPredicate GreaterThan(BaseReadOperand<TValue> other) => Ordered(other, FilterOperator.GreaterThan);

    /// <summary>Builds a greater-than-or-equal predicate.</summary>
    public BaseReadPredicate GreaterThanOrEqual(BaseReadOperand<TValue> other) => Ordered(other, FilterOperator.GreaterThanOrEqual);

    /// <summary>Builds a bounded set-membership predicate.</summary>
    public BaseReadPredicate In(BaseReadOperand<TValue[]> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new BaseReadPredicate(new BaseRelationalPredicate { Kind = FilterNodeKind.In, Left = Operand, Right = values.Operand });
    }

    /// <summary>Builds an inclusive ordered-range predicate.</summary>
    public BaseReadPredicate Between(BaseReadOperand<TValue[]> bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        BaseReadTypeRules.RequireOrdered<TValue>();
        return new BaseReadPredicate(new BaseRelationalPredicate { Kind = FilterNodeKind.Between, Left = Operand, Right = bounds.Operand });
    }

    /// <summary>Builds a null predicate.</summary>
    public BaseReadPredicate IsNull() => new(new BaseRelationalPredicate { Kind = FilterNodeKind.IsNull, Left = Operand });

    /// <summary>Builds a defined-value predicate.</summary>
    public BaseReadPredicate IsDefined() => new(new BaseRelationalPredicate { Kind = FilterNodeKind.IsDefined, Left = Operand });

    private BaseReadPredicate Compare(BaseReadOperand<TValue> other, FilterOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new BaseReadPredicate(new BaseRelationalPredicate
        {
            Kind = FilterNodeKind.Compare,
            Operator = @operator,
            Left = Operand,
            Right = other.Operand,
        });
    }

    private BaseReadPredicate Ordered(BaseReadOperand<TValue> other, FilterOperator @operator)
    {
        BaseReadTypeRules.RequireOrdered<TValue>();
        return Compare(other, @operator);
    }
}

/// <summary>Represents one typed registered read source.</summary>
public sealed class BaseReadSource<TRecord>
{
    internal BaseReadSource(string id, BaseCollection<TRecord> collection)
    {
        Id = id;
        Collection = collection;
    }

    internal string Id { get; }
    internal BaseCollection<TRecord> Collection { get; }

    /// <summary>References a stable typed source field.</summary>
    public BaseReadOperand<TValue> Field<TValue>(BaseField<TRecord, TValue> field) =>
        new(new BaseRelationalOperand
        {
            Kind = BaseRelationalOperandKind.SourceField,
            SourceId = Id,
            FieldId = field.Id,
        });

    /// <summary>References the canonical record identifier.</summary>
    public BaseReadOperand<BaseRecordId<TRecord>> RecordId =>
        new(new BaseRelationalOperand
        {
            Kind = BaseRelationalOperandKind.RecordId,
            SourceId = Id,
            FieldId = "base.recordId",
        });
}

/// <summary>Represents one closed predicate while defining a registered read.</summary>
public sealed class BaseReadPredicate
{
    internal BaseReadPredicate(BaseRelationalPredicate predicate) => Predicate = predicate;
    internal BaseRelationalPredicate Predicate { get; }

    /// <summary>Combines two predicates with logical conjunction.</summary>
    public BaseReadPredicate And(BaseReadPredicate other) => Combine(other, FilterNodeKind.And);

    /// <summary>Combines two predicates with logical disjunction.</summary>
    public BaseReadPredicate Or(BaseReadPredicate other) => Combine(other, FilterNodeKind.Or);

    /// <summary>Negates this predicate.</summary>
    public BaseReadPredicate Not() => new(new BaseRelationalPredicate { Kind = FilterNodeKind.Not, Children = [Predicate] });

    private BaseReadPredicate Combine(BaseReadPredicate other, FilterNodeKind kind)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new BaseReadPredicate(new BaseRelationalPredicate { Kind = kind, Children = [Predicate, other.Predicate] });
    }
}

/// <summary>Represents one typed aggregate while defining a registered read.</summary>
public sealed class BaseReadAggregate<TValue>
{
    internal BaseReadAggregate(BaseAggregateKind kind, BaseRelationalOperand? operand)
    { Kind = kind; Operand = operand; }
    internal BaseAggregateKind Kind { get; }
    internal BaseRelationalOperand? Operand { get; }
}

/// <summary>Creates closed portable aggregate declarations.</summary>
public static class BaseAggregate
{
    /// <summary>Counts non-null input values.</summary>
    public static BaseReadAggregate<long> Count<TValue>(BaseReadOperand<TValue> operand) => Create<long, TValue>(BaseAggregateKind.Count, operand);
    /// <summary>Counts distinct non-null input values.</summary>
    public static BaseReadAggregate<long> CountDistinct<TValue>(BaseReadOperand<TValue> operand) => Create<long, TValue>(BaseAggregateKind.CountDistinct, operand);
    /// <summary>Sums 32-bit integer input as a 64-bit integer result.</summary>
    public static BaseReadAggregate<long> Sum(BaseReadOperand<int> operand) => Create<long, int>(BaseAggregateKind.Sum, operand);
    /// <summary>Sums 64-bit integer input as a 64-bit integer result.</summary>
    public static BaseReadAggregate<long> Sum(BaseReadOperand<long> operand) => Create<long, long>(BaseAggregateKind.Sum, operand);
    /// <summary>Sums floating-point input as a floating-point result.</summary>
    public static BaseReadAggregate<double> Sum(BaseReadOperand<double> operand) => Create<double, double>(BaseAggregateKind.Sum, operand);
    /// <summary>Sums decimal input exactly.</summary>
    public static BaseReadAggregate<decimal> Sum(BaseReadOperand<decimal> operand) => Create<decimal, decimal>(BaseAggregateKind.Sum, operand);
    /// <summary>Averages 32-bit integer input as an exact decimal result.</summary>
    public static BaseReadAggregate<decimal> Average(BaseReadOperand<int> operand) => Create<decimal, int>(BaseAggregateKind.Average, operand);
    /// <summary>Averages 64-bit integer input as an exact decimal result.</summary>
    public static BaseReadAggregate<decimal> Average(BaseReadOperand<long> operand) => Create<decimal, long>(BaseAggregateKind.Average, operand);
    /// <summary>Averages floating-point input as a floating-point result.</summary>
    public static BaseReadAggregate<double> Average(BaseReadOperand<double> operand) => Create<double, double>(BaseAggregateKind.Average, operand);
    /// <summary>Averages decimal input exactly.</summary>
    public static BaseReadAggregate<decimal> Average(BaseReadOperand<decimal> operand) => Create<decimal, decimal>(BaseAggregateKind.Average, operand);
    /// <summary>Returns the minimum ordered input value.</summary>
    public static BaseReadAggregate<TValue> Minimum<TValue>(BaseReadOperand<TValue> operand)
    { BaseReadTypeRules.RequireOrdered<TValue>(); return Create<TValue, TValue>(BaseAggregateKind.Minimum, operand); }
    /// <summary>Returns the maximum ordered input value.</summary>
    public static BaseReadAggregate<TValue> Maximum<TValue>(BaseReadOperand<TValue> operand)
    { BaseReadTypeRules.RequireOrdered<TValue>(); return Create<TValue, TValue>(BaseAggregateKind.Maximum, operand); }
    /// <summary>Returns whether any boolean input is true.</summary>
    public static BaseReadAggregate<bool> Any(BaseReadOperand<bool> operand) => Create<bool, bool>(BaseAggregateKind.Any, operand);
    /// <summary>Returns whether all boolean inputs are true.</summary>
    public static BaseReadAggregate<bool> All(BaseReadOperand<bool> operand) => Create<bool, bool>(BaseAggregateKind.All, operand);
    private static BaseReadAggregate<TResult> Create<TResult, TValue>(BaseAggregateKind kind, BaseReadOperand<TValue> operand)
    { ArgumentNullException.ThrowIfNull(operand); return new(kind, operand.Operand); }
}

/// <summary>Identifies canonical fields that exist on every record source.</summary>
public static class BaseFields
{
    /// <summary>Gets the contextual canonical record-ID field marker.</summary>
    public static BaseRecordIdField RecordId { get; } = new();
}

/// <summary>Marks the target source's canonical typed record identifier.</summary>
public sealed class BaseRecordIdField { internal BaseRecordIdField() { } }

/// <summary>Builds the closed canonical topology of one generated registered read.</summary>
public sealed class BaseReadDefinitionBuilder<TParameters, TRow>
{
    private readonly string _id;
    private readonly IReadOnlyDictionary<string, BaseRelationalReadParameter> _parameters;
    private readonly List<BaseRelationalReadSource> _sources = [];
    private readonly List<BaseRelationalReadJoin> _joins = [];
    private readonly List<BaseRelationalReadProjection> _projection = [];
    private readonly List<BaseRelationalReadAggregate> _aggregates = [];
    private readonly List<BaseRelationalOperand> _groups = [];
    private BaseRelationalPredicate? _predicate;
    private BaseRelationalPredicate? _having;
    private readonly List<BaseRelationalReadSort> _sort = [];
    private bool _distinct;

    internal BaseReadDefinitionBuilder(
        string id,
        IEnumerable<BaseRelationalReadParameter> parameters)
    {
        BaseApplicationId.Validate(id, nameof(id));
        _id = id;
        _parameters = parameters.ToDictionary(static parameter => parameter.Id, StringComparer.Ordinal);
    }

    /// <summary>Adds the root source.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> From<TRecord>(
        BaseCollection<TRecord> collection,
        string sourceId,
        out BaseReadSource<TRecord> source)
    {
        AddSource(collection, sourceId, out source);
        return this;
    }

    /// <summary>Adds an equality join to another source.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> Join<TRight, TValue>(
        BaseCollection<TRight> collection,
        string sourceId,
        BaseReadOperand<TValue> left,
        BaseField<TRight, TValue> rightField,
        BaseJoinKind kind,
        out BaseReadSource<TRight> source)
    {
        AddSource(collection, sourceId, out source);
        _joins.Add(new BaseRelationalReadJoin
        {
            Kind = kind,
            Left = ((IBaseReadOperand)left).Operand,
            Right = source.Field(rightField).Operand,
        });
        return this;
    }

    /// <summary>Adds an equality join to another source's canonical record identifier.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> Join<TRight>(
        BaseCollection<TRight> collection,
        string sourceId,
        BaseReadOperand<BaseRecordId<TRight>> left,
        BaseRecordIdField rightField,
        BaseJoinKind kind,
        out BaseReadSource<TRight> source)
    {
        ArgumentNullException.ThrowIfNull(rightField);
        AddSource(collection, sourceId, out source);
        _joins.Add(new BaseRelationalReadJoin { Kind = kind, Left = left.Operand, Right = source.RecordId.Operand });
        return this;
    }

    /// <summary>Adds a left equality join to another source.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> LeftJoin<TRight, TValue>(
        BaseCollection<TRight> collection, string sourceId, BaseReadOperand<TValue> left,
        BaseField<TRight, TValue> rightField, out BaseReadSource<TRight> source) =>
        Join(collection, sourceId, left, rightField, BaseJoinKind.Left, out source);

    /// <summary>Adds a predicate.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> Where(BaseReadPredicate predicate)
    {
        _predicate = predicate?.Predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    /// <summary>Adds an aggregate/group predicate.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> Having(BaseReadPredicate predicate)
    { _having = predicate?.Predicate ?? throw new ArgumentNullException(nameof(predicate)); return this; }

    /// <summary>References a declared typed request parameter.</summary>
    public BaseReadOperand<TValue> Parameter<TValue>(BaseReadParameter<TParameters, TValue> parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        if (!_parameters.ContainsKey(parameter.Id))
            throw new InvalidOperationException($"Read parameter '{parameter.Id}' is not declared.");
        return new BaseReadOperand<TValue>(new BaseRelationalOperand
        {
            Kind = BaseRelationalOperandKind.Parameter,
            ParameterId = parameter.Id,
        });
    }

    /// <summary>Creates a closed scalar or bounded-array literal operand.</summary>
    public BaseReadOperand<TValue> Literal<TValue>(TValue value) =>
        new(new BaseRelationalOperand { Kind = BaseRelationalOperandKind.Literal, Literal = BaseReadLiteral.Value(value) });

    /// <summary>Creates a closed typed record-ID literal operand.</summary>
    public BaseReadOperand<BaseRecordId<TRecord>> Literal<TRecord>(BaseRecordId<TRecord> value) =>
        new(new BaseRelationalOperand { Kind = BaseRelationalOperandKind.Literal, Literal = BaseQueryValue.From(value.Value) });

    /// <summary>Adds deterministic grouping operands.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> GroupBy(params IBaseReadOperand[] operands)
    {
        ArgumentNullException.ThrowIfNull(operands);
        _groups.AddRange(operands.Select(static operand => operand.Operand));
        return this;
    }

    /// <summary>Maps one typed projection field.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> Project<TValue>(
        BaseReadField<TRow, TValue> field,
        BaseReadOperand<TValue> operand)
    {
        _projection.Add(new BaseRelationalReadProjection { FieldId = field.Id, Operand = operand.Operand });
        return this;
    }

    /// <summary>Maps one typed projection field to a portable aggregate.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> Aggregate<TValue>(
        BaseReadField<TRow, TValue> field, BaseReadAggregate<TValue> aggregate)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(aggregate);
        if (_aggregates.Any(item => string.Equals(item.Id, field.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Read aggregate '{field.Id}' is already declared.");
        _aggregates.Add(new BaseRelationalReadAggregate { Id = field.Id, Kind = aggregate.Kind, Operand = aggregate.Operand });
        _projection.Add(new BaseRelationalReadProjection
        {
            FieldId = field.Id,
            Operand = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.Aggregate, AggregateId = field.Id },
        });
        return this;
    }

    /// <summary>Maps an aggregate and exposes its typed output for having or ordering.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> Aggregate<TValue>(
        BaseReadField<TRow, TValue> field,
        BaseReadAggregate<TValue> aggregate,
        out BaseReadOperand<TValue> output)
    {
        Aggregate(field, aggregate);
        output = new BaseReadOperand<TValue>(new BaseRelationalOperand
        {
            Kind = BaseRelationalOperandKind.Aggregate,
            AggregateId = field.Id,
        });
        return this;
    }

    /// <summary>Adds deterministic result ordering.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> OrderBy<TValue>(
        BaseReadOperand<TValue> operand, QuerySortDirection direction = QuerySortDirection.Asc,
        QueryNullOrder nulls = QueryNullOrder.Unspecified)
    { ArgumentNullException.ThrowIfNull(operand); BaseReadTypeRules.RequireOrdered<TValue>(); _sort.Add(new BaseRelationalReadSort { Operand = operand.Operand, Direction = direction, Nulls = nulls }); return this; }

    /// <summary>Requests distinct projected rows.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> Distinct()
    { _distinct = true; return this; }

    internal BaseRelationalReadPlan Build()
    {
        if (_sources.Count == 0 || _projection.Count == 0)
            throw new InvalidOperationException("A registered read requires a root source and projection.");
        return new BaseRelationalReadPlan
        {
            Id = _id,
            Sources = _sources.ToArray(),
            Joins = _joins.ToArray(),
            Predicate = _predicate,
            GroupKeys = _groups.ToArray(),
            Aggregates = _aggregates.ToArray(),
            Having = _having,
            Projection = _projection.ToArray(),
            Distinct = _distinct,
            Sort = _sort.ToArray(),
            Parameters = _parameters.Values.OrderBy(static parameter => parameter.Id, StringComparer.Ordinal).ToArray(),
            Budgets = new BaseRelationalReadBudgets
            {
                MaxResultRows = 1_000,
                MaxResultBytes = 1_048_576,
                MaxOperations = 64,
            },
        };
    }

    private void AddSource<TRecord>(
        BaseCollection<TRecord> collection,
        string sourceId,
        out BaseReadSource<TRecord> source)
    {
        ArgumentNullException.ThrowIfNull(collection);
        BaseApplicationId.Validate(sourceId, nameof(sourceId));
        if (_sources.Any(item => string.Equals(item.Id, sourceId, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Read source '{sourceId}' is already declared.");
        _sources.Add(new BaseRelationalReadSource { Id = sourceId, CollectionId = collection.Id });
        source = new BaseReadSource<TRecord>(sourceId, collection);
    }
}

internal static class BaseReadTypeRules
{
    internal static void RequireOrdered<TValue>()
    {
        Type type = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        bool supported = type == typeof(string) || type == typeof(char) || type == typeof(byte) ||
            type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
            type == typeof(float) || type == typeof(double) || type == typeof(decimal) ||
            type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(Guid) ||
            type == typeof(RecordId) || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BaseRecordId<>);
        if (!supported) throw new InvalidOperationException("The operand type does not support portable ordering.");
    }
}

internal static class BaseReadLiteral
{
    internal static QueryValue Value<TValue>(TValue value)
    {
        if (value is Array values)
        {
            if (values.Length > 256) throw new ArgumentOutOfRangeException(nameof(value), "A literal array may contain at most 256 values.");
            return new QueryValue
            {
                Kind = QueryValueKind.Array,
                Array = values.Cast<object?>().Select(static item => BaseQueryValue.From(item)).ToArray(),
            };
        }
        return BaseQueryValue.From(value);
    }
}
