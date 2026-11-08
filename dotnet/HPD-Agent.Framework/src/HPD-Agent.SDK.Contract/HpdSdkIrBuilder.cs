using System.Security.Cryptography;
using System.Text.Json;

namespace HPD.Agent.SDK.Contract;

/// <summary>Builds the versioned HPD Agent SDK IR from a normalized contract.</summary>
public static class HpdSdkIrBuilder
{
    private static readonly HashSet<string> StructuralSchemaKeywords = new(StringComparer.Ordinal)
    {
        "$ref",
        "type",
        "format",
        "nullable",
        "oneOf",
        "anyOf",
        "allOf",
        "enum",
        "const",
        "properties",
        "required",
        "items",
        "additionalProperties"
    };

    /// <summary>Builds IR v1 for one normalized HPD agent contract.</summary>
    public static HpdSdkIr Build(OpenApiContract contract, string generatorVersion)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatorVersion);

        var diagnostics = new List<HpdDiagnosticIr>();
        var types = contract.Schemas
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new HpdTypeDefinitionIr(
                pair.Key,
                BuildType(
                    pair.Value,
                    "#/components/schemas/" + EscapeReference(pair.Key),
                    diagnostics,
                    depth: 0)))
            .ToArray();
        var operations = contract.Operations
            .OrderBy(static operation => operation.OperationId, StringComparer.Ordinal)
            .Select(operation => BuildOperation(operation, diagnostics))
            .ToArray();
        var unions = BuildUnions(contract, diagnostics);

        return new HpdSdkIr(
            HpdSdkIrVersions.V1,
            generatorVersion,
            contract.OpenApiSha256,
            contract.EventDigest,
            ComputeInputDigest(contract),
            new HpdApiIr(contract.Title, contract.ApiVersion, contract.OpenApiVersion),
            types,
            operations,
            unions,
            BuildCapabilities(contract, operations),
            diagnostics
                .Distinct()
                .OrderBy(static diagnostic => diagnostic.Path, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .ToArray());
    }

    private static HpdOperationIr BuildOperation(
        OperationIr operation,
        List<HpdDiagnosticIr> diagnostics)
    {
        var operationPath = "#/paths/" + EscapeReference(operation.Path) + "/" +
                            operation.Method.ToLowerInvariant();
        return new HpdOperationIr(
            operation.OperationId,
            operation.Method,
            operation.Path,
            operation.Tag,
            operation.Summary,
            operation.Parameters
                .OrderBy(static parameter => parameter.Location, StringComparer.Ordinal)
                .ThenBy(static parameter => parameter.Name, StringComparer.Ordinal)
                .Select(parameter => new HpdParameterIr(
                    parameter.Name,
                    parameter.Location,
                    parameter.Required,
                    BuildType(
                        parameter.Schema,
                        operationPath + "/parameters/" + parameter.Location + "/" +
                        EscapeReference(parameter.Name),
                        diagnostics,
                        depth: 0,
                        optional: !parameter.Required)))
                .ToArray(),
            operation.RequestBody is { } requestBody
                ? new HpdRequestBodyIr(
                    requestBody.Required,
                    requestBody.MediaType,
                    BuildType(
                        requestBody.Schema,
                        operationPath + "/requestBody",
                        diagnostics,
                        depth: 0))
                : null,
            operation.SuccessStatus,
            operation.SuccessMediaType,
            operation.SuccessSchema is { } successSchema
                ? BuildType(
                    successSchema,
                    operationPath + "/responses/" + operation.SuccessStatus,
                    diagnostics,
                    depth: 0)
                : null,
            operation.HasNotFoundResponse,
            ContentKind(operation),
            operation.ReplaySafe);
    }

    private static IReadOnlyList<HpdUnionIr> BuildUnions(
        OpenApiContract contract,
        List<HpdDiagnosticIr> diagnostics)
    {
        var unions = new List<HpdUnionIr>
        {
            BuildUnion(contract, "event", "output-event", "raw-event", contract.Events, diagnostics),
            BuildUnion(contract, "public-input", "request-input", "reject", contract.PublicInputs, diagnostics)
        };
        if (contract.ResponseInputs.Count > 0)
        {
            unions.Add(BuildUnion(
                contract,
                "response-input",
                "interaction-response",
                "reject",
                contract.ResponseInputs,
                diagnostics));
        }
        return unions.OrderBy(static union => union.Role, StringComparer.Ordinal).ToArray();
    }

    private static HpdUnionIr BuildUnion(
        OpenApiContract contract,
        string name,
        string role,
        string unknownVariantPolicy,
        IReadOnlyList<UnionVariant> variants,
        List<HpdDiagnosticIr> diagnostics) =>
        new(
            name,
            role,
            unknownVariantPolicy,
            variants
                .OrderBy(static variant => variant.Discriminator, StringComparer.Ordinal)
                .Select(variant => new HpdUnionVariantIr(
                    variant.ComponentId,
                    variant.Discriminator,
                    BuildSchemaReference(variant.ComponentId, diagnostics)))
                .ToArray());

    private static HpdTypeIr BuildSchemaReference(
        string componentId,
        List<HpdDiagnosticIr> diagnostics)
    {
        if (!componentId.Contains('~', StringComparison.Ordinal))
        {
            return new HpdTypeIr(
                "reference",
                Reference: "#/components/schemas/" + EscapeReference(componentId));
        }

        diagnostics.Add(new HpdDiagnosticIr(
            "error",
            "HPDIR001",
            "#/unions/" + EscapeReference(componentId),
            $"Component identifier '{componentId}' cannot be represented as a local schema reference."));
        return new HpdTypeIr("unknown");
    }

    private static IReadOnlyList<HpdCapabilityIr> BuildCapabilities(
        OpenApiContract contract,
        IReadOnlyList<HpdOperationIr> operations)
    {
        var operationCapabilities = new (string Name, Func<HpdOperationIr, bool> Matches)[]
        {
            ("agents", operation => operation.Tag == "Agents"),
            ("client-tools", operation => operation.Tag == "Client Tools"),
            ("content", operation => operation.Tag == "Content"),
            ("evaluations", operation => operation.Tag == "Evaluations"),
            ("executions", operation => operation.Tag == "Executions"),
            ("forking", operation => operation.OperationId == "ForkThread"),
            ("interactions", operation => operation.OperationId == "RespondToAgentRequest"),
            ("json", _ => true),
            ("runtime-extensions", operation => operation.Tag == "Runtime Extensions"),
            ("server-sent-events", operation => operation.ContentKind == "sse"),
            ("sessions", operation => operation.Tag == "Sessions"),
            ("threads", operation => operation.Tag == "Threads"),
            ("websocket", operation => operation.ContentKind == "websocket")
        };
        var capabilities = operationCapabilities
            .Select(definition =>
            {
                var matching = operations
                    .Where(definition.Matches)
                    .Select(static operation => operation.OperationId)
                    .OrderBy(static operationId => operationId, StringComparer.Ordinal)
                    .ToArray();
                return new HpdCapabilityIr(definition.Name, matching.Length > 0, matching);
            })
            .Where(static capability => capability.Available)
            .ToList();
        var componentIds = contract.Schemas.Keys.ToArray();
        AddSemanticCapability(
            capabilities,
            "audio",
            componentIds.Any(static componentId =>
                componentId.Contains("Audio", StringComparison.Ordinal)));
        AddSemanticCapability(
            capabilities,
            "collapsing",
            componentIds.Any(static componentId =>
                componentId.Contains("Collaps", StringComparison.Ordinal)));
        AddSemanticCapability(
            capabilities,
            "goals",
            componentIds.Any(static componentId =>
                componentId.Contains("Goals.", StringComparison.Ordinal)));
        AddSemanticCapability(
            capabilities,
            "middleware",
            componentIds.Any(static componentId =>
                componentId.Contains("Middleware.", StringComparison.Ordinal)));
        AddSemanticCapability(
            capabilities,
            "multimodal-content",
            componentIds.Any(static componentId =>
                componentId.Contains("AIContent", StringComparison.Ordinal)));
        AddSemanticCapability(
            capabilities,
            "provider-authentication",
            componentIds.Any(static componentId =>
                componentId.Contains("ProviderAuthentication", StringComparison.Ordinal)));
        AddSemanticCapability(
            capabilities,
            "unknown-events",
            contract.Events.Count > 0);
        return capabilities
            .OrderBy(static capability => capability.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddSemanticCapability(
        ICollection<HpdCapabilityIr> capabilities,
        string name,
        bool available)
    {
        if (available)
            capabilities.Add(new HpdCapabilityIr(name, true, []));
    }

    private static HpdTypeIr BuildType(
        JsonElement schema,
        string path,
        List<HpdDiagnosticIr> diagnostics,
        int depth,
        bool optional = false)
    {
        if (depth > 64)
        {
            AddLossDiagnostic(
                diagnostics,
                "HPDIR002",
                path,
                "Schema nesting exceeded 64 levels and was preserved as arbitrary JSON.");
            return new HpdTypeIr("any-json", Nullable: optional);
        }
        if (schema.ValueKind == JsonValueKind.True)
            return new HpdTypeIr("any-json", Nullable: optional);
        if (schema.ValueKind == JsonValueKind.False)
            return new HpdTypeIr("never", Nullable: optional);
        if (schema.ValueKind != JsonValueKind.Object)
        {
            AddLossDiagnostic(
                diagnostics,
                "HPDIR003",
                path,
                $"Schema value {schema.ValueKind} is not a JSON Schema object and was preserved as arbitrary JSON.");
            return new HpdTypeIr("any-json", Nullable: optional);
        }

        var nullable = optional || IsNullable(schema);
        var annotations = ReadAnnotations(schema);
        if (schema.TryGetProperty("$ref", out var reference) &&
            reference.GetString() is { Length: > 0 } referenceValue)
        {
            return new HpdTypeIr(
                "reference",
                Nullable: nullable,
                Reference: referenceValue,
                Annotations: annotations);
        }

        if (schema.TryGetProperty("const", out var constant))
        {
            if (constant.ValueKind == JsonValueKind.String)
            {
                return new HpdTypeIr(
                    "literal",
                    Nullable: nullable,
                    Literal: constant.GetString(),
                    Annotations: annotations);
            }
            AddLossDiagnostic(
                diagnostics,
                "HPDIR004",
                path + "/const",
                $"Non-string const value '{constant.GetRawText()}' was preserved as arbitrary JSON.");
            return new HpdTypeIr("any-json", Nullable: nullable, Annotations: annotations);
        }

        if (TryReadStringEnum(schema, out var enumValues))
        {
            return new HpdTypeIr(
                "enum",
                Nullable: nullable,
                EnumValues: enumValues,
                Annotations: annotations);
        }
        if (schema.TryGetProperty("enum", out var enumElement) &&
            enumElement.ValueKind == JsonValueKind.Array)
        {
            AddLossDiagnostic(
                diagnostics,
                "HPDIR005",
                path + "/enum",
                "Non-string or empty enum values were preserved as arbitrary JSON.");
            return new HpdTypeIr("any-json", Nullable: nullable, Annotations: annotations);
        }

        foreach (var composition in new[] { "oneOf", "anyOf", "allOf" })
        {
            if (!schema.TryGetProperty(composition, out var branches) ||
                branches.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            var kind = composition == "allOf" ? "intersection" : "union";
            var branchTypes = branches
                .EnumerateArray()
                .Select((branch, index) => BuildType(
                    branch,
                    path + "/" + composition + "/" + index,
                    diagnostics,
                    depth + 1))
                .ToArray();
            if (branchTypes.Length == 0)
            {
                AddLossDiagnostic(
                    diagnostics,
                    "HPDIR006",
                    path + "/" + composition,
                    $"{composition} has no branches and was preserved as arbitrary JSON.");
                return new HpdTypeIr("any-json", Nullable: nullable, Annotations: annotations);
            }
            return new HpdTypeIr(
                kind,
                Nullable: nullable,
                ObjectModel: SchemaType(schema) == "object" ||
                             schema.TryGetProperty("properties", out _),
                Composition: composition,
                Branches: branchTypes,
                Annotations: annotations);
        }

        var type = SchemaType(schema);
        if (type == "object" ||
            schema.TryGetProperty("properties", out _) ||
            schema.TryGetProperty("additionalProperties", out _))
        {
            return BuildObjectType(schema, path, diagnostics, depth, nullable, annotations);
        }
        if (type == "array")
        {
            var itemType = schema.TryGetProperty("items", out var items)
                ? BuildType(items, path + "/items", diagnostics, depth + 1)
                : new HpdTypeIr("any-json");
            var prefixItems = schema.TryGetProperty("prefixItems", out var tupleItems) &&
                              tupleItems.ValueKind == JsonValueKind.Array
                ? tupleItems
                    .EnumerateArray()
                    .Select((item, index) => BuildType(
                        item,
                        path + "/prefixItems/" + index,
                        diagnostics,
                        depth + 1))
                    .ToArray()
                : null;
            if (prefixItems is { Length: > 0 })
            {
                AddLossDiagnostic(
                    diagnostics,
                    "HPDIR008",
                    path + "/prefixItems",
                    "Tuple schemas are represented by their array item type; prefix items remain in annotations.");
            }
            return new HpdTypeIr(
                "array",
                Nullable: nullable,
                ItemType: itemType,
                Annotations: annotations);
        }

        if (type is "string" or "boolean" or "integer" or "number" or "null")
        {
            return new HpdTypeIr(
                "primitive",
                Primitive: type,
                Format: ReadFormat(schema),
                Nullable: nullable,
                Annotations: annotations);
        }

        // An empty schema, or a schema with only validation annotations, is a
        // valid JSON Schema for an arbitrary JSON value. It is not semantic loss.
        return new HpdTypeIr("any-json", Nullable: nullable, Annotations: annotations);
    }

    private static HpdTypeIr BuildObjectType(
        JsonElement schema,
        string path,
        List<HpdDiagnosticIr> diagnostics,
        int depth,
        bool nullable,
        IReadOnlyDictionary<string, JsonElement>? annotations)
    {
        var required = schema.TryGetProperty("required", out var requiredElement) &&
                       requiredElement.ValueKind == JsonValueKind.Array
            ? requiredElement
                .EnumerateArray()
                .Where(static value => value.ValueKind == JsonValueKind.String)
                .Select(static value => value.GetString())
                .Where(static value => value is not null)
                .Select(static value => value!)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var properties = schema.TryGetProperty("properties", out var propertyElement) &&
                         propertyElement.ValueKind == JsonValueKind.Object
            ? propertyElement
                .EnumerateObject()
                .Select(property => new HpdPropertyIr(
                    property.Name,
                    required.Contains(property.Name),
                    BuildType(
                        property.Value,
                        path + "/properties/" + EscapeReference(property.Name),
                        diagnostics,
                        depth + 1,
                        optional: !required.Contains(property.Name))))
                .ToArray()
            : [];

        HpdTypeIr? valueType = null;
        var allowsAdditional = false;
        if (schema.TryGetProperty("additionalProperties", out var additional))
        {
            switch (additional.ValueKind)
            {
                case JsonValueKind.True:
                    allowsAdditional = true;
                    valueType = new HpdTypeIr("any-json");
                    break;
                case JsonValueKind.Object:
                    allowsAdditional = true;
                    valueType = BuildType(
                        additional,
                        path + "/additionalProperties",
                        diagnostics,
                        depth + 1);
                    break;
                case JsonValueKind.False:
                    break;
                default:
                    AddLossDiagnostic(
                        diagnostics,
                        "HPDIR010",
                        path + "/additionalProperties",
                        "additionalProperties must be a boolean or schema; the value was ignored.");
                    break;
            }
        }

        if (properties.Length == 0 && allowsAdditional)
            return new HpdTypeIr(
                "map",
                Nullable: nullable,
                ValueType: valueType,
                ObjectModel: true,
                Annotations: annotations);

        return new HpdTypeIr(
            "object",
            Nullable: nullable,
            Properties: properties,
            RequiredProperties: required.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
            ValueType: valueType,
            AllowsAdditionalProperties: allowsAdditional,
            ObjectModel: true,
            Annotations: annotations);
    }

    private static IReadOnlyDictionary<string, JsonElement>? ReadAnnotations(JsonElement schema)
    {
        var annotations = schema
            .EnumerateObject()
            .Where(property => !StructuralSchemaKeywords.Contains(property.Name))
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .ToDictionary(
                static property => property.Name,
                static property => property.Value.Clone(),
                StringComparer.Ordinal);
        return annotations.Count == 0 ? null : annotations;
    }

    private static bool TryReadStringEnum(
        JsonElement schema,
        out IReadOnlyList<string> values)
    {
        values = [];
        if (!schema.TryGetProperty("enum", out var enumElement) ||
            enumElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in enumElement.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.Null)
                continue;
            if (value.ValueKind != JsonValueKind.String ||
                value.GetString() is not { Length: > 0 } text)
            {
                return false;
            }
            if (seen.Add(text))
                result.Add(text);
        }
        values = result.ToArray();
        return values.Count > 0;
    }

    private static bool IsNullable(JsonElement schema)
    {
        if (schema.TryGetProperty("nullable", out var nullable) &&
            nullable.ValueKind == JsonValueKind.True)
        {
            return true;
        }
        return schema.TryGetProperty("type", out var type) &&
               type.ValueKind == JsonValueKind.Array &&
               type.EnumerateArray().Any(static value =>
                   value.ValueKind == JsonValueKind.String &&
                   value.GetString() == "null");
    }

    private static string? SchemaType(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type))
            return null;
        if (type.ValueKind == JsonValueKind.String)
            return type.GetString();
        if (type.ValueKind != JsonValueKind.Array)
            return null;
        return type
            .EnumerateArray()
            .Where(static value => value.ValueKind == JsonValueKind.String)
            .Select(static value => value.GetString())
            .FirstOrDefault(static value => value is not null and not "null");
    }

    private static string? ReadFormat(JsonElement schema) =>
        schema.TryGetProperty("format", out var format) &&
        format.ValueKind == JsonValueKind.String
            ? format.GetString()
            : null;

    private static string ContentKind(OperationIr operation)
    {
        if (operation.IsServerSentEvents)
            return "sse";
        if (operation.IsWebSocketUpgrade)
            return "websocket";
        if (string.Equals(operation.SuccessMediaType, "application/octet-stream", StringComparison.Ordinal))
            return "binary";
        if (string.Equals(operation.RequestBody?.MediaType, "multipart/form-data", StringComparison.Ordinal))
            return "multipart";
        if (operation.SuccessMediaType is not null)
            return "json";
        return "empty";
    }

    private static string ComputeInputDigest(OpenApiContract contract)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var input in contract.PublicInputs
                         .OrderBy(static input => input.Discriminator, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("componentId", input.ComponentId);
                writer.WriteString("discriminator", input.Discriminator);
                if (contract.Schemas.TryGetValue(input.ComponentId, out var schema))
                {
                    writer.WritePropertyName("schema");
                    schema.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return "sha256:" +
               Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void AddLossDiagnostic(
        List<HpdDiagnosticIr> diagnostics,
        string code,
        string path,
        string message) =>
        diagnostics.Add(new HpdDiagnosticIr("warning", code, path, message));

    private static string EscapeReference(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);
}
