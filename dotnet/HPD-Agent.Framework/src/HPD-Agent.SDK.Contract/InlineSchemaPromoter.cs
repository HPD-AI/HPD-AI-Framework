using System.Text;
using System.Text.Json.Nodes;

namespace HPD.Agent.SDK.Contract;

/// <summary>
/// Promotes reusable inline object schemas to named components before code
/// generation so compound request and response shapes do not collapse to JSON.
/// </summary>
public sealed class InlineSchemaPromoter
{
    private readonly JsonObject _schemas;
    private readonly HashSet<string> _usedNames = new(StringComparer.Ordinal);

    private InlineSchemaPromoter(JsonObject schemas)
    {
        _schemas = schemas;
        foreach (var property in schemas)
            _usedNames.Add(property.Key);
    }

    /// <summary>
    /// Promotes reusable inline object schemas into named components.
    /// </summary>
    public static void Promote(JsonNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root is not JsonObject document ||
            document["components"] is not JsonObject components ||
            components["schemas"] is not JsonObject schemas)
        {
            return;
        }

        var promoter = new InlineSchemaPromoter(schemas);
        if (document["paths"] is JsonObject paths)
            promoter.PromoteOperationSchemas(paths);

        // Components can themselves contain inline schemas. Iterate a queue so
        // newly promoted child components are normalized in the same pass.
        var processed = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var next = schemas
                .Select(static property => property.Key)
                .FirstOrDefault(processed.Add);
            if (next is null)
                break;
            if (schemas[next] is JsonObject schema)
                promoter.TransformChildren(schema, next);
        }
    }

    private void PromoteOperationSchemas(JsonObject paths)
    {
        foreach (var path in paths)
        {
            if (path.Key is null || path.Value is not JsonObject pathItem)
                continue;
            foreach (var method in pathItem)
            {
                if (method.Value is not JsonObject operation)
                    continue;
                var operationContext = Sanitize(path.Key + "_" + method.Key);
                if (operation["parameters"] is JsonArray parameters)
                {
                    for (var index = 0; index < parameters.Count; index++)
                    {
                        if (parameters[index] is JsonObject parameter &&
                            parameter["schema"] is JsonObject schema)
                        {
                            parameter["schema"] = PromoteInline(
                                schema,
                                operationContext + "_Parameter_" + index);
                        }
                    }
                }
                if (operation["requestBody"] is JsonObject requestBody)
                    PromoteContent(requestBody["content"], operationContext + "_Request");
                if (operation["responses"] is JsonObject responses)
                {
                    foreach (var response in responses)
                    {
                        if (response.Value is JsonObject responseObject)
                        {
                            PromoteContent(
                                responseObject["content"],
                                operationContext + "_Response_" + Sanitize(response.Key));
                        }
                    }
                }
            }
        }
    }

    private void PromoteContent(JsonNode? content, string context)
    {
        if (content is not JsonObject contentObject)
            return;
        foreach (var mediaType in contentObject)
        {
            if (mediaType.Value is JsonObject media &&
                media["schema"] is JsonObject schema)
            {
                media["schema"] = PromoteInline(
                    schema,
                    context + "_" + Sanitize(mediaType.Key));
            }
        }
    }

    private JsonNode PromoteInline(JsonObject schema, string context)
    {
        TransformChildren(schema, context);
        if (schema.ContainsKey("$ref") ||
            !HasObjectShape(schema))
        {
            return schema;
        }

        var componentId = UniqueName("Inline_" + context);
        _schemas[componentId] = schema.DeepClone();
        var reference = new JsonObject
        {
            ["$ref"] = "#/components/schemas/" + componentId
        };
        if (IsNullable(schema))
        {
            reference["type"] = new JsonArray
            {
                "null",
                schema["type"] is JsonArray typeArray
                    ? typeArray
                        .Select(static value => value?.GetValue<string>())
                        .FirstOrDefault(static value => value is not null and not "null") ?? "object"
                    : "object"
            };
        }
        return reference;
    }

    private void TransformChildren(JsonObject schema, string context)
    {
        if (schema["properties"] is JsonObject properties)
        {
            foreach (var property in properties.ToArray())
            {
                if (property.Value is JsonObject propertySchema)
                {
                    var promoted = PromoteInline(
                        propertySchema,
                        context + "_" + Sanitize(property.Key));
                    if (!ReferenceEquals(promoted, propertySchema))
                        properties[property.Key] = promoted;
                }
            }
        }
        if (schema["items"] is JsonObject items)
        {
            var promoted = PromoteInline(items, context + "_Item");
            if (!ReferenceEquals(promoted, items))
                schema["items"] = promoted;
        }
        else if (schema["items"] is JsonArray itemsTuple)
        {
            for (var index = 0; index < itemsTuple.Count; index++)
            {
                if (itemsTuple[index] is JsonObject item)
                {
                    var promoted = PromoteInline(item, context + "_Item_" + index);
                    if (!ReferenceEquals(promoted, item))
                        itemsTuple[index] = promoted;
                }
            }
        }
        foreach (var keyword in new[] { "oneOf", "anyOf", "allOf", "prefixItems" })
        {
            if (schema[keyword] is not JsonArray choices)
                continue;
            for (var index = 0; index < choices.Count; index++)
            {
                if (choices[index] is JsonObject choice)
                {
                    var promoted = PromoteInline(choice, context + "_" + keyword + "_" + index);
                    if (!ReferenceEquals(promoted, choice))
                        choices[index] = promoted;
                }
            }
        }
        if (schema["additionalProperties"] is JsonObject additional)
        {
            var promoted = PromoteInline(additional, context + "_AdditionalProperty");
            if (!ReferenceEquals(promoted, additional))
                schema["additionalProperties"] = promoted;
        }
        if (schema["$defs"] is JsonObject definitions)
        {
            foreach (var definition in definitions.ToArray())
            {
                if (definition.Value is JsonObject definitionSchema)
                {
                    var promoted = PromoteInline(
                        definitionSchema,
                        context + "_Definition_" + Sanitize(definition.Key));
                    if (!ReferenceEquals(promoted, definitionSchema))
                        definitions[definition.Key] = promoted;
                }
            }
        }
    }

    private string UniqueName(string candidate)
    {
        if (_usedNames.Add(candidate))
            return candidate;
        for (var suffix = 2; ; suffix++)
        {
            var next = candidate + "_" + suffix;
            if (_usedNames.Add(next))
                return next;
        }
    }

    private static bool HasObjectShape(JsonObject schema) =>
        !schema.ContainsKey("oneOf") &&
        !schema.ContainsKey("anyOf") &&
        !schema.ContainsKey("allOf") &&
        (schema["properties"] is JsonObject ||
         schema["type"] is JsonValue type &&
         type.TryGetValue<string>(out var value) &&
         string.Equals(value, "object", StringComparison.Ordinal) &&
         !schema.ContainsKey("additionalProperties"));

    private static bool IsNullable(JsonObject schema)
    {
        if (schema["nullable"] is JsonValue nullable &&
            nullable.TryGetValue<bool>(out var isNullable) &&
            isNullable)
        {
            return true;
        }
        return schema["type"] is JsonArray types &&
               types.Any(static value => string.Equals(value?.GetValue<string>(), "null", StringComparison.Ordinal));
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Anonymous";
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '_' or '-'
                ? character
                : '_');
        }
        return builder.ToString().Trim('_');
    }
}
