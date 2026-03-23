// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Evaluators.Deterministic;

/// <summary>
/// BooleanMetric — output conforms to the provided JSON schema.
/// Parses the response text as JSON and validates it against the schema using
/// System.Text.Json. Returns false if the output cannot be parsed as valid JSON
/// or if it fails the schema check.
/// </summary>
public sealed class SchemaConformanceEvaluator : HpdDeterministicEvaluatorBase
{
    private readonly JsonElement _schema;

    /// <param name="schema">The JSON schema to validate against, as a JSON string.</param>
    public SchemaConformanceEvaluator(string schema)
    {
        _schema = JsonDocument.Parse(schema).RootElement.Clone();
    }

    /// <param name="schema">The JSON schema to validate against, as a pre-parsed element.</param>
    public SchemaConformanceEvaluator(JsonElement schema)
    {
        _schema = schema.Clone();
    }

    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Schema Conformance"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Schema Conformance");
        var text = modelResponse.Text ?? string.Empty;

        JsonElement document;
        try
        {
            document = JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            metric.Value = false;
            metric.Reason = $"Output is not valid JSON: {ex.Message}";
            metric.MarkAsHpdBuiltIn();
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        var violations = ValidateAgainstSchema(document, _schema);
        metric.Value = violations.Count == 0;
        metric.Reason = violations.Count == 0
            ? "Output conforms to schema."
            : $"Schema violations: {string.Join("; ", violations)}";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }

    /// <summary>
    /// Validates a JSON element against a JSON schema subset.
    /// Supports: type, required, properties (type checking), minLength, maxLength,
    /// minimum, maximum, enum.
    /// Returns a list of human-readable violation messages (empty = valid).
    /// </summary>
    private static List<string> ValidateAgainstSchema(JsonElement value, JsonElement schema, string path = "$")
    {
        var violations = new List<string>();

        // type check
        if (schema.TryGetProperty("type", out var typeProp))
        {
            var expectedType = typeProp.GetString();
            bool typeMatch = expectedType switch
            {
                "object" => value.ValueKind == JsonValueKind.Object,
                "array" => value.ValueKind == JsonValueKind.Array,
                "string" => value.ValueKind == JsonValueKind.String,
                "number" => value.ValueKind is JsonValueKind.Number,
                "integer" => value.ValueKind == JsonValueKind.Number &&
                             value.TryGetInt64(out _),
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "null" => value.ValueKind == JsonValueKind.Null,
                _ => true,
            };
            if (!typeMatch)
                violations.Add($"{path}: expected type '{expectedType}', got '{value.ValueKind}'");
        }

        // required fields (only meaningful for objects)
        if (schema.TryGetProperty("required", out var requiredProp) &&
            requiredProp.ValueKind == JsonValueKind.Array &&
            value.ValueKind == JsonValueKind.Object)
        {
            foreach (var req in requiredProp.EnumerateArray())
            {
                var fieldName = req.GetString();
                if (fieldName is not null && !value.TryGetProperty(fieldName, out _))
                    violations.Add($"{path}: required field '{fieldName}' is missing");
            }
        }

        // properties — validate each named property recursively
        if (schema.TryGetProperty("properties", out var propertiesProp) &&
            propertiesProp.ValueKind == JsonValueKind.Object &&
            value.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in propertiesProp.EnumerateObject())
            {
                if (value.TryGetProperty(prop.Name, out var childValue))
                {
                    var childViolations = ValidateAgainstSchema(
                        childValue, prop.Value, $"{path}.{prop.Name}");
                    violations.AddRange(childViolations);
                }
            }
        }

        // string constraints
        if (value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString() ?? string.Empty;
            if (schema.TryGetProperty("minLength", out var minLen) &&
                str.Length < minLen.GetInt32())
                violations.Add($"{path}: string length {str.Length} < minLength {minLen.GetInt32()}");

            if (schema.TryGetProperty("maxLength", out var maxLen) &&
                str.Length > maxLen.GetInt32())
                violations.Add($"{path}: string length {str.Length} > maxLength {maxLen.GetInt32()}");
        }

        // numeric constraints
        if (value.ValueKind == JsonValueKind.Number)
        {
            var num = value.GetDouble();
            if (schema.TryGetProperty("minimum", out var min) && num < min.GetDouble())
                violations.Add($"{path}: value {num} < minimum {min.GetDouble()}");

            if (schema.TryGetProperty("maximum", out var max) && num > max.GetDouble())
                violations.Add($"{path}: value {num} > maximum {max.GetDouble()}");
        }

        // enum check
        if (schema.TryGetProperty("enum", out var enumProp) &&
            enumProp.ValueKind == JsonValueKind.Array)
        {
            var valueStr = value.ToString();
            bool inEnum = enumProp.EnumerateArray()
                .Any(e => string.Equals(e.ToString(), valueStr, StringComparison.Ordinal));
            if (!inEnum)
                violations.Add($"{path}: value '{valueStr}' is not in enum");
        }

        return violations;
    }
}

