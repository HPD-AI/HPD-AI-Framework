using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Represents one immutable typed predicate over a BASE collection.</summary>
public sealed class BasePredicate<T>
{
    internal BasePredicate(FilterExpression expression) => Expression = expression;
    internal FilterExpression Expression { get; }

    /// <summary>Creates a bounded conjunction.</summary>
    public static BasePredicate<T> And(params ReadOnlySpan<BasePredicate<T>> predicates) => Group(FilterNodeKind.And, predicates);
    /// <summary>Creates a bounded disjunction.</summary>
    public static BasePredicate<T> Or(params ReadOnlySpan<BasePredicate<T>> predicates) => Group(FilterNodeKind.Or, predicates);
    /// <summary>Negates this predicate.</summary>
    public BasePredicate<T> Not() => new(new FilterExpression { Kind = FilterNodeKind.Not, Children = [Expression] });

    private static BasePredicate<T> Group(FilterNodeKind kind, ReadOnlySpan<BasePredicate<T>> predicates)
    {
        if (predicates.Length == 0) throw new ArgumentException("A predicate group cannot be empty.", nameof(predicates));
        return new BasePredicate<T>(new FilterExpression
        {
            Kind = kind,
            Children = predicates.ToArray().Select(static item => item?.Expression
                ?? throw new ArgumentException("A predicate cannot be null.")).ToArray(),
        });
    }
}

/// <summary>Provides the closed typed predicate vocabulary for declared BASE fields.</summary>
public static class BaseFieldPredicateExtensions
{
    /// <summary>Creates an equality predicate.</summary>
    public static BasePredicate<T> Equal<T, TValue>(this BaseField<T, TValue> field, TValue value) => Compare(field, FilterOperator.Equal, value, BaseFieldOperator.Equal);
    /// <summary>Creates an inequality predicate.</summary>
    public static BasePredicate<T> NotEqual<T, TValue>(this BaseField<T, TValue> field, TValue value) => Compare(field, FilterOperator.NotEqual, value, BaseFieldOperator.Equal);
    /// <summary>Creates a less-than predicate.</summary>
    public static BasePredicate<T> LessThan<T, TValue>(this BaseField<T, TValue> field, TValue value) => Compare(field, FilterOperator.LessThan, value, BaseFieldOperator.Order);
    /// <summary>Creates a less-than-or-equal predicate.</summary>
    public static BasePredicate<T> LessThanOrEqual<T, TValue>(this BaseField<T, TValue> field, TValue value) => Compare(field, FilterOperator.LessThanOrEqual, value, BaseFieldOperator.Order);
    /// <summary>Creates a greater-than predicate.</summary>
    public static BasePredicate<T> GreaterThan<T, TValue>(this BaseField<T, TValue> field, TValue value) => Compare(field, FilterOperator.GreaterThan, value, BaseFieldOperator.Order);
    /// <summary>Creates a greater-than-or-equal predicate.</summary>
    public static BasePredicate<T> GreaterThanOrEqual<T, TValue>(this BaseField<T, TValue> field, TValue value) => Compare(field, FilterOperator.GreaterThanOrEqual, value, BaseFieldOperator.Order);
    /// <summary>Creates a bounded membership predicate.</summary>
    public static BasePredicate<T> In<T, TValue>(this BaseField<T, TValue> field, ImmutableArray<TValue> values)
    {
        Ensure(field, BaseFieldOperator.Membership);
        if (values.IsDefaultOrEmpty) throw new ArgumentException("IN requires at least one value.", nameof(values));
        return new BasePredicate<T>(new FilterExpression { Kind = FilterNodeKind.In, Field = field.Id, Values = values.Select(BaseQueryValue.From).ToArray() });
    }
    /// <summary>Creates an inclusive range predicate.</summary>
    public static BasePredicate<T> Between<T, TValue>(this BaseField<T, TValue> field, TValue lower, TValue upper)
    {
        Ensure(field, BaseFieldOperator.Order);
        return new BasePredicate<T>(new FilterExpression { Kind = FilterNodeKind.Between, Field = field.Id, Values = [BaseQueryValue.From(lower), BaseQueryValue.From(upper)] });
    }
    /// <summary>Creates an explicit-null predicate.</summary>
    public static BasePredicate<T> IsNull<T, TValue>(this BaseField<T, TValue> field) => Node(field, FilterNodeKind.IsNull);
    /// <summary>Creates a field-presence predicate.</summary>
    public static BasePredicate<T> IsDefined<T, TValue>(this BaseField<T, TValue> field) => Node(field, FilterNodeKind.IsDefined);
    /// <summary>Creates an ordinal contains predicate.</summary>
    public static BasePredicate<T> Contains<T>(this BaseField<T, string> field, string value) => Compare(field, FilterOperator.Contains, value, BaseFieldOperator.Text);
    /// <summary>Creates an ordinal prefix predicate.</summary>
    public static BasePredicate<T> StartsWith<T>(this BaseField<T, string> field, string value) => Compare(field, FilterOperator.StartsWith, value, BaseFieldOperator.Text);
    /// <summary>Creates an ordinal suffix predicate.</summary>
    public static BasePredicate<T> EndsWith<T>(this BaseField<T, string> field, string value) => Compare(field, FilterOperator.EndsWith, value, BaseFieldOperator.Text);
    /// <summary>Creates a provider-certified LIKE predicate.</summary>
    public static BasePredicate<T> Like<T>(this BaseField<T, string> field, string value) => Compare(field, FilterOperator.Like, value, BaseFieldOperator.Text);

    private static BasePredicate<T> Compare<T, TValue>(BaseField<T, TValue> field, FilterOperator op, TValue value, BaseFieldOperator required)
    {
        Ensure(field, required);
        return new BasePredicate<T>(new FilterExpression { Kind = FilterNodeKind.Compare, Field = field.Id, Operator = op, Value = BaseQueryValue.From(value) });
    }
    private static BasePredicate<T> Node<T, TValue>(BaseField<T, TValue> field, FilterNodeKind kind)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new BasePredicate<T>(new FilterExpression { Kind = kind, Field = field.Id });
    }
    private static void Ensure<T, TValue>(BaseField<T, TValue> field, BaseFieldOperator required)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!field.Operators.HasFlag(required)) throw new InvalidOperationException($"Field '{field.Id}' does not support this predicate.");
    }
}
