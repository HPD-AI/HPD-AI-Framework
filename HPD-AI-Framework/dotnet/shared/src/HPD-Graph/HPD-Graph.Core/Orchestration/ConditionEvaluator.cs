using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;

namespace HPDAgent.Graph.Core.Orchestration;

/// <summary>
/// Evaluates edge conditions against node outputs.
/// Supports declarative condition evaluation without lambdas.
/// </summary>
public static class ConditionEvaluator
{
    // Regex cache: keyed by (pattern, options) to avoid recompilation.
    private static readonly ConcurrentDictionary<(string Pattern, RegexOptions Options), Regex> _regexCache = new();

    // Default regex match timeout (50ms) to guard against ReDoS.
    private static TimeSpan _regexMatchTimeout = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Override the default regex match timeout (default: 50ms).
    /// Affects all subsequent regex evaluations.
    /// </summary>
    public static TimeSpan RegexMatchTimeout
    {
        get => _regexMatchTimeout;
        set => _regexMatchTimeout = value;
    }

    /// <summary>
    /// Evaluate a condition against node outputs (legacy method for backward compatibility).
    /// For upstream conditions, use the overload that accepts context and edge.
    /// </summary>
    /// <param name="condition">Condition to evaluate</param>
    /// <param name="nodeOutputs">Outputs from the source node</param>
    /// <returns>True if condition is met, false otherwise</returns>
    public static bool Evaluate(EdgeCondition? condition, Dictionary<string, object>? nodeOutputs)
    {
        // Null condition means always traverse (unconditional edge)
        if (condition == null)
        {
            return true;
        }

        // No outputs means we can't evaluate field-based conditions
        if (nodeOutputs == null || nodeOutputs.Count == 0)
        {
            return condition.Type == ConditionType.Always;
        }

        return condition.Type switch
        {
            ConditionType.Always => true,
            ConditionType.FieldEquals => EvaluateFieldEquals(condition, nodeOutputs),
            ConditionType.FieldNotEquals => !EvaluateFieldEquals(condition, nodeOutputs),
            ConditionType.FieldGreaterThan => EvaluateFieldGreaterThan(condition, nodeOutputs),
            ConditionType.FieldLessThan => EvaluateFieldLessThan(condition, nodeOutputs),
            ConditionType.FieldExists => EvaluateFieldExists(condition, nodeOutputs),
            ConditionType.FieldNotExists => !EvaluateFieldExists(condition, nodeOutputs),
            ConditionType.FieldContains => EvaluateFieldContains(condition, nodeOutputs),
            ConditionType.And => EvaluateAnd(condition, nodeOutputs),
            ConditionType.Or => EvaluateOr(condition, nodeOutputs),
            ConditionType.Not => EvaluateNot(condition, nodeOutputs),
            ConditionType.FieldStartsWith => EvaluateFieldStartsWith(condition, nodeOutputs),
            ConditionType.FieldEndsWith => EvaluateFieldEndsWith(condition, nodeOutputs),
            ConditionType.FieldMatchesRegex => EvaluateFieldMatchesRegex(condition, nodeOutputs),
            ConditionType.FieldIsEmpty => EvaluateFieldIsEmpty(condition, nodeOutputs),
            ConditionType.FieldIsNotEmpty => !EvaluateFieldIsEmpty(condition, nodeOutputs),
            ConditionType.FieldContainsAny => EvaluateFieldContainsAny(condition, nodeOutputs),
            ConditionType.FieldContainsAll => EvaluateFieldContainsAll(condition, nodeOutputs),
            ConditionType.UpstreamOneSuccess => throw new InvalidOperationException("Upstream conditions require context. Use Evaluate(condition, nodeOutputs, context, edge) overload."),
            ConditionType.UpstreamAllDone => throw new InvalidOperationException("Upstream conditions require context. Use Evaluate(condition, nodeOutputs, context, edge) overload."),
            ConditionType.UpstreamAllDoneOneSuccess => throw new InvalidOperationException("Upstream conditions require context. Use Evaluate(condition, nodeOutputs, context, edge) overload."),
            _ => false
        };
    }