/// <summary>
/// NumericMetric 0–1 — fraction of specified fields that are present and non-null
/// in the JSON output. Returns 1.0 if all fields are present, 0.0 if none are.
/// Empty field list vacuously returns 1.0.
/// </summary>
public sealed class FieldCompletenessEvaluator(string[] fields) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Field Completeness"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new NumericMetric("Field Completeness");

        if (fields.Length == 0)
        {
            metric.Value = 1.0;
            metric.Reason = "No fields specified — vacuously complete.";
            metric.MarkAsHpdBuiltIn();
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        var text = modelResponse.Text ?? string.Empty;
        JsonElement doc;
        try
        {
            doc = JsonDocument.Parse(text).RootElement;
        }
        catch (JsonException)
        {
            metric.Value = 0.0;
            metric.Reason = "Output is not valid JSON — cannot check field completeness.";
            metric.MarkAsHpdBuiltIn();
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        if (doc.ValueKind != JsonValueKind.Object)
        {
            metric.Value = 0.0;
            metric.Reason = "Output is not a JSON object — cannot check field completeness.";
            metric.MarkAsHpdBuiltIn();
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        int present = 0;
        var missing = new List<string>();
        foreach (var field in fields)
        {
            if (doc.TryGetProperty(field, out var prop) &&
                prop.ValueKind != JsonValueKind.Null)
            {
                present++;
            }
            else
            {
                missing.Add(field);
            }
        }

        metric.Value = Math.Round((double)present / fields.Length, 2);
        metric.Reason = missing.Count == 0
            ? $"All {fields.Length} fields are present and non-null."
            : $"{present}/{fields.Length} fields present. Missing: {string.Join(", ", missing)}.";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>
/// BooleanMetric — a specific field in the JSON output matches the expected value.
/// Compares the field value as a JSON string against the expected string using
/// ordinal case-insensitive comparison. Returns false if the output is not valid JSON,
/// not an object, the field is missing, or the value does not match.
/// </summary>
public sealed class FieldAccuracyEvaluator(string field, string expectedValue) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Field Accuracy"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Field Accuracy");
        var text = modelResponse.Text ?? string.Empty;

        JsonElement doc;
        try
        {
            doc = JsonDocument.Parse(text).RootElement;
        }
        catch (JsonException)
        {
            metric.Value = false;
            metric.Reason = "Output is not valid JSON.";
            metric.MarkAsHpdBuiltIn();
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        if (doc.ValueKind != JsonValueKind.Object)
        {
            metric.Value = false;
            metric.Reason = "Output is not a JSON object.";
            metric.MarkAsHpdBuiltIn();
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        if (!doc.TryGetProperty(field, out var prop))
        {
            metric.Value = false;
            metric.Reason = $"Field '{field}' is not present in the output.";
            metric.MarkAsHpdBuiltIn();
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        // Compare the raw JSON value string against the expected value.
        // For string fields, GetString() gives the unquoted value.
        // For other types, ToString() gives the JSON representation.
        var actualValue = prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : prop.ToString();

        metric.Value = string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase);
        metric.Reason = metric.Value == true
            ? $"Field '{field}' matches expected value '{expectedValue}'."
            : $"Field '{field}' is '{actualValue}', expected '{expectedValue}'.";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}
