using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base;

/// <summary>Contains one complete bounded vector result.</summary>
public sealed record BaseVectorResult<T>
{
    /// <summary>Gets the authoritative ranked matches.</summary>
    public required BaseVectorMatch<T>[] Matches { get; init; }
    /// <summary>Gets the stable vector-index identifier.</summary>
    public required string VectorIndexId { get; init; }
    /// <summary>Gets the vector-index generation.</summary>
    public required long VectorIndexGeneration { get; init; }
    /// <summary>Gets the stable provider identifier.</summary>
    public required string ProviderId { get; init; }
    /// <summary>Gets the ranking accuracy.</summary>
    public required BaseVectorResultAccuracy Accuracy { get; init; }
    /// <summary>Gets opaque consistency evidence bound to this result.</summary>
    public required BaseVectorConsistencyToken ConsistencyToken { get; init; }
}

/// <summary>Builds one bounded vector query from a principal-bound collection session.</summary>
public sealed class BaseVectorQuery<T>
{
    private readonly BaseCollectionSession<T> _session;
    private readonly BaseVectorIndex<T> _index;
    private readonly BaseVector? _vector;
    private readonly BaseVectorCandidateConstraint _constraint;
    private readonly int? _take;
    private readonly BaseVectorConsistencyRequirement? _consistency;

    internal BaseVectorQuery(BaseCollectionSession<T> session, BaseVectorIndex<T> index, BaseVector? vector = null, BaseVectorCandidateConstraint? constraint = null, int? take = null, BaseVectorConsistencyRequirement? consistency = null)
    { _session = session; _index = index; _vector = vector; _constraint = constraint ?? new BaseVectorCandidateConstraint.True(); _take = take; _consistency = consistency; }

    /// <summary>Sets the externally produced query vector.</summary>
    public BaseVectorQuery<T> Nearest(BaseVector vector) => new(_session, _index, vector, _constraint, _take, _consistency);

