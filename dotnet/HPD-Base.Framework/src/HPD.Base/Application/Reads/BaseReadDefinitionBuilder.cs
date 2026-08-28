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

/// <summary>Creates closed string predicates for registered-read operands.</summary>
public static class BaseReadStringOperandExtensions
{
    /// <summary>Builds an ordinal contains predicate over a string source.</summary>
    public static BaseReadPredicate Contains(
        this BaseReadOperand<string> operand,
        BaseReadOperand<string> value) => Compare(operand, value, FilterOperator.Contains);

    /// <summary>Builds an ordinal prefix predicate over a string source.</summary>
    public static BaseReadPredicate StartsWith(
        this BaseReadOperand<string> operand,
        BaseReadOperand<string> value) => Compare(operand, value, FilterOperator.StartsWith);

    /// <summary>Builds an ordinal suffix predicate over a string source.</summary>
    public static BaseReadPredicate EndsWith(
        this BaseReadOperand<string> operand,
        BaseReadOperand<string> value) => Compare(operand, value, FilterOperator.EndsWith);

    private static BaseReadPredicate Compare(
        BaseReadOperand<string> operand,
        BaseReadOperand<string> value,
        FilterOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(operand);
        ArgumentNullException.ThrowIfNull(value);
        return new BaseReadPredicate(new BaseRelationalPredicate
        {
            Kind = FilterNodeKind.Compare,
            Operator = @operator,
            Left = operand.Operand,
            Right = value.Operand,
        });
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

    /// <summary>References an optional value field through its non-null scalar type for null-aware predicates.</summary>
    public BaseReadOperand<TValue> OptionalField<TValue>(BaseField<TRecord, TValue?> field)
        where TValue : struct => new(new BaseRelationalOperand
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

    /// <summary>References the current authoritative record revision.</summary>
    public BaseReadOperand<RevisionToken> Revision => new(new BaseRelationalOperand
    {
        Kind = BaseRelationalOperandKind.RecordRevision,
        SourceId = Id,
        FieldId = "base.revision",
    });
}

/// <summary>Represents an output-only exported-subject reference projection.</summary>
/// <typeparam name="TSubject">The public exported-subject marker type.</typeparam>
public sealed class BaseReadSubjectReferenceProjection<TSubject>
{
    internal BaseReadSubjectReferenceProjection(BaseRelationalOperand operand) => Operand = operand;
    internal BaseRelationalOperand Operand { get; }
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

/// <summary>Builds one provenance-sealed independent count branch during host construction.</summary>
public sealed class BaseReadCountBranchBuilder<TParameters, TRecord>
{
    private readonly BaseReadSource<TRecord> _source;
    private readonly IReadOnlyDictionary<string, BaseRelationalReadParameter> _parameters;
    private BaseRelationalPredicate? _predicate;

    internal BaseReadCountBranchBuilder(BaseReadSource<TRecord> source, IReadOnlyDictionary<string, BaseRelationalReadParameter> parameters)
    { _source = source; _parameters = parameters; }

    /// <summary>References one field on this branch's sole source.</summary>
    public BaseReadOperand<TValue> Field<TValue>(BaseField<TRecord, TValue> field) => _source.Field(field);
    /// <summary>References one declared required request parameter.</summary>
    public BaseReadOperand<TValue> Parameter<TValue>(BaseReadParameter<TParameters, TValue> parameter)
    { ArgumentNullException.ThrowIfNull(parameter); Require(parameter.Id); return new(new() { Kind = BaseRelationalOperandKind.Parameter, ParameterId = parameter.Id }); }
    /// <summary>References one declared nullable value parameter through its non-null scalar type.</summary>
    public BaseReadOperand<TValue> OptionalParameter<TValue>(BaseReadParameter<TParameters, TValue?> parameter) where TValue : struct
    { ArgumentNullException.ThrowIfNull(parameter); Require(parameter.Id); return new(new() { Kind = BaseRelationalOperandKind.Parameter, ParameterId = parameter.Id }); }
    /// <summary>References one canonical GUID parameter as a typed record identifier for an exact target collection.</summary>
    public BaseReadOperand<BaseRecordId<TTarget>> RecordIdParameter<TTarget>(BaseReadParameter<TParameters, Guid> parameter)
    { ArgumentNullException.ThrowIfNull(parameter); Require(parameter.Id); return new(new() { Kind = BaseRelationalOperandKind.Parameter, ParameterId = parameter.Id }); }
    /// <summary>Creates one exact closed-enum literal from source-generated wire authority.</summary>
    public BaseReadOperand<TEnum> ClosedEnumLiteral<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        BaseClosedEnumGeneratedAuthority<TEnum> authority = BaseClosedEnumGeneratedContract.Resolve<TEnum>();
        if (!authority.ToWire.TryGetValue(value, out string? wire))
            throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        return new(new BaseRelationalOperand { Kind = BaseRelationalOperandKind.Literal, Literal = new QueryValue { Kind = QueryValueKind.String, String = wire } });
    }

    /// <summary>References this branch's canonical record identifier.</summary>
    public BaseReadOperand<BaseRecordId<TRecord>> RecordId => _source.RecordId;
    /// <summary>Installs the branch's sole predicate.</summary>
    public BaseReadCountBranchBuilder<TParameters, TRecord> Where(BaseReadPredicate predicate)
    { if (_predicate is not null) throw new InvalidOperationException("base.relational.read.invalid"); _predicate = predicate?.Predicate ?? throw new ArgumentNullException(nameof(predicate)); return this; }
    internal BaseRelationalPredicate? Build() { Validate(_predicate); return _predicate; }
    private void Require(string id) { if (!_parameters.ContainsKey(id)) throw new InvalidOperationException("base.relational.read.invalid"); }
    private void Validate(BaseRelationalPredicate? predicate)
    {
        if (predicate is null) return;
        Validate(predicate.Left); Validate(predicate.Right);
        foreach (BaseRelationalPredicate child in predicate.Children ?? []) Validate(child);
    }
    private void Validate(BaseRelationalOperand? operand)
    {
        if (operand is null) return;
        if (operand.SourceId is not null && !string.Equals(operand.SourceId, _source.Id, StringComparison.Ordinal)
            || operand.ParameterId is not null && !_parameters.ContainsKey(operand.ParameterId)
            || operand.Kind is BaseRelationalOperandKind.Aggregate or BaseRelationalOperandKind.SubjectReference or BaseRelationalOperandKind.StoredSubjectReference)
            throw new InvalidOperationException("base.relational.read.invalid");
    }
}

/// <summary>Builds the closed canonical topology of one generated registered read.</summary>
public sealed class BaseReadDefinitionBuilder<TParameters, TRow>
{
    private readonly string _id;
    private readonly IReadOnlyDictionary<string, BaseRelationalReadParameter> _parameters;
    private readonly Dictionary<string, (CollectionDefinition Definition, IReadOnlyDictionary<string, object> Fields)> _sourceContracts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BaseReadCanonicalJsonAuthority> _canonicalJsonBindings = new(StringComparer.Ordinal);
    private readonly List<BaseRelationalReadSource> _sources = [];
    private readonly List<BaseRelationalReadJoin> _joins = [];
    private readonly List<BaseRelationalReadProjection> _projection = [];
    private readonly List<BaseRelationalReadAggregate> _aggregates = [];
    private readonly List<BaseRelationalCompoundCountBranch> _compoundBranches = [];
    private readonly List<BaseRelationalOperand> _groups = [];
    private BaseRelationalPredicate? _predicate;
    private BaseRelationalPredicate? _having;
    private readonly List<BaseRelationalReadSort> _sort = [];
    private bool _distinct;
    private BaseRegisteredReadPaginationAuthority _pagination = new()
    {
        Mode = BaseRegisteredReadPaginationMode.PageOnly,
        MaximumOffset = 0,
    };
    private BaseRelationalReadBudgets _budgets = new()
    {
        MaxResultRows = 1_000,
        MaxResultBytes = 1_048_576,
        MaxOperations = 64,
        MaxExecutionMilliseconds = 2_000,
        MaxCompoundBranches = 0,
        MaxCompoundOperations = 0,
    };

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

    /// <summary>Adds one independent record-ID count branch in installed discriminator order.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> CountBranch<TRecord>(
        string branchId,
        BaseReadField<TRow, string> discriminatorOutput,
        string discriminator,
        BaseCollection<TRecord> collection,
        BaseReadField<TRow, long> countOutput,
        Action<BaseReadCountBranchBuilder<TParameters, TRecord>> configure)
    {
        ArgumentNullException.ThrowIfNull(discriminatorOutput); ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(countOutput); ArgumentNullException.ThrowIfNull(configure);
        if (_sources.Count != _compoundBranches.Count || _projection.Count != 0 || _joins.Count != 0 || _aggregates.Count != 0 || _groups.Count != 0 || _sort.Count != 0 || _predicate is not null || _having is not null || _distinct)
            throw new InvalidOperationException("base.relational.read.invalid");
        ValidateCompoundText(branchId, 120); ValidateCompoundText(discriminator, 128);
        if (_compoundBranches.Any(branch => string.Equals(branch.Id, branchId, StringComparison.Ordinal)
                || string.Equals(branch.Discriminator, discriminator, StringComparison.Ordinal)))
            throw new InvalidOperationException("base.relational.read.invalid");
        string sourceId = branchId + ".source";
        AddSource(collection, sourceId, out BaseReadSource<TRecord> source);
        var branch = new BaseReadCountBranchBuilder<TParameters, TRecord>(source, _parameters);
        configure(branch);
        _compoundBranches.Add(new BaseRelationalCompoundCountBranch
        {
            Id = branchId, Source = _sources[^1], Predicate = branch.Build(), Discriminator = discriminator,
            DiscriminatorOutputFieldId = discriminatorOutput.Id, CountOutputFieldId = countOutput.Id,
            BranchChecksum = BaseSchemaAuthorityChecksum.Create(new byte[32]),
        });
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

    /// <summary>References one canonical GUID parameter as a typed record identifier for an exact target collection.</summary>
    public BaseReadOperand<BaseRecordId<TTarget>> RecordIdParameter<TTarget>(
        BaseReadParameter<TParameters, Guid> parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        if (!_parameters.ContainsKey(parameter.Id))
            throw new InvalidOperationException($"Read parameter '{parameter.Id}' is not declared.");
        return new BaseReadOperand<BaseRecordId<TTarget>>(new BaseRelationalOperand
        {
            Kind = BaseRelationalOperandKind.Parameter,
            ParameterId = parameter.Id,
        });
    }

    /// <summary>Binds one required canonical-JSON parameter to an exact installed source field.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> BindCanonicalJsonParameter<TRecord>(
        BaseReadParameter<TParameters, BaseCanonicalJson> parameter,
        BaseField<TRecord, BaseCanonicalJson> field) => BindCanonicalJsonParameterCore(parameter?.Id, field);

    /// <summary>Binds one required present canonical-JSON parameter to an optional or nullable source field.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> BindCanonicalJsonParameter<TRecord>(
        BaseReadParameter<TParameters, BaseCanonicalJson> parameter,
        BaseField<TRecord, BaseCanonicalJson?> field) => BindCanonicalJsonParameterCore(parameter?.Id, field);

