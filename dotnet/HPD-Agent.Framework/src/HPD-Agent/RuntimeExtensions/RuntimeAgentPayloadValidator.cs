using System.Text;
using System.Text.Json;
using Json.Schema;

namespace HPD.Agent;

internal static class RuntimeAgentPayloadValidator
{
    private static readonly EvaluationOptions EvaluationOptions = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true
    };

    public static JsonSchema Compile(
        JsonElement schema,
        RuntimeAgentExtensionLimits limits)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Runtime extension payload schema must be an object.");
        ValidateBounds(schema, limits, "schema", limits.MaxSchemaBytes);
        ValidateReferences(schema);
        try
        {
            return JsonSchema.FromText(schema.GetRawText());
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new InvalidDataException("Runtime extension payload schema is invalid.", exception);
        }
    }

    public static bool TryValidate(
        JsonSchema schema,
        JsonElement payload,
        RuntimeAgentExtensionLimits limits,
        out string? error)
    {
        ValidateBounds(payload, limits, "payload", limits.MaxPayloadBytes);
        var result = schema.Evaluate(payload, EvaluationOptions);
        if (result.IsValid)
        {
            error = null;
            return true;
        }
        var messages = Flatten(result)
            .SelectMany(static item => item.Errors ?? new Dictionary<string, string>())
            .Select(static item => $"{item.Key}: {item.Value}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        error = messages.Length == 0
            ? "Runtime extension payload does not satisfy its schema."
            : string.Join("; ", messages);
        return false;
    }

    private static IEnumerable<EvaluationResults> Flatten(EvaluationResults result)
    {
        yield return result;
        if (result.Details is null)
            yield break;
        foreach (var detail in result.Details)
        foreach (var nested in Flatten(detail))
            yield return nested;
    }

    private static void ValidateBounds(
        JsonElement value,
        RuntimeAgentExtensionLimits limits,
        string kind,
        int maximumBytes)
    {
        var bytes = Encoding.UTF8.GetByteCount(value.GetRawText());
        if (bytes > maximumBytes)
            throw new InvalidDataException($"Runtime extension {kind} is {bytes} bytes; maximum is {maximumBytes}.");
        var nodes = 0;
        ValidateBounds(value, limits, kind, depth: 0, ref nodes);
    }

    private static void ValidateBounds(
        JsonElement value,
        RuntimeAgentExtensionLimits limits,
        string kind,
        int depth,
        ref int nodes)
    {
        nodes++;
        if (depth > limits.MaxDepth)
            throw new InvalidDataException($"Runtime extension {kind} exceeds the maximum depth.");
        if (nodes > limits.MaxNodes)
            throw new InvalidDataException($"Runtime extension {kind} exceeds the maximum node count.");
        if (value.ValueKind == JsonValueKind.Object)
        {
            var count = value.EnumerateObject().Count();
            if (count > limits.MaxProperties)
                throw new InvalidDataException($"Runtime extension {kind} exceeds the property limit.");
            foreach (var property in value.EnumerateObject())
                ValidateBounds(property.Value, limits, kind, depth + 1, ref nodes);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var count = value.GetArrayLength();
            if (count > limits.MaxArrayItems)
                throw new InvalidDataException($"Runtime extension {kind} exceeds the array item limit.");
            foreach (var item in value.EnumerateArray())
                ValidateBounds(item, limits, kind, depth + 1, ref nodes);
        }
        else if (value.ValueKind == JsonValueKind.String &&
                 value.GetString()!.Length > limits.MaxStringLength)
        {
            throw new InvalidDataException($"Runtime extension {kind} exceeds the string length limit.");
        }
    }

    private static void ValidateReferences(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("$ref", out var reference) &&
                reference.GetString() is { } referenceText &&
                !referenceText.StartsWith('#'))
            {
                throw new InvalidDataException(
                    "Runtime extension schemas may reference only fragments in the same schema.");
            }
            foreach (var property in value.EnumerateObject())
                ValidateReferences(property.Value);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                ValidateReferences(item);
        }
    }
}
