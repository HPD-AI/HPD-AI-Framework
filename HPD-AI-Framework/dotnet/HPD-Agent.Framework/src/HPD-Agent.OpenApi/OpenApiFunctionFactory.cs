using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HPD.OpenApi.Core;
using HPD.OpenApi.Core.Model;
using Microsoft.Extensions.AI;

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
///   FunctionRetryMiddleware catches via OpenApiErrorHandler, retries with backoff
/// - 400, 404, 422, other 4xx → return <see cref="OpenApiErrorResponse"/> to LLM →
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
        bool collapseWithinToolkit = false)
    {
        var functions = new List<AIFunction>();

        foreach (var operation in spec.Operations)
        {
            try
            {
                functions.Add(CreateFunction(
                    operation, runner, config, namePrefix, parentContainer, collapseWithinToolkit));
            }
            catch (Exception ex)
            {
                // One bad operation schema doesn't block the rest (same pattern as SK)
                System.Diagnostics.Debug.WriteLine(
                    $"[HPD-Agent.OpenApi] Skipping operation '{operation.Id}': {ex.Message}");
            }
        }

        // If CollapseWithinToolkit, wrap all functions behind their own container.
        // parentContainer may be null for standalone WithOpenApi calls — that's fine,
        // the container becomes a top-level tool with no parent (same as CodingToolkit).
        if (collapseWithinToolkit && functions.Count > 0)
        {
            var containerName = $"OpenApi_{namePrefix ?? "spec"}";
            var (container, collapsedFunctions) =
                ExternalToolCollapsingWrapper.WrapOpenApiTools(
                    containerName,
                    functions,
                    parentContainer: parentContainer);

            return [container, ..collapsedFunctions];
        }

        return functions;
    }

    private static AIFunction CreateFunction(
        RestApiOperation operation,
        OpenApiOperationRunner runner,
        OpenApiConfig config,
        string? namePrefix,
        string? parentContainer,
        bool collapseWithinToolkit)
    {
        var functionName = BuildFunctionName(operation, namePrefix);
        var schema = BuildParameterSchema(operation, config);

        async Task<object?> InvokeAsync(AIFunctionArguments args, CancellationToken ct)
        {
            var result = await runner.RunAsync(operation, args, config.ServerUrlOverride, ct);

            // Bridge: retryable errors → exception for FunctionRetryMiddleware.
            // Client errors → return to LLM for self-correction.
            if (result is OpenApiErrorResponse error)
            {
                if (IsRetryableStatusCode(error.StatusCode))
                    throw new OpenApiRequestException(error);

                // 400, 404, 422, etc. — LLM should fix its request
                return error;
            }

            // Success path: runner returns OpenApiOperationResponse containing the parsed body
            // and the expected schema from the spec. ResponseOptimizationMiddleware will
            // process Content (field filtering, truncation) and serialize the whole object
            // to JSON so the LLM sees both the response data and the schema hint.
            return result;
        }

        // ParentContainer metadata:
        // - collapseWithinToolkit=false → stamp parentContainer on each function so
        //   ToolVisibilityManager treats them as flat members of the parent toolkit
        // - collapseWithinToolkit=true → parentContainer is stamped on the wrapper
        //   container (in CreateFunctions above), not on individual functions
        var effectiveParent = collapseWithinToolkit ? null : parentContainer;

        return HPDAIFunctionFactory.Create(
            InvokeAsync,
            new HPDAIFunctionFactoryOptions
            {
                Name = functionName,
                Description = operation.Description ?? $"{operation.Method} {operation.Path}",
                RequiresPermission = config.RequiresPermission,
                SchemaProvider = () => schema,
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["ParentContainer"] = effectiveParent,
                    ["IsContainer"] = false,
                    ["SourceType"] = "OpenApi",
                    ["openapi.path"] = operation.Path,
                    ["openapi.method"] = operation.Method.ToString(),
                    ["openapi.operationId"] = operation.Id,
                    // Response optimization hints — read by ResponseOptimizationMiddleware
                    ["openapi.response.dataField"] = config.ResponseOptimization?.DataField,
                    ["openapi.response.fieldsToInclude"] = config.ResponseOptimization?.FieldsToInclude,
                    ["openapi.response.fieldsToExclude"] = config.ResponseOptimization?.FieldsToExclude,
                    ["openapi.response.maxLength"] = config.ResponseOptimization?.MaxLength ?? 0
                }
            });
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

        foreach (var param in operation.Parameters.Where(p =>
            p.Location is RestApiParameterLocation.Path
                or RestApiParameterLocation.Query
                or RestApiParameterLocation.Header))
        {
            var paramSchema = param.Schema.HasValue
                ? JsonNode.Parse(param.Schema.Value.GetRawText())
                : new JsonObject { ["type"] = param.Type ?? "string" };

            if (paramSchema is JsonObject obj
                && !string.IsNullOrEmpty(param.Description)
                && !obj.ContainsKey("description"))
            {
                obj["description"] = param.Description;
            }

            properties.Add(param.Name, paramSchema);
            if (param.IsRequired) required.Add(param.Name);
        }

        if (operation.Payload != null)
        {
            if (config.EnableDynamicPayload)
                AddPayloadProperties(properties, required, operation.Payload.Properties);
            else
            {
                properties.Add("payload", new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = operation.Payload.Description ?? "The request body as a JSON string"
                });
                required.Add("payload");
            }
        }

        JsonNode schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false
        };

        // Apply AIJsonUtilities transform pipeline if configured.
        if (config.SchemaTransformOptions is { } opts)
        {
            var element = JsonSerializer.Deserialize<JsonElement>(schema.ToJsonString());
            var transformed = AIJsonUtilities.TransformSchema(element, opts);
            return transformed;
        }

        return JsonSerializer.Deserialize<JsonElement>(schema.ToJsonString());
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
            if (prop.IsRequired) required.Add(prop.Name);
        }
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
