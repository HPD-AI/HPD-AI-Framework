using System.Collections.Immutable;
using System.Buffers;
using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.OpenApi.Core;
using HPD.OpenApi.Core.Model;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.OpenApi;

/// <summary>
/// Converts a <see cref="ParsedOpenApiSpec"/> into <see cref="AIFunction"/> instances
/// via <see cref="HPDAIFunctionFactory"/>.
///
/// Stamps HPD-Agent-specific collapsing metadata ("ParentContainer", "IsContainer")
/// so ToolVisibilityManager and ExternalToolCollapsingWrapper handle them correctly.
///
/// Throw-vs-return error bridging:
/// - 429, 5xx, 401, 408 → throw <see cref="OpenApiRequestException"/> →
///   FunctionRetryMiddleware applies the configured retry policy
/// - 400, 404, 422, other 4xx → return a model-facing error payload →
///   LLM self-corrects its request (retrying a bad request won't help)
/// </summary>
internal static partial class OpenApiFunctionFactory
{
    public static List<AIFunction> CreateFunctions(
        ParsedOpenApiSpec spec,
        OpenApiConfig config,
        OpenApiOperationRunner runner,
        string? namePrefix = null,
        string? parentContainer = null,
        bool collapseWithinToolHarness = false,
        ILogger? logger = null)
    {
        var functions = new List<AIFunction>();
        var containerName = collapseWithinToolHarness
            ? $"OpenApi_{namePrefix ?? "spec"}"
            : null;
        var containerId = containerName is null
            ? (CapabilityId?)null
            : CapabilityId.Create(
                $"openapi:{parentContainer ?? "standalone"}:{namePrefix ?? "spec"}:container");

        foreach (var operation in spec.Operations)
        {
            try
            {
                OpenApiSourceInspection.Validate(spec.SourceDocument, operation);
                functions.Add(CreateFunction(
                    operation,
                    spec.SourceDocument,
                    runner,
                    config,
                    namePrefix,
                    parentContainer,
                    collapseWithinToolHarness,
                    containerId));
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    ex,
                    "Skipped OpenAPI operation {OperationId} from source {SourceName}. " +
                    "Method {Method}, path {Path}, reason {Reason}.",
                    operation.Id,
                    namePrefix ?? "standalone",
                    operation.Method,
                    operation.Path,
                    ex.Message);
            }
        }

        // If CollapseWithinToolHarness, wrap all functions behind their own container.
        // parentContainer may be null for standalone WithOpenApi calls — that's fine,
        // the container becomes a top-level tool with no parent (same as CodingToolHarness).
        if (collapseWithinToolHarness && functions.Count > 0)
        {
            var (container, collapsedFunctions) =
                ExternalToolCollapsingWrapper.WrapOpenApiTools(
                    containerName!,
                    functions,
                    parentContainer: parentContainer);

            var metadata = new HPDCapabilityMetadata
            {
                Id = containerId!.Value,
                Kind = HPDCapabilityKind.ToolHarnessActivation,
                ParentContainerIds = CreateParentContainerIds(parentContainer),
                Reveals = collapsedFunctions
                    .Select(ReadCapabilityMetadata)
                    .Select(static value => value.Id)
                    .ToImmutableArray()
            };
            var taggedContainer = HPDAIFunctionFactory.Project(
                container,
                ReplaceCapabilityMetadata(container, metadata));
            return [taggedContainer, ..collapsedFunctions];
        }

