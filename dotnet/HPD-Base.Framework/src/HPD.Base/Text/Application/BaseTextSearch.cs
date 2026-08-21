using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CS1591

namespace HPD.Base;

/// <summary>Contains one authoritative lexical match.</summary>
public sealed record BaseTextMatch<T>
{
    public required BaseRecord<T> Record { get; init; }
    public required RevisionToken Revision { get; init; }
    public required BaseTextScore Score { get; init; }
}

/// <summary>Contains one complete bounded lexical result page.</summary>
public sealed record BaseTextResult<T>
{
    public required ImmutableArray<BaseTextMatch<T>> Matches { get; init; }
    public BaseTextCursor? Next { get; init; }
    public required BaseTextConsistencyToken Consistency { get; init; }
}

/// <summary>Contains opaque continuation syntax for one lexical result universe.</summary>
public readonly struct BaseTextCursor : IEquatable<BaseTextCursor>
{
    private readonly ImmutableArray<byte> _bytes;
    private BaseTextCursor(ImmutableArray<byte> bytes) => _bytes = ImmutableArray.Create(bytes.ToArray());
    public static BaseTextCursor Parse(string value) => TryParse(value, out BaseTextCursor result) ? result : throw new FormatException(BaseTextErrorCodes.CursorInvalid);
    public static bool TryParse(string? value, out BaseTextCursor result)
    {
        result = default; if (string.IsNullOrEmpty(value)) return false;
        if (value.Length is < 1 or > 16 * 1024 || value.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))) return false;
        result = new(ImmutableArray.Create(System.Text.Encoding.ASCII.GetBytes(value))); return true;
    }
    public string Encode() => _bytes.IsDefaultOrEmpty ? throw new InvalidOperationException(BaseTextErrorCodes.CursorInvalid) : System.Text.Encoding.ASCII.GetString(_bytes.AsSpan());
    internal ImmutableArray<byte> Bytes => _bytes.IsDefault ? [] : ImmutableArray.Create(_bytes.ToArray());
    internal static BaseTextCursor Create(ImmutableArray<byte> bytes) => new(bytes);
    public bool Equals(BaseTextCursor other) => !_bytes.IsDefault && !other._bytes.IsDefault && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(_bytes.AsSpan(), other._bytes.AsSpan());
    public override bool Equals(object? obj) => obj is BaseTextCursor other && Equals(other);
    public override int GetHashCode() => _bytes.IsDefault ? 0 : BitConverter.ToInt32(System.Security.Cryptography.SHA256.HashData(_bytes.AsSpan()));
    public override string ToString() => "BaseTextCursor[redacted]";
}

