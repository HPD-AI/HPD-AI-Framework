using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HPD.Agent.Middleware;
using HPD.OpenApi.Core;

namespace HPD.Agent.OpenApi;

/// <summary>
/// Optimizes OpenAPI function responses before the LLM sees them, reducing token consumption.
/// Reads optimization hints from function <c>AdditionalProperties</c> set by
/// <see cref="OpenApiFunctionFactory"/>.
///
/// Follows the ErrorFormattingMiddleware pattern: mutates <c>context.Result</c> in
/// <see cref="AfterFunctionAsync"/>. Only acts on functions that have the
/// <c>"openapi.operationId"</c> metadata key.
///
/// Auto-registered in AgentBuilder when OpenAPI functions are present.
///
/// DefaultMaxLength = 4000 is a safety net — HistoryReductionMiddleware manages the
/// overall context window budget; this is per-response protection only.
/// </summary>
public sealed class ResponseOptimizationMiddleware : IAgentMiddleware
{
    /// <summary>
    /// Default maximum character length for serialized OpenAPI responses.
    /// Applied when no per-function MaxLength is set in ResponseOptimizationConfig.
    /// </summary>
    public int DefaultMaxLength { get; set; } = 4000;

    private static readonly JsonSerializerOptions s_serializeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken ct)
    {
        if (!context.IsSuccess) return Task.CompletedTask;

        var props = context.Function?.AdditionalProperties;
        if (props?.ContainsKey("openapi.operationId") != true) return Task.CompletedTask;

        if (context.Result is not OpenApiOperationResponse response) return Task.CompletedTask;

        // Unwrap Content, apply dataField extraction / field filtering / truncation,
        // then re-serialize the whole envelope so the LLM sees:
        // { "content": <processed>, "status": 200, "expectedSchema": {...} }
        response.Content = ProcessContent(response.Content, props);
        context.Result = JsonSerializer.Serialize(response, s_serializeOptions);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs content through the full pipeline:
    /// dataField extraction → field filtering → length truncation.
    /// </summary>
    private object? ProcessContent(object? content, IReadOnlyDictionary<string, object?> props)
    {
        if (content is null) return null;

        if (content is not JsonElement json)
        {
            // Non-JSON content (plain string): apply length truncation only
            var str = content.ToString() ?? "";
            var maxLen = GetMaxLength(props);
            return str.Length > maxLen ? str[..maxLen] + "..." : str;
        }

        return ApplyJsonTransforms(json, props);
    }

    /// <summary>
    /// Applies dataField extraction, field filtering, and length truncation to a JSON element.
    /// Returns a serialized string.
    /// </summary>
    private string ApplyJsonTransforms(JsonElement json, IReadOnlyDictionary<string, object?> props)
    {
        // Extract from data envelope field if configured (e.g., Stripe { "data": [...] })
        if (props.TryGetValue("openapi.response.dataField", out var dataFieldObj)
            && dataFieldObj is string dataField && !string.IsNullOrEmpty(dataField))
        {
            json = ExtractDataField(json, dataField);
        }

        // Apply field whitelist or blacklist
        if (props.TryGetValue("openapi.response.fieldsToInclude", out var includeObj)
            && includeObj is IList<string> fieldsToInclude && fieldsToInclude.Count > 0)
        {
            json = FilterFields(json, fieldsToInclude, include: true);
        }
        else if (props.TryGetValue("openapi.response.fieldsToExclude", out var excludeObj)
            && excludeObj is IList<string> fieldsToExclude && fieldsToExclude.Count > 0)
        {
            json = FilterFields(json, fieldsToExclude, include: false);
        }

        var serialized = json.ToString();
        var maxLength = GetMaxLength(props);
        if (serialized.Length > maxLength)
            serialized = serialized[..maxLength] + "...";

        return serialized;
    }

    private int GetMaxLength(IReadOnlyDictionary<string, object?> props)
    {
        if (props.TryGetValue("openapi.response.maxLength", out var maxLenObj)
            && maxLenObj is int maxLen && maxLen > 0)
            return maxLen;
        return DefaultMaxLength;
    }

    private static JsonElement ExtractDataField(JsonElement json, string dataField)
    {
        var segments = dataField.Split('.');
        var current = json;
        foreach (var segment in segments)
        {
            if (current.ValueKind == JsonValueKind.Object
                && current.TryGetProperty(segment, out var nested))
                current = nested;
            else
                return json; // Can't navigate further — return original
        }
        return current;
    }

    private static JsonElement FilterFields(JsonElement json, IList<string> fields, bool include)
    {
        if (json.ValueKind == JsonValueKind.Array)
        {
            var arrayBuilder = new StringBuilder("[");
            var first = true;
            foreach (var element in json.EnumerateArray())
            {
                if (!first) arrayBuilder.Append(',');
                arrayBuilder.Append(FilterSingleObject(element, fields, include));
                first = false;
            }
            arrayBuilder.Append(']');
            return JsonDocument.Parse(arrayBuilder.ToString()).RootElement.Clone();
        }

        if (json.ValueKind == JsonValueKind.Object)
        {
            var filtered = FilterSingleObject(json, fields, include);
            return JsonDocument.Parse(filtered).RootElement.Clone();
        }

        return json;
    }

    private static string FilterSingleObject(JsonElement obj, IList<string> fields, bool include)
    {
        if (obj.ValueKind != JsonValueKind.Object) return obj.ToString();

        var result = new JsonObject();
        foreach (var prop in obj.EnumerateObject())
        {
            var shouldInclude = include ? fields.Contains(prop.Name) : !fields.Contains(prop.Name);
            if (shouldInclude)
                result.Add(prop.Name, JsonNode.Parse(prop.Value.GetRawText()));
        }
        return result.ToJsonString();
    }
}
