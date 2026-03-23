using HPDAgent.Graph.Abstractions.Graph;

namespace HPD.MultiAgent.Routing;

/// <summary>
/// Static factory methods for building serializable <see cref="EdgeCondition"/> instances.
/// Intended for use with a static import:
/// <code>using static HPD.MultiAgent.Routing.Condition;</code>
/// </summary>
/// <remarks>
/// All returned conditions are fully serializable via <c>ExportConfigJson()</c> and
/// can be composed arbitrarily deep:
/// <code>
/// .When(And(
///     Equals("intent", "billing"),
///     Or(Equals("tier", "VIP"), GreaterThan("priority", 5))
/// ))
/// </code>
/// </remarks>
public static class Condition
{
    // ========================================
    // Compound Logic
    // ========================================

    /// <summary>Traverse if ALL sub-conditions are true.</summary>
    public static EdgeCondition And(params EdgeCondition[] conditions) => new()
    {
        Type = ConditionType.And,
        Conditions = conditions
    };

    /// <summary>Traverse if ANY sub-condition is true.</summary>
    public static EdgeCondition Or(params EdgeCondition[] conditions) => new()
    {
        Type = ConditionType.Or,
        Conditions = conditions
    };

    /// <summary>Traverse if the sub-condition is false.</summary>
    public static EdgeCondition Not(EdgeCondition condition) => new()
    {
        Type = ConditionType.Not,
        Conditions = [condition]
    };

    // ========================================
    // Field Equality / Comparison
    // ========================================

    /// <summary>Traverse if <paramref name="field"/> equals <paramref name="value"/>.</summary>
    public static EdgeCondition Equals(string field, object value) => new()
    {
        Type = ConditionType.FieldEquals,
        Field = field,
        Value = value
    };

    /// <summary>Traverse if <paramref name="field"/> does not equal <paramref name="value"/>.</summary>
    public static EdgeCondition NotEquals(string field, object value) => new()
    {
        Type = ConditionType.FieldNotEquals,
        Field = field,
        Value = value
    };

    /// <summary>Traverse if <paramref name="field"/> is greater than <paramref name="value"/>.</summary>
    public static EdgeCondition GreaterThan(string field, object value) => new()
    {
        Type = ConditionType.FieldGreaterThan,
        Field = field,
        Value = value
    };

    /// <summary>Traverse if <paramref name="field"/> is less than <paramref name="value"/>.</summary>
    public static EdgeCondition LessThan(string field, object value) => new()
    {
        Type = ConditionType.FieldLessThan,
        Field = field,
        Value = value
    };

    /// <summary>Traverse if <paramref name="field"/> exists (is not null).</summary>
    public static EdgeCondition Exists(string field) => new()
    {
        Type = ConditionType.FieldExists,
        Field = field
    };

    /// <summary>Traverse if <paramref name="field"/> does not exist (is null or absent).</summary>
    public static EdgeCondition NotExists(string field) => new()
    {
        Type = ConditionType.FieldNotExists,
        Field = field
    };

    /// <summary>Traverse if <paramref name="field"/> contains <paramref name="value"/> (string or collection).</summary>
    public static EdgeCondition Contains(string field, object value) => new()
    {
        Type = ConditionType.FieldContains,
        Field = field,
        Value = value
    };

    // ========================================
    // Advanced String Conditions
    // ========================================

    /// <summary>Traverse if <paramref name="field"/> starts with <paramref name="prefix"/>.</summary>
    public static EdgeCondition StartsWith(string field, string prefix) => new()
    {
        Type = ConditionType.FieldStartsWith,
        Field = field,
        Value = prefix
    };

    /// <summary>Traverse if <paramref name="field"/> ends with <paramref name="suffix"/>.</summary>
    public static EdgeCondition EndsWith(string field, string suffix) => new()
    {
        Type = ConditionType.FieldEndsWith,
        Field = field,
        Value = suffix
    };

    /// <summary>
    /// Traverse if <paramref name="field"/> matches the regular expression <paramref name="pattern"/>.
    /// </summary>
    /// <param name="field">Field name.</param>
    /// <param name="pattern">Regular expression pattern.</param>
    /// <param name="options">Optional regex options (e.g. <see cref="System.Text.RegularExpressions.RegexOptions.IgnoreCase"/>).</param>
    public static EdgeCondition MatchesRegex(
        string field,
        string pattern,
        System.Text.RegularExpressions.RegexOptions options = System.Text.RegularExpressions.RegexOptions.None)
    {
        var optionsStr = options == System.Text.RegularExpressions.RegexOptions.None
            ? null
            : options.ToString(); // produces e.g. "IgnoreCase, Multiline" (comma-space separated by default)

        // Normalise to comma-only (no spaces) for consistency with the evaluator parser
        if (optionsStr != null)
            optionsStr = string.Join(",", optionsStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

        return new()
        {
            Type = ConditionType.FieldMatchesRegex,
            Field = field,
            Value = pattern,
            RegexOptions = optionsStr
        };
    }

    /// <summary>Traverse if <paramref name="field"/> is null, empty, or whitespace-only.</summary>
    public static EdgeCondition IsEmpty(string field) => new()
    {
        Type = ConditionType.FieldIsEmpty,
        Field = field
    };

    /// <summary>Traverse if <paramref name="field"/> is NOT null, empty, or whitespace-only.</summary>
    public static EdgeCondition IsNotEmpty(string field) => new()
    {
        Type = ConditionType.FieldIsNotEmpty,
        Field = field
    };

    // ========================================
    // Collection Conditions
    // ========================================

    /// <summary>
    /// Traverse if the <paramref name="field"/> array contains at least one of the given <paramref name="values"/>.
    /// </summary>
    public static EdgeCondition ContainsAny(string field, params object[] values) => new()
    {
        Type = ConditionType.FieldContainsAny,
        Field = field,
        Value = values
    };

    /// <summary>
    /// Traverse if the <paramref name="field"/> array contains all of the given <paramref name="values"/>.
    /// </summary>
    public static EdgeCondition ContainsAll(string field, params object[] values) => new()
    {
        Type = ConditionType.FieldContainsAll,
        Field = field,
        Value = values
    };

    // ========================================
    // Convenience
    // ========================================

    /// <summary>Always traverse (unconditional).</summary>
    public static EdgeCondition Always() => new() { Type = ConditionType.Always };
}
