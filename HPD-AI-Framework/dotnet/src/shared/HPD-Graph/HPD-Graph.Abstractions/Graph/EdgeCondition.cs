namespace HPDAgent.Graph.Abstractions.Graph;

/// <summary>
/// Type of condition for edge traversal.
/// </summary>
public enum ConditionType
{
    /// <summary>
    /// Always traverse (unconditional edge).
    /// </summary>
    Always,

    /// <summary>
    /// Traverse if field equals value.
    /// </summary>
    FieldEquals,

    /// <summary>
    /// Traverse if field does not equal value.
    /// </summary>
    FieldNotEquals,

    /// <summary>
    /// Traverse if field is greater than value.
    /// </summary>
    FieldGreaterThan,

    /// <summary>
    /// Traverse if field is less than value.
    /// </summary>
    FieldLessThan,

    /// <summary>
    /// Traverse if field exists (not null).
    /// </summary>
    FieldExists,

    /// <summary>
    /// Traverse if field does not exist (is null).
    /// </summary>
    FieldNotExists,

    /// <summary>
    /// Traverse if field contains value (for strings/collections).
    /// </summary>
    FieldContains,

    /// <summary>
    /// Default/fallback edge - traverse only if no other conditions from the same source node match.
    /// Only one default edge per source node is allowed.
    /// </summary>
    Default,

    // ========================================
    // Upstream State Conditions
    // ========================================

    /// <summary>
    /// Traverse if at least one upstream node succeeded.
    /// Use case: Fallback chains (try A, if fails try B).
    /// Requires context at evaluation time.
    /// </summary>
    UpstreamOneSuccess,

    /// <summary>
    /// Traverse if all upstream nodes completed (any terminal state).
    /// Use case: Aggregation regardless of success/failure.
    /// Requires context at evaluation time.
    /// </summary>
    UpstreamAllDone,

    /// <summary>
    /// Traverse if all upstream nodes completed AND at least one succeeded.
    /// Use case: Partial success handling.
    /// Requires context at evaluation time.
    /// </summary>
    UpstreamAllDoneOneSuccess,

    // ========================================
    // Compound Logic Conditions
    // ========================================

    /// <summary>
    /// Traverse if ALL sub-conditions are true.
    /// Sub-conditions are specified in the <see cref="EdgeCondition.Conditions"/> list.
    /// </summary>
    And,

    /// <summary>
    /// Traverse if ANY sub-condition is true.
    /// Sub-conditions are specified in the <see cref="EdgeCondition.Conditions"/> list.
    /// </summary>
    Or,

    /// <summary>
    /// Traverse if the single sub-condition is false.
    /// The sub-condition is the first element of <see cref="EdgeCondition.Conditions"/>.
    /// </summary>
    Not,

    // ========================================
    // Advanced String Conditions
    // ========================================

    /// <summary>
    /// Traverse if field value starts with the given string.
    /// </summary>
    FieldStartsWith,

    /// <summary>
    /// Traverse if field value ends with the given string.
    /// </summary>
    FieldEndsWith,

    /// <summary>
    /// Traverse if field value matches a regular expression.
    /// Use <see cref="EdgeCondition.RegexOptions"/> for flags (e.g. "IgnoreCase,Multiline").
    /// </summary>
    FieldMatchesRegex,

    /// <summary>
    /// Traverse if field value is null, empty string, or whitespace-only.
    /// </summary>
    FieldIsEmpty,

    /// <summary>
    /// Traverse if field value is NOT null, empty string, or whitespace-only.
    /// </summary>
    FieldIsNotEmpty,

    // ========================================
    // Multi-Value Collection Conditions
    // ========================================

    /// <summary>
    /// Traverse if the field array contains AT LEAST ONE of the given values.
    /// Value must be an array/collection.
    /// </summary>
    FieldContainsAny,

    /// <summary>
    /// Traverse if the field array contains ALL of the given values.
    /// Value must be an array/collection.
    /// </summary>
    FieldContainsAll,
}

/// <summary>
/// Condition for edge traversal (declarative only - no lambdas).
/// Evaluated against node outputs.
/// </summary>
public sealed record EdgeCondition
{
    /// <summary>
    /// Condition type.
    /// </summary>
    public required ConditionType Type { get; init; }

    /// <summary>
    /// Field name to evaluate (from node outputs).
    /// Required for field-based conditions.
    /// </summary>
    public string? Field { get; init; }

    /// <summary>
    /// Value to compare against.
    /// Required for comparison conditions.
    /// For <see cref="ConditionType.FieldContainsAny"/> and <see cref="ConditionType.FieldContainsAll"/>,
    /// this must be an array or collection of values.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Sub-conditions for compound types (<see cref="ConditionType.And"/>, <see cref="ConditionType.Or"/>,
    /// <see cref="ConditionType.Not"/>). Null for leaf conditions.
    /// </summary>
    public IReadOnlyList<EdgeCondition>? Conditions { get; init; }

    /// <summary>
    /// Regex flags for <see cref="ConditionType.FieldMatchesRegex"/>.
    /// Serialized as a comma-separated string of <see cref="System.Text.RegularExpressions.RegexOptions"/> member names,
    /// e.g. "IgnoreCase,Multiline".
    /// </summary>
    public string? RegexOptions { get; init; }

    /// <summary>
    /// Get a human-readable description of this condition.
    /// </summary>
    public string GetDescription()
    {
        return Type switch
        {
            ConditionType.Always => "Always",
            ConditionType.FieldEquals => $"{Field} == {Value}",
            ConditionType.FieldNotEquals => $"{Field} != {Value}",
            ConditionType.FieldGreaterThan => $"{Field} > {Value}",
            ConditionType.FieldLessThan => $"{Field} < {Value}",
            ConditionType.FieldExists => $"{Field} exists",
            ConditionType.FieldNotExists => $"{Field} not exists",
            ConditionType.FieldContains => $"{Field} contains {Value}",
            ConditionType.Default => "Default (fallback)",
            ConditionType.UpstreamOneSuccess => "At least one upstream succeeded",
            ConditionType.UpstreamAllDone => "All upstreams completed",
            ConditionType.UpstreamAllDoneOneSuccess => "All upstreams done, at least one succeeded",
            ConditionType.And => $"({string.Join(" AND ", Conditions?.Select(c => c.GetDescription()) ?? [])})",
            ConditionType.Or => $"({string.Join(" OR ", Conditions?.Select(c => c.GetDescription()) ?? [])})",
            ConditionType.Not => $"NOT ({Conditions?.FirstOrDefault()?.GetDescription() ?? ""})",
            ConditionType.FieldStartsWith => $"{Field} starts with {Value}",
            ConditionType.FieldEndsWith => $"{Field} ends with {Value}",
            ConditionType.FieldMatchesRegex => $"{Field} matches regex {Value}",
            ConditionType.FieldIsEmpty => $"{Field} is empty",
            ConditionType.FieldIsNotEmpty => $"{Field} is not empty",
            ConditionType.FieldContainsAny => $"{Field} contains any of [{Value}]",
            ConditionType.FieldContainsAll => $"{Field} contains all of [{Value}]",
            _ => Type.ToString()
        };
    }
}
