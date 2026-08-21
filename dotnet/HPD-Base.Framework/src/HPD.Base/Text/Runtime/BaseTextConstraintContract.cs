using System.Collections.Immutable;

namespace HPD.Base;

internal static class BaseTextConstraintContract
{
    internal static BaseTextCandidateConstraint Normalize(BaseTextCandidateConstraint value, BaseTextIndexDefinition index)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            BaseTextCandidateConstraint.True => value,
            BaseTextCandidateConstraint.False => value,
            BaseTextCandidateConstraint.IsMissing leaf => new BaseTextCandidateConstraint.IsMissing(Field(leaf.Field, index)),
            BaseTextCandidateConstraint.IsNull leaf => new BaseTextCandidateConstraint.IsNull(Field(leaf.Field, index)),
            BaseTextCandidateConstraint.Equal leaf => new BaseTextCandidateConstraint.Equal(Field(leaf.Field, index), Value(leaf.Value, leaf.Field.ValueKind)),
            BaseTextCandidateConstraint.In leaf => In(Field(leaf.Field, index), leaf.Values),
            BaseTextCandidateConstraint.And logical => Logical(logical.Children, index, true),
            BaseTextCandidateConstraint.Or logical => Logical(logical.Children, index, false),
            _ => throw new ArgumentException(BaseTextErrorCodes.QueryInvalid, nameof(value)),
        };
    }

    internal static BaseTextCandidateConstraint In(BaseTextFilterField field, IEnumerable<BaseTextFilterValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ImmutableArray<BaseTextFilterValue> canonical = values.Select(value => Value(value, field.ValueKind))
            .OrderBy(static value => BaseTextSemanticEvaluator.FilterValueEncoding(value), ByteArrayComparer.Instance)
            .GroupBy(static value => Convert.ToHexString(BaseTextSemanticEvaluator.FilterValueEncoding(value).AsSpan()), StringComparer.Ordinal)
            .Select(static group => group.First()).ToImmutableArray();
        if (canonical.Length is < 1 or > 64) throw new ArgumentException(BaseTextErrorCodes.QueryInvalid, nameof(values));
        return new BaseTextCandidateConstraint.In(field, canonical);
    }

    private static BaseTextCandidateConstraint Logical(ImmutableArray<BaseTextCandidateConstraint> children, BaseTextIndexDefinition index, bool and)
    {
        if (children.IsDefault) throw new ArgumentException(BaseTextErrorCodes.QueryInvalid, nameof(children));
        ImmutableArray<BaseTextCandidateConstraint> canonical = children.Select(child => Normalize(child, index))
            .Select(static child => (Child: child, Bytes: BaseTextSemanticEvaluator.ConstraintNodeEncoding(child)))
            .OrderBy(static value => value.Bytes, ByteArrayComparer.Instance)
            .GroupBy(static value => Convert.ToHexString(value.Bytes.AsSpan()), StringComparer.Ordinal)
            .Select(static group => group.First().Child).ToImmutableArray();
        if (canonical.Length is < 2 or > 16) throw new ArgumentException(BaseTextErrorCodes.QueryInvalid, nameof(children));
        return and ? new BaseTextCandidateConstraint.And(canonical) : new BaseTextCandidateConstraint.Or(canonical);
    }

    private static BaseTextFilterField Field(BaseTextFilterField field, BaseTextIndexDefinition index)
    {
        if (string.IsNullOrWhiteSpace(field.StableFieldId) || !Enum.IsDefined(field.ValueKind)
            || !index.FilterFields.Any(value => value.StableFieldId == field.StableFieldId && value.ValueKind == field.ValueKind))
            throw new ArgumentException(BaseTextErrorCodes.QueryInvalid, nameof(field));
        return field;
    }

    private static BaseTextFilterValue Value(BaseTextFilterValue value, BaseTextFilterValueKind expected)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Kind != expected || !Enum.IsDefined(value.Kind)) throw new ArgumentException(BaseTextErrorCodes.QueryInvalid, nameof(value));
        bool valid = value.Kind switch
        {
            BaseTextFilterValueKind.String => value.StringValue is not null && value.BooleanValue is null && value.IntegerValue is null,
            BaseTextFilterValueKind.Id => ValidId(value.StringValue) && value.BooleanValue is null && value.IntegerValue is null,
            BaseTextFilterValueKind.Boolean => value.StringValue is null && value.BooleanValue is not null && value.IntegerValue is null,
            BaseTextFilterValueKind.Integer => value.StringValue is null && value.BooleanValue is null && value.IntegerValue is not null,
            _ => false,
        };
        if (!valid) throw new ArgumentException(BaseTextErrorCodes.QueryInvalid, nameof(value));
        return value with { StringValue = value.StringValue is null ? null : new string(value.StringValue.AsSpan()) };
    }

    private static bool ValidId(string? value)
    {
        if (value is null) return false;
        try { BaseApplicationId.Validate(value, nameof(value)); return true; }
        catch (ArgumentException) { return false; }
    }

    private sealed class ByteArrayComparer : IComparer<ImmutableArray<byte>>
    {
        internal static readonly ByteArrayComparer Instance = new();
        public int Compare(ImmutableArray<byte> x, ImmutableArray<byte> y) => x.AsSpan().SequenceCompareTo(y.AsSpan());
    }
}
