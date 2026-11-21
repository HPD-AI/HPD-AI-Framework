using System.Text.Json;
using HPD.OpenApi.Core.Model;

namespace HPD.Agent.OpenApi;

/// <summary>Checks source input schemas before relying on the reader's normalized projection.</summary>
internal static class OpenApiSourceInspection
{
    private static readonly HashSet<string> UnsupportedKeywords = new(StringComparer.Ordinal)
    {
        "$id", "$anchor", "$dynamicAnchor", "$dynamicRef", "$recursiveRef", "$recursiveAnchor",
        "$vocabulary", "unevaluatedProperties", "unevaluatedItems", "prefixItems", "dependentSchemas",
        "dependentRequired", "if", "then", "else", "contains", "minContains", "maxContains",
        "propertyNames", "patternProperties", "contentSchema"
    };

    internal static void Validate(JsonElement document, RestApiOperation operation)
    {
        if (document.ValueKind == JsonValueKind.Undefined)
            return; // Explicitly constructed operation models have no source document.
        if (document.ValueKind != JsonValueKind.Object ||
            !document.TryGetProperty("paths", out var paths) ||
            !paths.TryGetProperty(operation.Path, out var path) ||
            !path.TryGetProperty(operation.Method.Method.ToLowerInvariant(), out var sourceOperation))
            throw new InvalidOperationException("The original OpenAPI operation could not be located for input inspection.");

        var active = new HashSet<string>(StringComparer.Ordinal);
        InspectParameters(document, path, active);
        InspectParameters(document, sourceOperation, active);
        if (sourceOperation.TryGetProperty("requestBody", out var body))
        {
            body = Resolve(document, body, "requestBody");
            if (!body.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Unsupported request body: content is missing or invalid.");
            var mediaTypes = content.EnumerateObject().ToArray();
            if (mediaTypes.Length != 1 ||
                !(string.Equals(mediaTypes[0].Name, "application/json", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(mediaTypes[0].Name, "text/plain", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Unsupported request body content types: exactly one application/json or text/plain representation is required.");
            var media = mediaTypes[0];
            if (media.Value.TryGetProperty("encoding", out _))
                throw new InvalidOperationException($"Unsupported request body encoding for '{media.Name}'.");
            if (media.Value.TryGetProperty("schema", out var schema))
                InspectSchema(document, schema, active, $"requestBody.content.{media.Name}.schema", rootBody: true);
        }
    }

    private static void InspectParameters(JsonElement document, JsonElement owner, HashSet<string> active)
    {
        if (!owner.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Array)
            return;
        foreach (var parameter in parameters.EnumerateArray())
        {
            var resolved = Resolve(document, parameter, "parameter");
            var name = resolved.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : "(unnamed)";
            var location = resolved.TryGetProperty("in", out var locationValue) ? locationValue.GetString() : null;
            if (location == "cookie")
                throw new InvalidOperationException($"Cookie parameter '{name}' is not supported by the HTTP mapping.");
            if (resolved.TryGetProperty("content", out _))
                throw new InvalidOperationException($"Content-based parameter '{name}' is not supported by the HTTP mapping.");
            if (resolved.TryGetProperty("allowReserved", out var allowReserved) && allowReserved.ValueKind == JsonValueKind.True)
                throw new InvalidOperationException($"Parameter '{name}' uses unsupported allowReserved encoding.");
            if (resolved.TryGetProperty("style", out var styleValue))
            {
                var style = styleValue.GetString();
                var expected = location == "query" ? "form" : location is "path" or "header" ? "simple" : null;
                if (expected is null || !string.Equals(style, expected, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Parameter '{name}' uses unsupported style '{style}'.");
            }
            if (resolved.TryGetProperty("schema", out var schema))
            {
                InspectSchema(document, schema, active, "parameter.schema", rootBody: false);
                var actualSchema = Resolve(document, schema, "parameter.schema");
                if (actualSchema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String &&
                    (type.GetString() == "object" || (type.GetString() == "array" && location != "query")))
                    throw new InvalidOperationException($"Parameter '{name}' uses an unsupported {location} collection encoding.");
            }
        }
    }

    private static void InspectSchema(JsonElement document, JsonElement schema, HashSet<string> active, string location, bool rootBody)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Unsupported schema at {location}: expected an object.");
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var pointer = reference.ValueKind == JsonValueKind.String ? reference.GetString() : null;
            if (pointer is null || !active.Add(pointer))
                throw new InvalidOperationException($"Unsupported recursive or invalid reference at {location}.");
            try { InspectSchema(document, Resolve(document, schema, location), active, location, rootBody); }
            finally { active.Remove(pointer); }
            return;
        }
        foreach (var keyword in schema.EnumerateObject())
        {
            if (UnsupportedKeywords.Contains(keyword.Name) ||
                (keyword.Name == "type" && keyword.Value.ValueKind == JsonValueKind.Array) ||
                (rootBody && keyword.Name is "oneOf" or "anyOf" or "allOf"))
                throw new InvalidOperationException($"Unsupported input schema keyword '{keyword.Name}' at {location}.");
            if (keyword.Name is "properties" or "$defs" or "definitions")
            {
                if (keyword.Value.ValueKind == JsonValueKind.Object)
                    foreach (var property in keyword.Value.EnumerateObject())
                        InspectSchema(document, property.Value, active, $"{location}.{keyword.Name}.{property.Name}", false);
            }
            else if (keyword.Name is "items" or "additionalProperties" or "not")
            {
                if (keyword.Value.ValueKind == JsonValueKind.Object)
                    InspectSchema(document, keyword.Value, active, $"{location}.{keyword.Name}", false);
            }
            else if (keyword.Name is "oneOf" or "anyOf" or "allOf")
            {
                if (keyword.Value.ValueKind == JsonValueKind.Array)
                    foreach (var branch in keyword.Value.EnumerateArray())
                        InspectSchema(document, branch, active, $"{location}.{keyword.Name}", false);
            }
        }
    }

    private static JsonElement Resolve(JsonElement document, JsonElement value, string location)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$ref", out var reference))
        {
            if (value.EnumerateObject().Any(property => property.Name is not ("$ref" or "summary" or "description")))
                throw new InvalidOperationException($"Unsupported reference siblings at {location}.");
            var pointer = reference.ValueKind == JsonValueKind.String ? reference.GetString() : null;
            if (pointer is null || !pointer.StartsWith("#/", StringComparison.Ordinal) || !seen.Add(pointer))
                throw new InvalidOperationException($"Unsupported or recursive reference at {location}.");
            var target = document;
            foreach (var segment in pointer[2..].Split('/'))
            {
                var name = segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
                if (target.ValueKind != JsonValueKind.Object || !target.TryGetProperty(name, out target))
                    throw new InvalidOperationException($"Unresolved reference '{pointer}' at {location}.");
            }
            value = target;
        }
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Expected an object at {location}.");
        return value;
    }
}