    /// <summary>Adds one generated, declared equality filter.</summary>
    public BaseVectorQuery<T> Where<TValue>(BaseField<T, TValue> field, TValue value)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!_index.FilterFieldIds.Contains(field.Id, StringComparer.Ordinal)) throw new InvalidOperationException($"Field '{field.Id}' is not declared as a vector filter field.");
        var leaf = new BaseVectorCandidateConstraint.Equal(ToFilterField(field, value), ToFilterValue(value));
        return new(_session, _index, _vector, _constraint is BaseVectorCandidateConstraint.True ? leaf : new BaseVectorCandidateConstraint.And([_constraint, leaf]), _take, _consistency);
    }

    /// <summary>Adds one generated equality filter combined with the existing filters by OR.</summary>
    public BaseVectorQuery<T> OrWhere<TValue>(BaseField<T, TValue> field, TValue value)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!_index.FilterFieldIds.Contains(field.Id, StringComparer.Ordinal)) throw new InvalidOperationException($"Field '{field.Id}' is not declared as a vector filter field.");
        var leaf = new BaseVectorCandidateConstraint.Equal(ToFilterField(field, value), ToFilterValue(value));
        return new(_session, _index, _vector, _constraint is BaseVectorCandidateConstraint.True ? leaf : new BaseVectorCandidateConstraint.Or([_constraint, leaf]), _take, _consistency);
    }

    /// <summary>Adds a bounded generated IN filter combined with the existing filters by AND.</summary>
    public BaseVectorQuery<T> WhereAny<TValue>(BaseField<T, TValue> field, params TValue[] values)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(values));
        if (!_index.FilterFieldIds.Contains(field.Id, StringComparer.Ordinal)) throw new InvalidOperationException($"Field '{field.Id}' is not declared as a vector filter field.");
        BaseVectorFilterValue[] converted = values.Select(ToFilterValue).ToArray();
        BaseVectorFilterValueKind kind = converted[0].Kind;
        if (converted.Any(value => value.Kind != kind)) throw new ArgumentException("Every IN value must use the same portable value kind.", nameof(values));
        var leaf = new BaseVectorCandidateConstraint.In(new BaseVectorFilterField(field.Id, kind), converted);
        return new(_session, _index, _vector, _constraint is BaseVectorCandidateConstraint.True ? leaf : new BaseVectorCandidateConstraint.And([_constraint, leaf]), _take, _consistency);
    }

    /// <summary>Sets the required bounded top-K result count.</summary>
    public BaseVectorQuery<T> Take(int count) => count > 0 ? new(_session, _index, _vector, _constraint, count, _consistency) : throw new ArgumentOutOfRangeException(nameof(count));

    /// <summary>Sets the explicit consistency requirement.</summary>
    public BaseVectorQuery<T> WithConsistency(BaseVectorConsistencyRequirement requirement) => new(_session, _index, _vector, _constraint, _take, requirement ?? throw new ArgumentNullException(nameof(requirement)));

    /// <summary>Executes this builder exactly once as one complete bounded result.</summary>
    public async ValueTask<BaseResult<BaseVectorResult<T>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (_vector is null) throw new InvalidOperationException("Nearest(vector) is required before execution.");
        if (_take is null) throw new InvalidOperationException("Take(count) is required before execution.");
        IBaseVectorRuntime runtime = _session.Session.Services.GetRequiredService<IBaseVectorRuntime>();
        OperationResult<BaseVectorRuntimeResult> result = await runtime.ExecuteAsync(new BaseVectorRuntimeRequest
        {
            Collection = _session.Contract.Definition,
            Index = _index.Definition,
            Vector = _vector.Value,
            Constraint = _constraint,
            Take = _take.Value,
            Consistency = _consistency,
            Principal = _session.Session.Principal,
            Operation = _session.Session.Operation(BaseOperationKind.VectorQuery, _session.Contract.Id),
        }, cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, value => new BaseVectorResult<T>
        {
            Matches = value.Matches.Select(match => new BaseVectorMatch<T> { Record = BaseRecordCodec.Decode(_session.Contract, match.Record), Rank = match.Rank, Measure = match.Measure }).ToArray(),
            VectorIndexId = value.VectorIndexId,
            VectorIndexGeneration = value.VectorIndexGeneration,
            ProviderId = value.ProviderId,
            Accuracy = value.Accuracy,
            ConsistencyToken = value.ConsistencyToken,
        });
    }

    internal async ValueTask<BaseResult<BaseVectorConsistencyToken>> CaptureAsync(CancellationToken cancellationToken)
    {
        IBaseVectorRuntime runtime = _session.Session.Services.GetRequiredService<IBaseVectorRuntime>();
        OperationResult<BaseVectorConsistencyToken> result = await runtime.CaptureAsync(_session.Contract.Definition, _index.Definition, _session.Session.Principal, _session.Session.Operation(BaseOperationKind.VectorQuery, _session.Contract.Id), cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static token => token);
    }

    private static BaseVectorFilterField ToFilterField<TValue>(BaseField<T, TValue> field, TValue value) => new(field.Id, ToFilterValue(value).Kind);
    private static BaseVectorFilterValue ToFilterValue<TValue>(TValue value) => value switch
    {
        null => BaseVectorFilterValue.Null(),
        bool boolean => BaseVectorFilterValue.FromBoolean(boolean),
        byte number => BaseVectorFilterValue.FromInteger(number), sbyte number => BaseVectorFilterValue.FromInteger(number),
        short number => BaseVectorFilterValue.FromInteger(number), ushort number => BaseVectorFilterValue.FromInteger(number),
        int number => BaseVectorFilterValue.FromInteger(number), uint number => BaseVectorFilterValue.FromInteger(number),
        long number => BaseVectorFilterValue.FromInteger(number),
        string text => BaseVectorFilterValue.FromString(text),
        RecordId id => BaseVectorFilterValue.FromId(id.Value),
        _ => throw new NotSupportedException("The field value is not portable in an L39 vector constraint."),
    };
}

/// <summary>Adds vector-query entry points to principal-bound collection sessions.</summary>
public static class BaseVectorSessionExtensions
{
    /// <summary>Begins a typed vector query for one generated index.</summary>
    public static BaseVectorQuery<T> Vector<T>(this BaseCollectionSession<T> session, BaseVectorIndex<T> index)
    { ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(index); if (!string.Equals(session.Contract.Id, index.CollectionId, StringComparison.Ordinal)) throw new ArgumentException("The vector index belongs to another collection.", nameof(index)); return new(session, index); }

    /// <summary>Captures opaque finite consistency evidence without running vector ranking.</summary>
    public static ValueTask<BaseResult<BaseVectorConsistencyToken>> CaptureConsistencyAsync<T>(this BaseVectorQuery<T> query, CancellationToken cancellationToken = default) => query.CaptureAsync(cancellationToken);
}