        return functions;
    }

    private static AIFunction CreateFunction(
        RestApiOperation operation,
        JsonElement sourceDocument,
        OpenApiOperationRunner runner,
        OpenApiConfig config,
        string? namePrefix,
        string? parentContainer,
        bool collapseWithinToolHarness,
        CapabilityId? containerId)
    {
        operation = SnapshotOperation(operation);
        var functionName = BuildFunctionName(operation, namePrefix);
        var schema = BuildParameterSchema(operation, config);
        var rawBody = !config.EnableDynamicPayload;
        var serverUrlOverride = config.ServerUrlOverride;
        var responseOptimization = SnapshotResponseOptimization(config.ResponseOptimization);

        async Task<object?> InvokeAsync(AIFunctionArguments args, FunctionExecutionContext _, CancellationToken ct)
        {
            var input = args.GetJson();
            var result = await runner.RunProjectedAsync(operation,
                ReadGroup(input, "path"), ReadGroup(input, "query"), ReadGroup(input, "headers"),
                ReadGroup(input, "server"),
                input.TryGetProperty("body", out var body) ? body : default,
                rawBody, serverUrlOverride, ct);

            if (result is OpenApiErrorResponse error)
            {
                if (IsRetryableStatusCode(error.StatusCode))
                    throw new OpenApiRequestException(error);

                return OpenApiResponseFormatter.FormatError(error);
            }

            if (result is OpenApiOperationResponse response)
                return OpenApiResponseFormatter.FormatSuccess(response, responseOptimization);

            return result?.ToString();
        }

        // ParentContainer metadata:
        // - collapseWithinToolHarness=false → stamp parentContainer on each function so
        //   ToolVisibilityManager treats them as flat members of the parent toolharness
        // - collapseWithinToolHarness=true → parentContainer is stamped on the wrapper
        //   container (in CreateFunctions above), not on individual functions
        var effectiveParent = collapseWithinToolHarness ? null : parentContainer;
        var parentContainerIds = collapseWithinToolHarness
            ? containerId is { } parent
                ? ImmutableArray.Create(parent)
                : []
            : CreateParentContainerIds(parentContainer);
        var capabilityId = CapabilityId.Create(
            $"openapi:{parentContainer ?? "standalone"}:{namePrefix ?? "spec"}:{operation.Id}");

        return HPDAIFunctionFactory.Create(
            InvokeAsync,
            new HPDAIFunctionFactoryOptions
            {
                Name = functionName,
                Description = operation.Description ?? $"{operation.Method} {operation.Path}",
                FunctionPermission = config.RequiresPermission
                    ? AIFunctionPermissionDeclaration.Required(functionName)
                    : null,
                SchemaProvider = () => schema,
                InputContractKind = FunctionInputContractKind.ProjectedSpecification,
                PermissionSourceRevision = ComputeSourceRevision(operation, sourceDocument, schema, rawBody, serverUrlOverride),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["ParentContainer"] = effectiveParent,
                    ["IsContainer"] = false,
                    ["SourceType"] = "OpenApi",
                    ["openapi.path"] = operation.Path,
                    ["openapi.method"] = operation.Method.ToString(),
                    ["openapi.operationId"] = operation.Id,
                    [HPDCapabilityMetadata.AdditionalPropertiesKey] = new HPDCapabilityMetadata
                    {
                        Id = capabilityId,
                        Kind = HPDCapabilityKind.OpenApi,
                        ParentContainerIds = parentContainerIds
                    }
                }
            });
    }

    private static string ComputeSourceRevision(
        RestApiOperation operation, JsonElement sourceDocument, JsonElement schema, bool rawBody, Uri? serverUrlOverride)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("operationId", operation.Id);
            writer.WriteString("method", operation.Method.Method);
            writer.WriteString("path", operation.Path);
            writer.WriteString("server", operation.ServerUrl);
            writer.WriteString("serverOverride", serverUrlOverride?.ToString());
            writer.WriteBoolean("rawBody", rawBody);
            writer.WritePropertyName("projectedSchema");
            schema.WriteTo(writer);
            writer.WritePropertyName("source");
            if (sourceDocument.ValueKind == JsonValueKind.Undefined) writer.WriteNullValue();
            else sourceDocument.WriteTo(writer);
            writer.WriteStartArray("parameters");
            foreach (var parameter in operation.Parameters)
            {
                writer.WriteStartObject();
                writer.WriteString("name", parameter.Name);
                writer.WriteString("location", parameter.Location.ToString());
                writer.WriteBoolean("required", parameter.IsRequired);
                writer.WriteBoolean("expand", parameter.Expand);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartObject("serverVariables");
            foreach (var (name, variable) in operation.ServerVariables.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject(name);
                writer.WriteString("default", variable.Default);
                writer.WriteString("argumentName", variable.ArgumentName);
                writer.WriteStartArray("enum");
                if (variable.Enum is not null)
                    foreach (var value in variable.Enum) writer.WriteStringValue(value);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteString("bodyMediaType", operation.Payload?.MediaType);
            writer.WriteBoolean("bodyRequired", operation.Payload?.IsRequired == true);
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static RestApiOperation SnapshotOperation(RestApiOperation operation) => new()
    {
        Id = operation.Id, Path = operation.Path, Method = operation.Method,
        Description = operation.Description, ServerUrl = operation.ServerUrl,
        Parameters = operation.Parameters.Select(parameter => new RestApiParameter
        {
            Name = parameter.Name, Type = parameter.Type, Format = parameter.Format,
            IsRequired = parameter.IsRequired, Location = parameter.Location,
            Description = parameter.Description, DefaultValue = parameter.DefaultValue,
            Schema = parameter.Schema?.Clone(), ArrayItemType = parameter.ArrayItemType,
            Expand = parameter.Expand
        }).ToList(),
        Payload = operation.Payload is { } payload ? new RestApiPayload
        {
            MediaType = payload.MediaType, IsRequired = payload.IsRequired,
            Description = payload.Description, Schema = payload.Schema?.Clone(),
            Properties = SnapshotPayloadProperties(payload.Properties)
        } : null,
        ServerVariables = operation.ServerVariables.ToDictionary(pair => pair.Key, pair => new RestApiServerVariable
        {
            Default = pair.Value.Default, Description = pair.Value.Description,
            ArgumentName = pair.Value.ArgumentName, Enum = pair.Value.Enum?.ToArray()
        }, StringComparer.Ordinal),
        ResponseSchemas = operation.ResponseSchemas.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase)
    };

    private static List<RestApiPayloadProperty> SnapshotPayloadProperties(IEnumerable<RestApiPayloadProperty> properties) =>
        properties.Select(property => new RestApiPayloadProperty
        {
            Name = property.Name, Type = property.Type, Format = property.Format,
            IsRequired = property.IsRequired, Description = property.Description,
            Schema = property.Schema?.Clone(), DefaultValue = property.DefaultValue,
            Properties = SnapshotPayloadProperties(property.Properties)
        }).ToList();

    private static ResponseOptimizationConfig? SnapshotResponseOptimization(ResponseOptimizationConfig? options) =>
        options is null ? null : new ResponseOptimizationConfig
        {
            DataField = options.DataField, MaxLength = options.MaxLength,
            FieldsToInclude = options.FieldsToInclude?.ToArray(),
            FieldsToExclude = options.FieldsToExclude?.ToArray()
        };

    private static ImmutableArray<CapabilityId> CreateParentContainerIds(string? parentContainer) =>
        parentContainer is null
            ? []
            : [CapabilityId.Create($"generated:{parentContainer}:harness")];

    private static HPDCapabilityMetadata ReadCapabilityMetadata(AIFunction function) =>
        function.AdditionalProperties?.TryGetValue(
            HPDCapabilityMetadata.AdditionalPropertiesKey,
            out var value) == true &&
        value is HPDCapabilityMetadata metadata
            ? metadata
            : throw new InvalidOperationException(
                $"OpenAPI function '{function.Name}' has no typed capability metadata.");

    private static Dictionary<string, object?> ReplaceCapabilityMetadata(
        AIFunction function,
        HPDCapabilityMetadata metadata)
    {
        var properties = function.AdditionalProperties?.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.Ordinal) ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        properties[HPDCapabilityMetadata.AdditionalPropertiesKey] = metadata;
        return properties;
    }

    private static bool IsRetryableStatusCode(int statusCode) =>
        statusCode is 429 or 401 or 408 or (>= 500 and < 600);

    /// <summary>
    /// Builds the JSON Schema for a REST API operation's parameters.
    /// OpenAPI parameters don't come from C# types — schemas are built manually
    /// from RestApiParameter/RestApiPayloadProperty metadata.
    ///
    /// If SchemaTransformOptions is set on the config, AIJsonUtilities.TransformSchema()
    /// is applied post-construction. Note: TransformSchema throws if passed the Default
    /// instance — guard against it before calling.
    /// </summary>
    private static JsonElement BuildParameterSchema(RestApiOperation operation, OpenApiConfig config)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, location) in new[]
        {
            ("path", RestApiParameterLocation.Path),
            ("query", RestApiParameterLocation.Query),
            ("headers", RestApiParameterLocation.Header)
        })
        {
            var groupProperties = new JsonObject();
            var groupRequired = new JsonArray();
            foreach (var parameter in operation.Parameters.Where(parameter => parameter.Location == location))
            {
                var parameterSchema = parameter.Schema.HasValue
                    ? JsonNode.Parse(parameter.Schema.Value.GetRawText())
                    : new JsonObject { ["type"] = parameter.Type ?? "string" };
                if (parameterSchema is JsonObject obj && !string.IsNullOrEmpty(parameter.Description) && !obj.ContainsKey("description"))
                    obj["description"] = parameter.Description;
                groupProperties.Add(parameter.Name, parameterSchema);
                if (parameter.IsRequired) groupRequired.Add(parameter.Name);
            }
            if (groupProperties.Count == 0) continue;
            properties.Add(name, new JsonObject
            {
                ["type"] = "object", ["properties"] = groupProperties,
                ["required"] = groupRequired, ["additionalProperties"] = false
            });
            if (groupRequired.Count != 0) required.Add(name);
        }
        if (operation.ServerVariables.Count > 0)
        {
            var variables = new JsonObject();
            foreach (var (name, variable) in operation.ServerVariables)
            {
                var variableSchema = new JsonObject { ["type"] = "string" };
                if (variable.Enum is not null)
                    variableSchema["enum"] = new JsonArray(variable.Enum.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
                variables.Add(variable.ArgumentName ?? name, variableSchema);
            }
            properties.Add("server", new JsonObject
            {
                ["type"] = "object", ["properties"] = variables, ["additionalProperties"] = false
            });
        }
        if (operation.Payload is { } payload)
        {
            JsonNode? bodySchema;
            if (!config.EnableDynamicPayload || payload.MediaType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase))
                bodySchema = new JsonObject { ["type"] = "string" };
            else if (payload.Schema is { } sourceSchema)
                bodySchema = JsonNode.Parse(sourceSchema.GetRawText());
            else
            {
                var bodyProperties = new JsonObject();
                var bodyRequired = new JsonArray();
                AddPayloadProperties(bodyProperties, bodyRequired, payload.Properties);
                bodySchema = new JsonObject { ["type"] = "object", ["properties"] = bodyProperties,
                    ["required"] = bodyRequired, ["additionalProperties"] = false };
            }
            properties.Add("body", bodySchema);
            if (payload.IsRequired) required.Add("body");
        }
        JsonNode schema = new JsonObject
        {
            ["type"] = "object", ["properties"] = properties, ["required"] = required,
            ["additionalProperties"] = false
        };

        // Apply AIJsonUtilities transform pipeline if configured.
        if (config.SchemaTransformOptions is { } opts)
        {
            var element = ParseJsonElement(schema);
            var transformed = AIJsonUtilities.TransformSchema(element, opts);
            return transformed;
        }

        return ParseJsonElement(schema);
    }

    private static IDictionary<string, object?> ReadGroup(JsonElement input, string name)
    {
        if (!input.TryGetProperty(name, out var group))
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        if (group.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"OpenAPI argument group '{name}' must be an object.");
        return group.EnumerateObject().ToDictionary(
            property => property.Name, property => (object?)property.Value.Clone(), StringComparer.Ordinal);
    }

    private static void AddPayloadProperties(
        JsonObject properties, JsonArray required,
        IList<RestApiPayloadProperty> payloadProperties)
    {
        foreach (var prop in payloadProperties)
        {
            var propSchema = prop.Schema.HasValue
                ? JsonNode.Parse(prop.Schema.Value.GetRawText())
                : new JsonObject { ["type"] = prop.Type ?? "string" };

            if (propSchema is JsonObject obj
                && !string.IsNullOrEmpty(prop.Description)
                && !obj.ContainsKey("description"))
            {
                obj["description"] = prop.Description;
            }

            properties.Add(prop.Name, propSchema);
            if (prop.IsRequired) required.Add((JsonNode?)JsonValue.Create(prop.Name));
        }
    }

    private static JsonElement ParseJsonElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    private static string BuildFunctionName(RestApiOperation operation, string? prefix)
    {
        string baseName;
        if (!string.IsNullOrWhiteSpace(operation.Id))
        {
            baseName = InvalidCharsRegex().Replace(operation.Id, "");
        }
        else
        {
            var tokens = operation.Path.Split('/', '\\');
            var sb = new System.Text.StringBuilder();
            sb.Append(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                operation.Method.ToString().ToLowerInvariant()));
            foreach (var token in tokens)
            {
                var clean = InvalidCharsRegex().Replace(token, "");
                if (!string.IsNullOrEmpty(clean))
                    sb.Append(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                        clean.ToLowerInvariant()));
            }
            baseName = sb.ToString();
        }
        return prefix != null ? $"{prefix}_{baseName}" : baseName;
    }

    [GeneratedRegex("[^0-9A-Za-z_]")]
    private static partial Regex InvalidCharsRegex();
}