    /// <summary>
    /// Evaluate a condition against node outputs and/or upstream states.
    /// </summary>
    /// <param name="condition">Condition to evaluate (null = always traverse)</param>
    /// <param name="nodeOutputs">Outputs from source node (for field-based conditions)</param>
    /// <param name="context">Graph context (required for upstream conditions)</param>
    /// <param name="edge">Edge being evaluated (required for upstream conditions)</param>
    /// <returns>True if condition is met, false otherwise</returns>
    public static bool Evaluate(
        EdgeCondition? condition,
        Dictionary<string, object>? nodeOutputs,
        IGraphContext context,
        Edge edge)
    {
        if (condition == null)
            return true;

        return condition.Type switch
        {
            // Field-based conditions (use existing logic)
            ConditionType.Always => true,
            ConditionType.FieldEquals => EvaluateFieldEquals(condition, nodeOutputs),
            ConditionType.FieldNotEquals => !EvaluateFieldEquals(condition, nodeOutputs),
            ConditionType.FieldGreaterThan => EvaluateFieldGreaterThan(condition, nodeOutputs),
            ConditionType.FieldLessThan => EvaluateFieldLessThan(condition, nodeOutputs),
            ConditionType.FieldExists => EvaluateFieldExists(condition, nodeOutputs),
            ConditionType.FieldNotExists => !EvaluateFieldExists(condition, nodeOutputs),
            ConditionType.FieldContains => EvaluateFieldContains(condition, nodeOutputs),
            ConditionType.Default => true, // Default edges handled separately
            ConditionType.And => EvaluateAnd(condition, nodeOutputs, context, edge),
            ConditionType.Or => EvaluateOr(condition, nodeOutputs, context, edge),
            ConditionType.Not => EvaluateNot(condition, nodeOutputs, context, edge),
            ConditionType.FieldStartsWith => EvaluateFieldStartsWith(condition, nodeOutputs),
            ConditionType.FieldEndsWith => EvaluateFieldEndsWith(condition, nodeOutputs),
            ConditionType.FieldMatchesRegex => EvaluateFieldMatchesRegex(condition, nodeOutputs),
            ConditionType.FieldIsEmpty => EvaluateFieldIsEmpty(condition, nodeOutputs),
            ConditionType.FieldIsNotEmpty => !EvaluateFieldIsEmpty(condition, nodeOutputs),
            ConditionType.FieldContainsAny => EvaluateFieldContainsAny(condition, nodeOutputs),
            ConditionType.FieldContainsAll => EvaluateFieldContainsAll(condition, nodeOutputs),

            // Upstream state conditions
            ConditionType.UpstreamOneSuccess => EvaluateUpstreamOneSuccess(context, edge),
            ConditionType.UpstreamAllDone => EvaluateUpstreamAllDone(context, edge),
            ConditionType.UpstreamAllDoneOneSuccess => EvaluateUpstreamAllDoneOneSuccess(context, edge),

            _ => throw new InvalidOperationException($"Unknown condition type: {condition.Type}")
        };
    }

    // ========================================
    // Compound Logic
    // ========================================

    private static void GuardNoDefaultInCompound(IReadOnlyList<EdgeCondition>? conditions, ConditionType parent)
    {
        if (conditions == null) return;
        foreach (var c in conditions)
        {
            if (c.Type == ConditionType.Default)
                throw new InvalidOperationException(
                    $"ConditionType.Default cannot be nested inside {parent}. " +
                    "Default is a graph-level routing concept and has no meaningful semantics as a sub-condition.");
        }
    }

    private static bool EvaluateAnd(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        GuardNoDefaultInCompound(condition.Conditions, ConditionType.And);
        if (condition.Conditions == null || condition.Conditions.Count == 0) return true; // vacuously true
        return condition.Conditions.All(c => Evaluate(c, nodeOutputs));
    }

    private static bool EvaluateAnd(EdgeCondition condition, Dictionary<string, object>? nodeOutputs, IGraphContext context, Edge edge)
    {
        GuardNoDefaultInCompound(condition.Conditions, ConditionType.And);
        if (condition.Conditions == null || condition.Conditions.Count == 0) return true;
        return condition.Conditions.All(c => Evaluate(c, nodeOutputs, context, edge));
    }

    private static bool EvaluateOr(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        GuardNoDefaultInCompound(condition.Conditions, ConditionType.Or);
        if (condition.Conditions == null || condition.Conditions.Count == 0) return false; // vacuously false
        return condition.Conditions.Any(c => Evaluate(c, nodeOutputs));
    }

