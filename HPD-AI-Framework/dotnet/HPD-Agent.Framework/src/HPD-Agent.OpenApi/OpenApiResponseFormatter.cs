using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HPD.OpenApi.Core;

namespace HPD.Agent.OpenApi;

internal static class OpenApiResponseFormatter
{
    private const int DefaultMaxLength = 4000;

    public static string FormatSuccess(
        OpenApiOperationResponse response,
        ResponseOptimizationConfig? optimization)
    {
        var content = ProcessContent(response.Content, optimization);
        var envelope = new JsonObject
        {
            ["content"] = ToJsonNode(content),
            ["status"] = response.StatusCode
        };

        if (response.ExpectedSchema.HasValue)
            envelope["expectedSchema"] = JsonNode.Parse(response.ExpectedSchema.Value.GetRawText());

        return envelope.ToJsonString();
    }

    public static string FormatError(OpenApiErrorResponse error)
    {
        var envelope = new JsonObject
        {
            ["error"] = true,
            ["status"] = error.StatusCode
        };

        if (!string.IsNullOrWhiteSpace(error.ReasonPhrase))
            envelope["reason"] = error.ReasonPhrase;
        if (!string.IsNullOrWhiteSpace(error.UserMessage))
            envelope["message"] = error.UserMessage;
        if (!string.IsNullOrWhiteSpace(error.Body))
            envelope["body"] = error.Body;

        return envelope.ToJsonString();
    }

    private static JsonNode? ToJsonNode(object? value)
    {
        return value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            JsonElement element => JsonNode.Parse(element.GetRawText()),
            string str => JsonValue.Create(str),
            bool boolean => JsonValue.Create(boolean),
            byte number => JsonValue.Create(number),
            sbyte number => JsonValue.Create(number),
            short number => JsonValue.Create(number),
            ushort number => JsonValue.Create(number),
            int number => JsonValue.Create(number),
            uint number => JsonValue.Create(number),
            long number => JsonValue.Create(number),
            ulong number => JsonValue.Create(number),
            float number => JsonValue.Create(number),
            double number => JsonValue.Create(number),
            decimal number => JsonValue.Create(number),
            _ => JsonValue.Create(value.ToString())
        };
    }

    private static object? ProcessContent(object? content, ResponseOptimizationConfig? optimization)
    {
        if (content is null) return null;

        if (content is not JsonElement json)
        {
            var text = content.ToString() ?? string.Empty;
            var maxLength = GetMaxLength(optimization);
            return text.Length > maxLength ? text[..maxLength] + "..." : text;
        }

        return ApplyJsonTransforms(json, optimization);
    }

    private static string ApplyJsonTransforms(JsonElement json, ResponseOptimizationConfig? optimization)
    {
        if (!string.IsNullOrEmpty(optimization?.DataField))
        {
            json = ExtractDataField(json, optimization.DataField);
        }

        if (optimization?.FieldsToInclude is { Count: > 0 } fieldsToInclude)
        {
            json = FilterFields(json, fieldsToInclude, include: true);
        }
        else if (optimization?.FieldsToExclude is { Count: > 0 } fieldsToExclude)
        {
            json = FilterFields(json, fieldsToExclude, include: false);
        }

        var serialized = json.ToString();
        var maxLength = GetMaxLength(optimization);
        return serialized.Length > maxLength ? serialized[..maxLength] + "..." : serialized;
    }

    private static int GetMaxLength(ResponseOptimizationConfig? optimization)
    {
        return optimization?.MaxLength > 0
            ? optimization.MaxLength
            : DefaultMaxLength;
    }

    private static JsonElement ExtractDataField(JsonElement json, string dataField)
    {
        var segments = dataField.Split('.');
        var current = json;
        foreach (var segment in segments)
        {
            if (current.ValueKind == JsonValueKind.Object
                && current.TryGetProperty(segment, out var nested))
            {
                current = nested;
            }
            else
            {
                return json;
            }
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
