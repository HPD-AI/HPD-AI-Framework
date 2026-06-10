using System.Text.Json;

namespace HPD.OpenApi.Core.Model;

/// <summary>Parsed OpenAPI specification with info and operations.</summary>
public sealed class ParsedOpenApiSpec
{
    public RestApiInfo Info { get; init; } = new();
    public List<RestApiOperation> Operations { get; init; } = [];
}

/// <summary>API metadata from the info block of the spec.</summary>
public sealed class RestApiInfo
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Version { get; init; }
}

/// <summary>
/// A variable defined in an OpenAPI server URL template.
/// Allows specs to define parameterized base URLs like
/// <c>https://{region}.api.example.com/{version}</c>.
/// </summary>
public sealed class RestApiServerVariable
{
    /// <summary>
    /// Default value used when no argument is provided (or when the provided value fails enum validation).
    /// Required by the OpenAPI specification.
    /// </summary>
    public string Default { get; init; } = string.Empty;

    /// <summary>Optional description of this variable.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional enumeration of valid values. When set, only values in this list are accepted;
    /// any other value falls back to <see cref="Default"/>.
    /// </summary>
    public IReadOnlyList<string>? Enum { get; init; }

    /// <summary>
    /// Optional alternative argument name used to look up the value in the function arguments
    /// before falling back to the variable's own name.
    /// </summary>
    public string? ArgumentName { get; init; }

    /// <summary>
    /// Returns true if <paramref name="value"/> is acceptable — either no enum is defined,
    /// or the value is in the enum list.
    /// </summary>
    public bool IsValid(string? value) => Enum is null || Enum.Contains(value!);
}

/// <summary>A single REST API operation (endpoint + HTTP method).</summary>
public sealed class RestApiOperation
{
    /// <summary>Operation ID from the spec. May be null for unnamed operations.</summary>
    public string? Id { get; init; }

    /// <summary>Path template (e.g., "/pets/{petId}").</summary>
    public required string Path { get; init; }

    /// <summary>HTTP method for this operation.</summary>
    public required HttpMethod Method { get; init; }

    /// <summary>Human-readable description of what this operation does.</summary>
    public string? Description { get; init; }

    /// <summary>Path, query, header, and cookie parameters.</summary>
    public List<RestApiParameter> Parameters { get; init; } = [];

    /// <summary>Request body payload definition. Null for GET/DELETE operations with no body.</summary>
    public RestApiPayload? Payload { get; init; }

    /// <summary>
    /// Base server URL template for this operation (from spec server list, may be overridden).
    /// May contain <c>{variableName}</c> placeholders defined in <see cref="ServerVariables"/>.
    /// </summary>
    public string? ServerUrl { get; init; }

    /// <summary>
    /// Server variables for substitution into <see cref="ServerUrl"/>.
    /// Empty when the server URL has no template placeholders.
    /// </summary>
    public IReadOnlyDictionary<string, RestApiServerVariable> ServerVariables { get; init; }
        = new Dictionary<string, RestApiServerVariable>();

    /// <summary>
    /// Response schemas from the OpenAPI spec, keyed by status code string (e.g. "200", "404", "default").
    /// Used by <see cref="OpenApiOperationRunner"/> to attach <c>ExpectedSchema</c> to successful responses
    /// so the LLM can reason about truncated or sparse results.
    /// Empty when the spec defines no response schemas for this operation.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> ResponseSchemas { get; init; }
        = new Dictionary<string, JsonElement>();
}

/// <summary>A parameter attached to an operation (path, query, header, or cookie).</summary>
public sealed class RestApiParameter
{
    public required string Name { get; init; }
    public string? Type { get; init; }
    public string? Format { get; init; }
    public bool IsRequired { get; init; }
    public RestApiParameterLocation Location { get; init; }
    public string? Description { get; init; }
    public object? DefaultValue { get; init; }

    /// <summary>Full JSON Schema for this parameter, serialized from the spec schema.</summary>
    public JsonElement? Schema { get; init; }

    /// <summary>Element type for array parameters.</summary>
    public string? ArrayItemType { get; init; }

    /// <summary>Whether array/object parameters should be exploded (serialized as individual values).</summary>
    public bool Expand { get; init; }
}

/// <summary>Parameter location in the HTTP request.</summary>
public enum RestApiParameterLocation
{
    Query,
    Header,
    Path,
    Cookie,
    Body
}

/// <summary>Request body payload for operations that accept a body (POST, PUT, PATCH).</summary>
public sealed class RestApiPayload
{
    public required string MediaType { get; init; }
    public string? Description { get; init; }
    public List<RestApiPayloadProperty> Properties { get; init; } = [];
    public JsonElement? Schema { get; init; }
}

/// <summary>A single property within a request body payload.</summary>
public sealed class RestApiPayloadProperty
{
    public required string Name { get; init; }
    public string? Type { get; init; }
    public string? Format { get; init; }
    public bool IsRequired { get; init; }
    public string? Description { get; init; }
    public List<RestApiPayloadProperty> Properties { get; init; } = [];
    public JsonElement? Schema { get; init; }
    public object? DefaultValue { get; init; }
}