    private static bool EvaluateOr(EdgeCondition condition, Dictionary<string, object>? nodeOutputs, IGraphContext context, Edge edge)
    {
        GuardNoDefaultInCompound(condition.Conditions, ConditionType.Or);
        if (condition.Conditions == null || condition.Conditions.Count == 0) return false;
        return condition.Conditions.Any(c => Evaluate(c, nodeOutputs, context, edge));
    }

    private static bool EvaluateNot(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        GuardNoDefaultInCompound(condition.Conditions, ConditionType.Not);
        var inner = condition.Conditions?.FirstOrDefault();
        if (inner == null) return true; // NOT nothing = true
        return !Evaluate(inner, nodeOutputs);
    }

    private static bool EvaluateNot(EdgeCondition condition, Dictionary<string, object>? nodeOutputs, IGraphContext context, Edge edge)
    {
        GuardNoDefaultInCompound(condition.Conditions, ConditionType.Not);
        var inner = condition.Conditions?.FirstOrDefault();
        if (inner == null) return true;
        return !Evaluate(inner, nodeOutputs, context, edge);
    }

    // ========================================
    // Field Equality / Comparison
    // ========================================

    private static bool EvaluateFieldEquals(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        if (string.IsNullOrWhiteSpace(condition.Field) || nodeOutputs == null)
            return false;

        if (!nodeOutputs.TryGetValue(condition.Field, out var fieldValue))
            return false;

        // Unwrap JsonElement for the field value
        var unwrapped = UnwrapScalar(fieldValue);

        // Handle null comparisons
        if (unwrapped == null && condition.Value == null)
            return true;

        if (unwrapped == null || condition.Value == null)
            return false;

        // Try direct equality
        if (unwrapped.Equals(condition.Value))
            return true;

        // Try string comparison
        if (unwrapped.ToString() == condition.Value.ToString())
            return true;

        return false;
    }

    private static bool EvaluateFieldGreaterThan(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        if (string.IsNullOrWhiteSpace(condition.Field) || nodeOutputs == null)
            return false;

        if (!nodeOutputs.TryGetValue(condition.Field, out var fieldValue))
            return false;

        if (fieldValue == null || condition.Value == null)
            return false;

        var unwrapped = UnwrapScalar(fieldValue);

        // Try numeric comparison
        if (TryGetNumericValue(unwrapped, out var fieldNum) &&
            TryGetNumericValue(condition.Value, out var conditionNum))
        {
            return fieldNum > conditionNum;
        }

        // Try string comparison
        var fieldStr = unwrapped?.ToString();
        var conditionStr = condition.Value?.ToString();
        if (fieldStr != null && conditionStr != null)
        {
            return string.Compare(fieldStr, conditionStr, StringComparison.Ordinal) > 0;
        }

        return false;
    }

    private static bool EvaluateFieldLessThan(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        if (string.IsNullOrWhiteSpace(condition.Field) || nodeOutputs == null)
            return false;

        if (!nodeOutputs.TryGetValue(condition.Field, out var fieldValue))
            return false;

        if (fieldValue == null || condition.Value == null)
            return false;

        var unwrapped = UnwrapScalar(fieldValue);

        // Try numeric comparison
        if (TryGetNumericValue(unwrapped, out var fieldNum) &&
            TryGetNumericValue(condition.Value, out var conditionNum))
        {
            return fieldNum < conditionNum;
        }

        // Try string comparison
        var fieldStr = unwrapped?.ToString();
        var conditionStr = condition.Value?.ToString();
        if (fieldStr != null && conditionStr != null)
        {
            return string.Compare(fieldStr, conditionStr, StringComparison.Ordinal) < 0;
        }

        return false;
    }

    private static bool EvaluateFieldExists(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        if (string.IsNullOrWhiteSpace(condition.Field) || nodeOutputs == null)
            return false;

        return nodeOutputs.ContainsKey(condition.Field) && nodeOutputs[condition.Field] != null;
    }

