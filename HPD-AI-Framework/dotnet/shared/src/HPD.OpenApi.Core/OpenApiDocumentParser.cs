using System.Text.Json;
using System.Text.Json.Nodes;
using HPD.OpenApi.Core.Model;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Models.Interfaces;
using Microsoft.OpenApi.Reader;

namespace HPD.OpenApi.Core;

/// <summary>
/// Parses OpenAPI specifications into intermediate <see cref="RestApiOperation"/> models.
/// Adapted from Semantic Kernel's OpenApiDocumentParser.
///
/// Supports JSON, OpenAPI 2.0/3.0/3.1. OpenAPI 3.1 documents are downgraded to 3.0.1 for
/// compatibility with Microsoft.OpenApi, which is safe for operation extraction purposes.
/// YAML is not supported — convert to JSON first (e.g., yq -o json . spec.yaml > spec.json).
/// </summary>
public sealed class OpenApiDocumentParser
{
    private const int PayloadPropertiesHierarchyMaxDepth = 10;
    private static readonly Version s_latestSupportedVersion = new(3, 0, 1);
    private static readonly List<string> s_supportedMediaTypes = ["application/json", "text/plain"];
    private static readonly JsonSerializerOptions s_writeIndented = new() { WriteIndented = true };

    /// <summary>
    /// Parses an OpenAPI spec from a stream (JSON format).
    /// </summary>
    /// <param name="baseUri">
    /// Used to resolve relative server URLs in the spec (e.g. <c>"/api/v3"</c> → <c>https://host/api/v3"</c>).
    /// Pass the URI the spec was fetched from, or a <c>file://</c> URI for local files.
    /// </param>
    public async Task<ParsedOpenApiSpec> ParseAsync(
        Stream stream,
        OpenApiCoreConfig config,
        Uri? baseUri = null,
        CancellationToken cancellationToken = default)
    {
        var jsonObject = await DowngradeVersionIfNeededAsync(stream, cancellationToken);
        using var memoryStream = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(jsonObject.ToJsonString(s_writeIndented)));
        var result = await OpenApiDocument.LoadAsync(memoryStream, cancellationToken: cancellationToken);
        AssertReadingSuccessful(result, config.IgnoreNonCompliantErrors);
        return new ParsedOpenApiSpec
        {
            Info = ExtractInfo(result.Document!),
            Operations = ExtractOperations(result.Document!, config, baseUri)
        };
    }

    /// <summary>Parses an OpenAPI spec from a file on disk (JSON format).</summary>
    public async Task<ParsedOpenApiSpec> ParseFromFileAsync(
        string filePath,
        OpenApiCoreConfig config,
        CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(filePath);
        var baseUri = new Uri(Path.GetFullPath(filePath));
        return await ParseAsync(stream, config, baseUri, cancellationToken);
    }

    /// <summary>Parses an OpenAPI spec by fetching it from a URI.</summary>
    public async Task<ParsedOpenApiSpec> ParseFromUriAsync(
        Uri uri,
        HttpClient httpClient,
        OpenApiCoreConfig config,
        CancellationToken cancellationToken = default)
    {
        using var stream = await httpClient.GetStreamAsync(uri, cancellationToken);
        return await ParseAsync(stream, config, uri, cancellationToken);
    }

    /// <summary>
    /// Reads the JSON object and downgrades the openapi version field from 3.1.x to 3.0.1
    /// if needed. Microsoft.OpenApi doesn't fully support 3.1; the downgrade is safe for
    /// operation extraction.
    /// </summary>
    private static async Task<JsonObject> DowngradeVersionIfNeededAsync(
        Stream stream, CancellationToken ct)
    {
        var jsonObject = await JsonNode.ParseAsync(stream, cancellationToken: ct) as JsonObject
            ?? throw new OpenApiParseException("Failed to parse OpenAPI document as JSON.");

        if (jsonObject.TryGetPropertyValue("openapi", out var versionNode)
            && versionNode is JsonValue versionValue
            && Version.TryParse(versionValue.ToString(), out var version)
            && version > s_latestSupportedVersion)
        {
            jsonObject["openapi"] = s_latestSupportedVersion.ToString();
        }

        return jsonObject;
    }

    private static List<RestApiOperation> ExtractOperations(
        OpenApiDocument document, OpenApiCoreConfig config, Uri? baseUri = null)
    {
        var operations = new List<RestApiOperation>();

        // Resolve the first global server URL and capture its variables
        var globalServer = document.Servers?.FirstOrDefault();
        var globalServers = document.Servers?.Select(s => ResolveServerUrl(s.Url, baseUri)).ToList() ?? [];
        IReadOnlyDictionary<string, RestApiServerVariable> globalServerVariables = globalServer != null
            ? ExtractServerVariables(globalServer)
            : new Dictionary<string, RestApiServerVariable>();

        foreach (var pathPair in document.Paths)
        {
            foreach (var opPair in pathPair.Value.Operations)
            {
                var method = opPair.Key.ToString();
                var op = opPair.Value;

                var context = new OperationSelectionContext
                {
                    Id = op.OperationId,
                    Path = pathPair.Key,
                    Method = method,
                    Description = op.Description ?? op.Summary
                };

                if (!ShouldIncludeOperation(context, config)) continue;

                var parameters = CreateParameters(
                    op.OperationId,
                    (op.Parameters ?? []).Union(pathPair.Value.Parameters ?? [], ParameterComparer.Instance));
                var payload = CreatePayload(op.OperationId, op.RequestBody);

                // Determine effective server URL and variables using the hierarchy:
                // operation-level > path-level > global
                OpenApiServer? effectiveApiServer =
                    op.Servers?.FirstOrDefault()
                    ?? pathPair.Value.Servers?.FirstOrDefault()
                    ?? globalServer;

                var serverUrl = effectiveApiServer != null
                    ? ResolveServerUrl(effectiveApiServer.Url, baseUri)
                    : globalServers.FirstOrDefault();

                var serverVariables = effectiveApiServer != null && effectiveApiServer != globalServer
                    ? ExtractServerVariables(effectiveApiServer)
                    : globalServerVariables;

                operations.Add(new RestApiOperation
                {
                    Id = op.OperationId,
                    Path = pathPair.Key,
                    Method = new HttpMethod(method),
                    Description = string.IsNullOrEmpty(op.Description) ? op.Summary : op.Description,
                    Parameters = parameters,
                    Payload = payload,
                    ServerUrl = serverUrl,
                    ServerVariables = serverVariables,
                    ResponseSchemas = ExtractResponseSchemas(op)
                });
            }
        }

        return operations;
    }

    /// <summary>
    /// Extracts server variables from an <see cref="OpenApiServer"/> into the HPD model.
    /// Returns an empty dictionary when the server has no variables.
    /// </summary>
    private static IReadOnlyDictionary<string, RestApiServerVariable> ExtractServerVariables(
        OpenApiServer server)
    {
        if (server.Variables is not { Count: > 0 } vars)
            return new Dictionary<string, RestApiServerVariable>();

        return vars.ToDictionary(
            kv => kv.Key,
            kv => new RestApiServerVariable
            {
                Default = kv.Value.Default ?? string.Empty,
                Description = kv.Value.Description,
                Enum = kv.Value.Enum?.Count > 0
                    ? kv.Value.Enum.ToList().AsReadOnly()
                    : null
            });
    }

    /// <summary>
    /// Extracts response schemas from an operation, keyed by status code string
    /// (e.g. "200", "404", "default"). Only responses that define a JSON schema are included.
    /// Used to attach <c>ExpectedSchema</c> to <see cref="OpenApiOperationResponse"/> so the
    /// LLM can reason about truncated or sparse results.
    /// </summary>
    private static IReadOnlyDictionary<string, JsonElement> ExtractResponseSchemas(
        OpenApiOperation operation)
    {
        if (operation.Responses is not { Count: > 0 }) return new Dictionary<string, JsonElement>();

        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (statusCode, response) in operation.Responses)
        {
            if (response?.Content == null) continue;
            var mediaType = GetSupportedMediaType(response.Content);
            if (mediaType == null) continue;
            var schema = response.Content[mediaType].Schema;
            if (schema == null) continue;
            try { result[statusCode] = schema.ToJsonSchema(); }
            catch { /* Skip schemas that can't be serialized */ }
        }
        return result;
    }

    /// <summary>
    /// Resolves a server URL from the spec. If the URL is relative (e.g. "/api/v3"), it is
    /// resolved against <paramref name="baseUri"/> (the location of the spec itself) so that
    /// HTTP requests are sent to the correct host. Absolute URLs are returned unchanged.
    /// </summary>
    private static string? ResolveServerUrl(string? serverUrl, Uri? baseUri)
    {
        if (string.IsNullOrEmpty(serverUrl)) return serverUrl;
        if (baseUri == null) return serverUrl;

        // Uri.IsWellFormedUriString with absolute kind catches http://, https://, etc.
        if (Uri.TryCreate(serverUrl, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            return serverUrl;

        // Relative path — resolve against the spec's base URI
        if (Uri.TryCreate(baseUri, serverUrl, out var resolved)
            && (resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps))
            return resolved.ToString();

        return serverUrl;
    }

    private static bool ShouldIncludeOperation(
        OperationSelectionContext context, OpenApiCoreConfig config)
    {
        if (config.OperationSelectionPredicate is { } predicate)
            return predicate(context);

        if (config.OperationsToExclude is { Count: > 0 } excludeList)
            return !excludeList.Contains(context.Id ?? "");

        return true;
    }

    private sealed class ParameterComparer : IEqualityComparer<IOpenApiParameter>
    {
        public static readonly ParameterComparer Instance = new();

        public bool Equals(IOpenApiParameter? x, IOpenApiParameter? y) =>
            x is not null && y is not null && x.Name == y.Name && x.In == y.In;

        public int GetHashCode(IOpenApiParameter obj) => HashCode.Combine(obj.Name, obj.In);
    }

    private static List<RestApiParameter> CreateParameters(
        string? operationId, IEnumerable<IOpenApiParameter> parameters)
    {
        var result = new List<RestApiParameter>();
        foreach (var param in parameters)
        {
            if (param.In is null)
                throw new OpenApiParseException(
                    $"Parameter location of '{param.Name}' in operation " +
                    $"'{operationId ?? "(unnamed)"}' is undefined.");

            // Serialize the schema first (resolves $ref inline via InlineLocalReferences).
            // When param.Schema is a $ref proxy, .Type/.Format/.Items are null on the proxy
            // object itself — read them from the already-resolved JSON so that component
            // $ref parameters (common in Stripe, GitHub specs) yield correct metadata.
            var schemaJson = param.Schema?.ToJsonSchema();

            var type = param.Schema?.Type.ToTypeString() ?? GetStringFromSchema(schemaJson, "type");
            var format = param.Schema?.Format ?? GetStringFromSchema(schemaJson, "format");
            var arrayItemType = param.Schema?.Items?.Type.ToTypeString()
                ?? (type == "array" ? GetStringFromSchema(GetNestedSchema(schemaJson, "items"), "type") : null);

            result.Add(new RestApiParameter
            {
                Name = param.Name,
                Type = type,
                Format = format,
                IsRequired = param.Required,
                Location = Enum.Parse<RestApiParameterLocation>(param.In.ToString()!),
                Description = param.Description,
                DefaultValue = GetPrimitiveValue(param.Schema?.Default),
                Schema = schemaJson,
                ArrayItemType = arrayItemType,
                Expand = param.Explode
            });
        }
        return result;
    }

    private static RestApiPayload? CreatePayload(string? operationId, IOpenApiRequestBody? requestBody)
    {
        if (requestBody?.Content is null) return null;
        var mediaType = GetSupportedMediaType(requestBody.Content);
        if (mediaType is null) return null;
        var mediaTypeMetadata = requestBody.Content[mediaType];
        return new RestApiPayload
        {
            MediaType = mediaType,
            Description = requestBody.Description,
            Properties = GetPayloadProperties(operationId, mediaTypeMetadata.Schema),
            Schema = mediaTypeMetadata.Schema?.ToJsonSchema()
        };
    }

    private static List<RestApiPayloadProperty> GetPayloadProperties(
        string? operationId, IOpenApiSchema? schema, int level = 0)
    {
        if (schema is null) return [];
        if (level > PayloadPropertiesHierarchyMaxDepth)
            throw new OpenApiParseException(
                $"Max depth {PayloadPropertiesHierarchyMaxDepth} exceeded for " +
                $"payload of '{operationId ?? "(unnamed)"}'.");

        // Collect all schemas that contribute properties: the schema itself plus any
        // allOf/anyOf/oneOf sub-schemas. This handles the common pattern where request
        // bodies are composed via allOf (e.g. Stripe, Kubernetes, GitHub specs).
        var schemas = new List<IOpenApiSchema> { schema };
        if (schema.AllOf?.Count > 0) schemas.AddRange(schema.AllOf);
        if (schema.AnyOf?.Count > 0) schemas.AddRange(schema.AnyOf);
        if (schema.OneOf?.Count > 0) schemas.AddRange(schema.OneOf);

        // Merge required sets across all sub-schemas so IsRequired is accurate.
        var allRequired = new HashSet<string>(schema.Required);
        foreach (var s in schemas.Skip(1))
            foreach (var r in s.Required)
                allRequired.Add(r);

        var result = new List<RestApiPayloadProperty>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var subSchema in schemas)
        {
            foreach (var prop in subSchema.Properties)
            {
                // Operation-level properties win (first definition wins per OpenAPI spec semantics).
                if (!seen.Add(prop.Key)) continue;

                result.Add(new RestApiPayloadProperty
                {
                    Name = prop.Key,
                    Type = prop.Value.Type.ToTypeString(),
                    Format = prop.Value.Format,
                    IsRequired = allRequired.Contains(prop.Key),
                    Description = prop.Value.Description,
                    Properties = GetPayloadProperties(operationId, prop.Value, level + 1),
                    Schema = prop.Value.ToJsonSchema(),
                    DefaultValue = GetPrimitiveValue(prop.Value.Default)
                });
            }
        }
        return result;
    }

    /// <summary>
    /// Reads a top-level string property from a serialized JSON schema element.
    /// Used to extract type/format from $ref-resolved schemas where the proxy object has null properties.
    /// </summary>
    private static string? GetStringFromSchema(JsonElement? schema, string propertyName)
    {
        if (schema is not { } s) return null;
        return s.ValueKind == JsonValueKind.Object
            && s.TryGetProperty(propertyName, out var prop)
            && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    /// <summary>
    /// Returns the nested JSON element at the given key within a schema element, or null.
    /// </summary>
    private static JsonElement? GetNestedSchema(JsonElement? schema, string key)
    {
        if (schema is not { } s || s.ValueKind != JsonValueKind.Object) return null;
        return s.TryGetProperty(key, out var nested) ? nested : null;
    }

    private static string? GetSupportedMediaType(IDictionary<string, OpenApiMediaType> content)
    {
        foreach (var supported in s_supportedMediaTypes)
            foreach (var key in content.Keys)
                if (key.Split(';')[0].Equals(supported, StringComparison.OrdinalIgnoreCase))
                    return key;
        return null;
    }

    private static object? GetPrimitiveValue(JsonNode? value) => value switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<int>(out var i) => i,
        JsonValue v when v.TryGetValue<long>(out var l) => l,
        JsonValue v when v.TryGetValue<float>(out var f) => f,
        JsonValue v when v.TryGetValue<double>(out var d) => d,
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        _ => null
    };

    private static void AssertReadingSuccessful(ReadResult result, bool ignoreErrors)
    {
        if (result.Document is null)
            throw new OpenApiParseException("Failed to parse OpenAPI document: document is null.");

        var errors = result.Diagnostic?.Errors ?? [];
        if (errors.Count > 0 && !ignoreErrors)
        {
            var messages = string.Join("; ", errors.Select(e => e.Message));
            throw new OpenApiParseException($"OpenAPI document has errors: {messages}");
        }
    }

    private static RestApiInfo ExtractInfo(OpenApiDocument document) => new()
    {
        Title = document.Info?.Title,
        Description = document.Info?.Description,
        Version = document.Info?.Version
    };
}