    /// <summary>Binds one nullable canonical-JSON parameter to an exact installed source field.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> BindCanonicalJsonParameter<TRecord>(
        BaseReadParameter<TParameters, BaseCanonicalJson?> parameter,
        BaseField<TRecord, BaseCanonicalJson?> field) => BindCanonicalJsonParameterCore(parameter?.Id, field);

    private BaseReadDefinitionBuilder<TParameters, TRow> BindCanonicalJsonParameterCore(string? parameterId, object? field)
    {
        ArgumentNullException.ThrowIfNull(parameterId);
        ArgumentNullException.ThrowIfNull(field);
        if (!_parameters.TryGetValue(parameterId, out BaseRelationalReadParameter? parameter)
            || parameter.Kind != QueryValueKind.CanonicalJson
            || !_canonicalJsonBindings.TryAdd(parameterId, FindCanonicalJsonAuthority(field)))
            throw new InvalidOperationException("base.relational.read.invalid");
        return this;
    }

    /// <summary>References an optional value parameter through its non-null scalar type for null-aware predicates.</summary>
    public BaseReadOperand<TValue> OptionalParameter<TValue>(BaseReadParameter<TParameters, TValue?> parameter)
        where TValue : struct
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

    /// <summary>Creates one exact closed-enum literal from source-generated wire authority.</summary>
    /// <typeparam name="TEnum">The closed enum type.</typeparam>
    /// <param name="value">The declared enum value.</param>
    /// <returns>A provenance-sealed literal operand containing the declared wire value.</returns>
    public BaseReadOperand<TEnum> ClosedEnumLiteral<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        BaseClosedEnumGeneratedAuthority<TEnum> authority = BaseClosedEnumGeneratedContract.Resolve<TEnum>();
        if (!authority.ToWire.TryGetValue(value, out string? wire))
            throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        return new BaseReadOperand<TEnum>(new BaseRelationalOperand
        {
            Kind = BaseRelationalOperandKind.Literal,
            Literal = new QueryValue { Kind = QueryValueKind.String, String = wire },
        });
    }

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

    /// <summary>Maps one acquisition-read output to the current reference for an exported subject.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> ProjectSubjectReference<TSubject, TRecord>(
        BaseReadField<TRow, BaseSubjectReference<TSubject>> field,
        BaseReadSource<TRecord> source,
        BaseGeneratedSubjectRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(registration);
        if (registration.MarkerType != typeof(TSubject))
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
        _projection.Add(new BaseRelationalReadProjection
        {
            FieldId = field.Id,
            Operand = new BaseRelationalOperand
            {
                Kind = BaseRelationalOperandKind.SubjectReference,
                SourceId = source.Id,
                SubjectContractId = registration.Definition.Id,
                SubjectContractVersion = registration.Definition.Version,
            },
        });
        return this;
    }

    /// <summary>Projects one required exported-subject reference exactly as stored on a source record.</summary>
    /// <typeparam name="TSubject">The exported-subject marker type.</typeparam>
    /// <typeparam name="TRecord">The source record type.</typeparam>
    /// <param name="output">The required result field.</param>
    /// <param name="source">The registered-read source.</param>
    /// <param name="storedField">The required stored subject-reference field.</param>
    /// <param name="registration">The generated subject-contract authority.</param>
    /// <returns>This builder.</returns>
    public BaseReadDefinitionBuilder<TParameters, TRow> ProjectStoredSubjectReference<TSubject, TRecord>(
        BaseReadField<TRow, BaseSubjectReference<TSubject>> output,
        BaseReadSource<TRecord> source,
        BaseField<TRecord, BaseSubjectReference<TSubject>> storedField,
        BaseGeneratedSubjectRegistration registration) =>
        ProjectStoredSubjectReferenceCore<TSubject, TRecord, BaseSubjectReference<TSubject>>(
            output, source, storedField, registration);

    /// <summary>Projects one optional or nullable exported-subject reference exactly as stored on a source record.</summary>
    /// <typeparam name="TSubject">The exported-subject marker type.</typeparam>
    /// <typeparam name="TRecord">The source record type.</typeparam>
    /// <param name="output">The nullable result field.</param>
    /// <param name="source">The registered-read source.</param>
    /// <param name="storedField">The optional or nullable stored subject-reference field.</param>
    /// <param name="registration">The generated subject-contract authority.</param>
    /// <returns>This builder.</returns>
    public BaseReadDefinitionBuilder<TParameters, TRow> ProjectStoredSubjectReference<TSubject, TRecord>(
        BaseReadField<TRow, BaseSubjectReference<TSubject>?> output,
        BaseReadSource<TRecord> source,
        BaseField<TRecord, BaseSubjectReference<TSubject>?> storedField,
        BaseGeneratedSubjectRegistration registration) =>
        ProjectStoredSubjectReferenceCore<TSubject, TRecord, BaseSubjectReference<TSubject>?>(
            output, source, storedField, registration);

    private BaseReadDefinitionBuilder<TParameters, TRow> ProjectStoredSubjectReferenceCore<TSubject, TRecord, TValue>(
        BaseReadField<TRow, TValue> output,
        BaseReadSource<TRecord> source,
        BaseField<TRecord, TValue> storedField,
        BaseGeneratedSubjectRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(storedField);
        ArgumentNullException.ThrowIfNull(registration);
        if (registration.MarkerType != typeof(TSubject))
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
        _projection.Add(new BaseRelationalReadProjection
        {
            FieldId = output.Id,
            Operand = new BaseRelationalOperand
            {
                Kind = BaseRelationalOperandKind.StoredSubjectReference,
                SourceId = source.Id,
                FieldId = storedField.Id,
                SubjectContractId = registration.Definition.Id,
                SubjectContractVersion = registration.Definition.Version,
            },
        });
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

    /// <summary>Sets the exact immutable execution budgets for this registered read.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> Limits(
        int maximumResultRows,
        int maximumResultBytes,
        int maximumOperations,
        int maximumExecutionMilliseconds)
    {
        if (maximumResultRows < 1) throw new ArgumentOutOfRangeException(nameof(maximumResultRows));
        if (maximumResultBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumResultBytes));
        if (maximumOperations < 1) throw new ArgumentOutOfRangeException(nameof(maximumOperations));
        if (maximumExecutionMilliseconds < 1) throw new ArgumentOutOfRangeException(nameof(maximumExecutionMilliseconds));
        _budgets = new BaseRelationalReadBudgets
        {
            MaxResultRows = maximumResultRows,
            MaxResultBytes = maximumResultBytes,
            MaxOperations = maximumOperations,
            MaxExecutionMilliseconds = maximumExecutionMilliseconds,
            MaxCompoundBranches = _budgets.MaxCompoundBranches,
            MaxCompoundOperations = _budgets.MaxCompoundOperations,
        };
        return this;
    }

    /// <summary>Allows bounded arbitrary-offset execution for this registered read.</summary>
    /// <param name="maximumOffset">The maximum admitted zero-based offset.</param>
    /// <returns>The same definition builder.</returns>
    public BaseReadDefinitionBuilder<TParameters, TRow> AllowOffsetPagination(int maximumOffset)
    {
        if (maximumOffset is < 0 or > 1_000_000 || _pagination.Mode != BaseRegisteredReadPaginationMode.PageOnly)
            throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        _pagination = new BaseRegisteredReadPaginationAuthority
        {
            Mode = BaseRegisteredReadPaginationMode.PageAndOffset,
            MaximumOffset = maximumOffset,
        };
        return this;
    }

    /// <summary>Sets the exact immutable execution budgets for a compound count read.</summary>
    public BaseReadDefinitionBuilder<TParameters, TRow> CompoundLimits(
        int maximumResultBytes, int maximumOperations, int maximumExecutionMilliseconds,
        int maximumBranches, int maximumCompoundOperations)
    {
        if (maximumBranches < 1 || maximumCompoundOperations < maximumBranches)
            throw new ArgumentOutOfRangeException(nameof(maximumBranches));
        Limits(maximumBranches, maximumResultBytes, maximumOperations, maximumExecutionMilliseconds);
        _budgets = _budgets with { MaxCompoundBranches = maximumBranches, MaxCompoundOperations = maximumCompoundOperations };
        return this;
    }

    internal BaseRelationalReadPlan Build()
    {
        bool compound = _compoundBranches.Count != 0;
        if (_sources.Count == 0 || !compound && _projection.Count == 0)
            throw new InvalidOperationException("A registered read requires a root source and projection.");
        if (compound && _pagination.Mode != BaseRegisteredReadPaginationMode.PageOnly)
            throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        if (compound && (_budgets.MaxCompoundBranches < _compoundBranches.Count || _budgets.MaxCompoundOperations < _compoundBranches.Count))
            throw new InvalidOperationException("base.relational.read.invalid");
        BaseRelationalCompoundCountBranch[] compoundBranches = compound
            ? _compoundBranches.OrderBy(static branch => branch.Discriminator, StringComparer.Ordinal).ToArray()
            : [];
        BaseRelationalReadSource[] sources = compound
            ? compoundBranches.Select(static branch => branch.Source).ToArray()
            : _sources.ToArray();
        return new BaseRelationalReadPlan
        {
            Id = _id,
            Topology = compound ? BaseRelationalReadTopology.CompoundCount : BaseRelationalReadTopology.Ordinary,
            CompoundCountBranches = compoundBranches,
            CompoundChecksum = compound ? BaseSchemaAuthorityChecksum.Create(new byte[32]) : null,
            Sources = sources,
            Joins = _joins.ToArray(),
            Predicate = _predicate,
            GroupKeys = _groups.ToArray(),
            Aggregates = _aggregates.ToArray(),
            Having = _having,
            Projection = _projection.Select(BindProjectionAuthority).ToArray(),
            Distinct = _distinct,
            Sort = _sort.ToArray(),
            Parameters = _parameters.Values.OrderBy(static parameter => parameter.Id, StringComparer.Ordinal)
                .Select(parameter => parameter.Kind == QueryValueKind.CanonicalJson
                    ? parameter with { CanonicalJsonAuthority = _canonicalJsonBindings.GetValueOrDefault(parameter.Id) }
                    : parameter).ToArray(),
            Budgets = _budgets with { },
            Pagination = _pagination with { },
        };
    }

    private static void ValidateCompoundText(string value, int maximumUtf8Bytes)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.IsNormalized(System.Text.NormalizationForm.FormC)
            || System.Text.Encoding.UTF8.GetByteCount(value) > maximumUtf8Bytes)
            throw new InvalidOperationException("base.relational.read.invalid");
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
        _sourceContracts.Add(sourceId, (collection.Definition, collection.Fields));
        source = new BaseReadSource<TRecord>(sourceId, collection);
    }

    private BaseReadCanonicalJsonAuthority FindCanonicalJsonAuthority(object field)
    {
        var matches = _sourceContracts.Values.Where(source => source.Fields.Values.Any(candidate => ReferenceEquals(candidate, field))).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("base.relational.read.invalid");
        FieldDefinition definition = matches[0].Definition.Fields!.Single(item =>
            matches[0].Fields.TryGetValue(item.Id, out object? candidate) && ReferenceEquals(candidate, field));
        return BaseReadCanonicalJsonAuthorityContract.Create(matches[0].Definition.Id, definition);
    }

    private BaseRelationalReadProjection BindProjectionAuthority(BaseRelationalReadProjection projection)
    {
        BaseRelationalOperand operand = projection.Operand;
        if (operand.Kind != BaseRelationalOperandKind.SourceField || operand.SourceId is null || operand.FieldId is null)
            return projection;
        (CollectionDefinition Definition, IReadOnlyDictionary<string, object> Fields) source = _sourceContracts[operand.SourceId];
        FieldDefinition field = source.Definition.Fields!.Single(item => string.Equals(item.Id, operand.FieldId, StringComparison.Ordinal));
        return field.ScalarKind == BaseScalarKind.CanonicalJson
            ? projection with { CanonicalJsonAuthority = BaseReadCanonicalJsonAuthorityContract.Create(source.Definition.Id, field) }
            : projection;
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
        Type declaredType = typeof(TValue);
        Type? arrayElementType = declaredType.IsArray ? declaredType.GetElementType() : null;
        if (declaredType.IsEnum || arrayElementType?.IsEnum == true || value is Enum)
            throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);

        if (value is Array values)
        {
            if (values.Length > 256) throw new ArgumentOutOfRangeException(nameof(value), "A literal array may contain at most 256 values.");
            if (values.Cast<object?>().Any(static item => item is Enum))
                throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
            return new QueryValue
            {
                Kind = QueryValueKind.Array,
                Array = values.Cast<object?>().Select(static item => BaseQueryValue.From(item)).ToArray(),
            };
        }
        return BaseQueryValue.From(value);
    }
}