/// <summary>Builds one typed bounded lexical query.</summary>
public sealed class BaseTextSearch<T>
{
    private readonly BaseCollectionSession<T> _session; private readonly BaseTextIndex<T> _index; private readonly BaseTextQuery _query;
    private readonly BaseTextCandidateConstraint _constraint; private readonly int? _take; private readonly BaseTextCursor? _after; private readonly BaseTextConsistencyRequirement _consistency;
    internal BaseTextSearch(BaseCollectionSession<T> session, BaseTextIndex<T> index, BaseTextQuery query, BaseTextCandidateConstraint? constraint = null, int? take = null, BaseTextCursor? after = null, BaseTextConsistencyRequirement? consistency = null)
    { _session = session; _index = index; _query = query; _constraint = constraint ?? new BaseTextCandidateConstraint.True(); _take = take; _after = after; _consistency = consistency ?? new BaseTextConsistencyRequirement.Current(); }
    public BaseTextSearch<T> Where<TValue>(BaseField<T, TValue> field, TValue value)
    {
        ArgumentNullException.ThrowIfNull(field); BaseTextIndexFilterFieldDefinition declared = _index.Definition.FilterFields.SingleOrDefault(item => item.StableFieldId == field.Id) ?? throw new InvalidOperationException("The field is not a declared text filter.");
        BaseTextFilterValue converted = ConvertValue(value, declared.ValueKind); var leaf = new BaseTextCandidateConstraint.Equal(new BaseTextFilterField(field.Id, declared.ValueKind), converted);
        return new(_session, _index, _query, _constraint is BaseTextCandidateConstraint.True ? leaf : new BaseTextCandidateConstraint.And([_constraint, leaf]), _take, _after, _consistency);
    }
    public BaseTextSearch<T> WhereAny<TValue>(BaseField<T, TValue> field, params TValue[] values)
    {
        ArgumentNullException.ThrowIfNull(field); ArgumentNullException.ThrowIfNull(values); if (values.Length is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(values));
        BaseTextIndexFilterFieldDefinition declared = _index.Definition.FilterFields.SingleOrDefault(item => item.StableFieldId == field.Id) ?? throw new InvalidOperationException("The field is not a declared text filter.");
        BaseTextFilterField handle = new(field.Id, declared.ValueKind);
        BaseTextCandidateConstraint leaf = BaseTextConstraintContract.In(handle, values.Select(value => ConvertValue(value, declared.ValueKind)));
        return Add(leaf);
    }
    public BaseTextSearch<T> WhereNull<TValue>(BaseField<T, TValue> field)
    {
        ArgumentNullException.ThrowIfNull(field); BaseTextIndexFilterFieldDefinition declared = _index.Definition.FilterFields.SingleOrDefault(item => item.StableFieldId == field.Id) ?? throw new InvalidOperationException("The field is not a declared text filter.");
        return Add(new BaseTextCandidateConstraint.IsNull(new BaseTextFilterField(field.Id, declared.ValueKind)));
    }
    public BaseTextSearch<T> WhereMissing<TValue>(BaseField<T, TValue> field)
    {
        ArgumentNullException.ThrowIfNull(field); BaseTextIndexFilterFieldDefinition declared = _index.Definition.FilterFields.SingleOrDefault(item => item.StableFieldId == field.Id) ?? throw new InvalidOperationException("The field is not a declared text filter.");
        return Add(new BaseTextCandidateConstraint.IsMissing(new BaseTextFilterField(field.Id, declared.ValueKind)));
    }
    public BaseTextSearch<T> Take(int count) => count > 0 ? new(_session, _index, _query, _constraint, count, _after, _consistency) : throw new ArgumentOutOfRangeException(nameof(count));
    public BaseTextSearch<T> After(BaseTextCursor cursor) => new(_session, _index, _query, _constraint, _take, cursor, _consistency);
    public BaseTextSearch<T> WithConsistency(BaseTextConsistencyRequirement requirement) => new(_session, _index, _query, _constraint, _take, _after, requirement ?? throw new ArgumentNullException(nameof(requirement)));
    private BaseTextSearch<T> Add(BaseTextCandidateConstraint leaf) => new(_session, _index, _query, _constraint is BaseTextCandidateConstraint.True ? leaf : new BaseTextCandidateConstraint.And([_constraint, leaf]), _take, _after, _consistency);
    public async ValueTask<BaseResult<BaseTextResult<T>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (_take is null) throw new InvalidOperationException("Take(count) is required before execution.");
        OperationResult<BaseTextRuntimeResult> result = await _session.Session.Services.GetRequiredService<IBaseTextRuntime>().ExecuteAsync(new BaseTextRuntimeRequest
        {
            Collection = _session.Contract.Definition, Index = _index.Definition, Query = _query, Constraint = _constraint, Take = _take.Value,
            After = _after, Consistency = _consistency, Principal = _session.Session.Principal, Operation = _session.Session.Operation(BaseOperationKind.TextQuery, _session.Contract.Id),
        }, cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, value => new BaseTextResult<T>
        {
            Matches = value.Matches.Select(match => new BaseTextMatch<T> { Record = BaseRecordCodec.Decode(_session.Session.Serializer(_session.Contract), match.Record), Revision = match.Revision, Score = match.Score }).ToImmutableArray(),
            Next = value.Next,
            Consistency = value.Consistency,
        });
    }
    /// <summary>Emits complete authoritative replacement pages whenever indexed collection authority changes.</summary>
    public async IAsyncEnumerable<BaseLiveQueryTransition<BaseTextResult<T>>> LiveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_take is null) throw new InvalidOperationException("Take(count) is required before execution.");
        IBaseLiveQueryCoordinator coordinator = _session.Session.Services.GetRequiredService<IBaseLiveQueryCoordinator>();
        IBaseDependencyReferenceFactory dependencies = _session.Session.Services.GetRequiredService<IBaseDependencyReferenceFactory>();
        string queryId = "base.text." + _session.Contract.Id + "." + _index.Definition.Id + "." + Convert.ToHexString(BaseTextQueryContract.Digest(_query).AsSpan());
        await using IBaseLiveQuerySubscription<BaseTextResult<T>> subscription = await coordinator.SubscribeAsync(new BaseLiveQueryRequest<BaseTextResult<T>>
        {
            QueryId = queryId,
            ExecuteAsync = async token =>
            {
                BaseResult<BaseTextResult<T>> result = await ExecuteAsync(token).ConfigureAwait(false);
                if (result is not BaseSuccess<BaseTextResult<T>> success) throw new BaseLiveQueryException(((BaseFailure<BaseTextResult<T>>)result).Error.Code, "Text search execution failed.");
                return new BaseLiveQueryEvaluation<BaseTextResult<T>>
                {
                    Value = success.Value,
                    Dependencies = dependencies.CreateSet(dependencies.Create(BaseDependencyIds.Collection,
                        new BaseDependencyParameter("tenant", _session.Session.Principal.CurrentTenantId),
                        new BaseDependencyParameter("collection", _session.Contract.Id))),
                };
            },
        }, cancellationToken).ConfigureAwait(false);
        await foreach (BaseLiveQueryTransition<BaseTextResult<T>> transition in subscription.Transitions.WithCancellation(cancellationToken).ConfigureAwait(false)) yield return transition;
    }
    private static BaseTextFilterValue ConvertValue<TValue>(TValue value, BaseTextFilterValueKind kind) => kind switch
    {
        BaseTextFilterValueKind.String when value is string text => BaseTextFilterValue.FromString(text),
        BaseTextFilterValueKind.Id when value is RecordId id => BaseTextFilterValue.FromId(id.Value),
        BaseTextFilterValueKind.Id when value is string id => BaseTextFilterValue.FromId(id),
        BaseTextFilterValueKind.Boolean when value is bool boolean => BaseTextFilterValue.FromBoolean(boolean),
        BaseTextFilterValueKind.Integer when value is sbyte or byte or short or ushort or int or uint or long => BaseTextFilterValue.FromInteger(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)),
        _ => throw new ArgumentException("The value does not match the declared text-filter kind.", nameof(value)),
    };
}

/// <summary>Adds lexical-search entry points to principal-bound collection sessions.</summary>
public static class BaseTextSessionExtensions
{
    public static BaseTextSearch<T> Text<T>(this BaseCollectionSession<T> session, BaseTextIndex<T> index, BaseTextQuery query)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(index); ArgumentNullException.ThrowIfNull(query);
        if (!string.Equals(session.Contract.Id, index.Definition.CollectionId, StringComparison.Ordinal)) throw new ArgumentException("The text index belongs to another collection.", nameof(index));
        return new(session, index, BaseTextQueryContract.Validate(query));
    }
}