    private static bool EvaluateFieldContains(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        if (string.IsNullOrWhiteSpace(condition.Field) || nodeOutputs == null)
            return false;

        if (!nodeOutputs.TryGetValue(condition.Field, out var fieldValue))
            return false;

        if (fieldValue == null || condition.Value == null)
            return false;

        var unwrapped = UnwrapScalar(fieldValue);

        // String contains
        if (unwrapped is string fieldStr && condition.Value is string conditionStr)
        {
            return fieldStr.Contains(conditionStr, StringComparison.OrdinalIgnoreCase);
        }

        // Collection contains
        if (unwrapped is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item != null && item.Equals(condition.Value))
                    return true;
            }
        }

        return false;
    }

    // ========================================
    // Advanced String Conditions
    // ========================================

    private static bool EvaluateFieldStartsWith(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        var fieldStr = GetFieldString(condition, nodeOutputs);
        if (fieldStr == null || condition.Value == null) return false;
        return fieldStr.StartsWith(condition.Value.ToString()!, StringComparison.Ordinal);
    }

    private static bool EvaluateFieldEndsWith(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        var fieldStr = GetFieldString(condition, nodeOutputs);
        if (fieldStr == null || condition.Value == null) return false;
        return fieldStr.EndsWith(condition.Value.ToString()!, StringComparison.Ordinal);
    }

    private static bool EvaluateFieldMatchesRegex(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        var fieldStr = GetFieldString(condition, nodeOutputs);
        if (fieldStr == null || condition.Value == null) return false;

        var pattern = condition.Value.ToString()!;
        var options = ParseRegexOptions(condition.RegexOptions);
        var regex = _regexCache.GetOrAdd((pattern, options), key =>
            new Regex(key.Pattern, key.Options, _regexMatchTimeout));

        try
        {
            return regex.IsMatch(fieldStr);
        }
        catch (RegexMatchTimeoutException)
        {
            // Treat timeout as non-match; caller may observe via diagnostic events
            return false;
        }
    }

    private static bool EvaluateFieldIsEmpty(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        if (string.IsNullOrWhiteSpace(condition.Field) || nodeOutputs == null)
            return true; // field absent → empty

        if (!nodeOutputs.TryGetValue(condition.Field, out var fieldValue))
            return true;

        var unwrapped = UnwrapScalar(fieldValue);
        if (unwrapped == null) return true;
        return string.IsNullOrWhiteSpace(unwrapped.ToString());
    }

    // ========================================
    // Collection Conditions
    // ========================================

    private static bool EvaluateFieldContainsAny(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        if (string.IsNullOrWhiteSpace(condition.Field) || nodeOutputs == null)
            return false;

        if (!nodeOutputs.TryGetValue(condition.Field, out var fieldValue))
            return false;

        var fieldItems = UnwrapCollection(fieldValue).ToList();
        var conditionItems = UnwrapCollection(condition.Value).ToList();

        if (conditionItems.Count == 0) return false;

        return conditionItems.Any(cv =>
            fieldItems.Any(fv => StringEquals(fv, cv)));
    }

    private static bool EvaluateFieldContainsAll(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        if (string.IsNullOrWhiteSpace(condition.Field) || nodeOutputs == null)
            return false;

        if (!nodeOutputs.TryGetValue(condition.Field, out var fieldValue))
            return false;

        var fieldItems = UnwrapCollection(fieldValue).ToList();
        var conditionItems = UnwrapCollection(condition.Value).ToList();

        if (conditionItems.Count == 0) return true; // vacuously true

        return conditionItems.All(cv =>
            fieldItems.Any(fv => StringEquals(fv, cv)));
    }

    // ========================================
    // Upstream State Condition Evaluation
    // ========================================

    /// <summary>
    /// Evaluate UpstreamOneSuccess: At least one upstream must have succeeded.
    /// </summary>
    private static bool EvaluateUpstreamOneSuccess(IGraphContext context, Edge edge)
    {
        var upstreamNodes = GetUpstreamNodes(context.Graph, edge.To);

        // At least one upstream must have succeeded
        foreach (var upstream in upstreamNodes)
        {
            if (!context.IsNodeComplete(upstream.Id))
                continue; // Still waiting

            var resultChannel = context.Channels[$"node_result:{upstream.Id}"];
            var result = resultChannel.Get<NodeExecutionResult>();
            if (result is NodeExecutionResult.Success)
            {
                return true; // Found a success - traverse
            }
        }

        // Check if all completed (no success found)
        if (upstreamNodes.All(u => context.IsNodeComplete(u.Id)))
            return false; // All done, none succeeded - don't traverse

        // Still waiting for some upstreams
        return false;
    }

    /// <summary>
    /// Evaluate UpstreamAllDone: All upstreams must be complete (any state).
    /// </summary>
    private static bool EvaluateUpstreamAllDone(IGraphContext context, Edge edge)
    {
        var upstreamNodes = GetUpstreamNodes(context.Graph, edge.To);

        // All upstreams must be complete (any state)
        return upstreamNodes.All(u => context.IsNodeComplete(u.Id));
    }

    /// <summary>
    /// Evaluate UpstreamAllDoneOneSuccess: All must complete AND at least one must succeed.
    /// </summary>
    private static bool EvaluateUpstreamAllDoneOneSuccess(IGraphContext context, Edge edge)
    {
        var upstreamNodes = GetUpstreamNodes(context.Graph, edge.To);

        // All must complete AND at least one must succeed
        if (!upstreamNodes.All(u => context.IsNodeComplete(u.Id)))
            return false;

        return upstreamNodes.Any(u =>
        {
            var resultChannel = context.Channels[$"node_result:{u.Id}"];
            var result = resultChannel.Get<NodeExecutionResult>();
            return result is NodeExecutionResult.Success;
        });
    }

    /// <summary>
    /// Get all upstream nodes (nodes with edges pointing to targetNodeId).
    /// </summary>
    private static IReadOnlyList<Node> GetUpstreamNodes(Abstractions.Graph.Graph graph, string targetNodeId)
    {
        var upstreamIds = graph.Edges
            .Where(e => e.To == targetNodeId)
            .Select(e => e.From)
            .Distinct()
            .ToList();

        return graph.Nodes
            .Where(n => upstreamIds.Contains(n.Id))
            .ToList();
    }

    // ========================================
    // Helper Methods
    // ========================================

    private static bool TryGetNumericValue(object? value, out double result)
    {
        result = 0;

        if (value == null)
            return false;

        switch (value)
        {
            case double d: result = d; return true;
            case float f: result = f; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case decimal dec: result = (double)dec; return true;
            case short s: result = s; return true;
            case byte b: result = b; return true;
            case JsonElement je:
                if (je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out var jd))
                {
                    result = jd;
                    return true;
                }
                return false;
            case string str:
                return double.TryParse(str, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out result);
            default:
                return false;
        }
    }

    /// <summary>
    /// Unwrap a JsonElement scalar to its underlying CLR type, or return the value as-is.
    /// </summary>
    private static object? UnwrapScalar(object? value)
    {
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.TryGetInt64(out var l) ? (object)l : je.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => je.GetRawText()
            };
        }
        return value;
    }

    /// <summary>
    /// Unwrap a value to an enumerable of string-comparable items.
    /// Handles JsonElement arrays, IEnumerable&lt;object&gt;, and scalar values.
    /// </summary>
    private static IEnumerable<object?> UnwrapCollection(object? value)
    {
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            return je.EnumerateArray().Select(e => (object?)UnwrapScalar(e));
        }

        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            return enumerable.Cast<object?>();
        }

        return value != null ? new[] { value } : [];
    }

    /// <summary>
    /// Get the string value of a field, unwrapping JsonElement if needed.
    /// Returns null if field is absent or not a string.
    /// </summary>
    private static string? GetFieldString(EdgeCondition condition, Dictionary<string, object>? nodeOutputs)
    {
        if (string.IsNullOrWhiteSpace(condition.Field) || nodeOutputs == null)
            return null;

        if (!nodeOutputs.TryGetValue(condition.Field, out var fieldValue))
            return null;

        return UnwrapScalar(fieldValue)?.ToString();
    }

    /// <summary>
    /// Compare two values for equality using string representation for cross-type comparison.
    /// </summary>
    private static bool StringEquals(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Equals(b)) return true;
        return a.ToString() == b?.ToString();
    }

    /// <summary>
    /// Parse a comma-separated RegexOptions string (e.g. "IgnoreCase,Multiline") into flags.
    /// Returns RegexOptions.None for null or empty input.
    /// </summary>
    private static RegexOptions ParseRegexOptions(string? regexOptionsStr)
    {
        if (string.IsNullOrWhiteSpace(regexOptionsStr))
            return RegexOptions.None;

        var result = RegexOptions.None;
        foreach (var part in regexOptionsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<RegexOptions>(part, ignoreCase: true, out var flag))
                result |= flag;
        }
        return result;
    }
}
