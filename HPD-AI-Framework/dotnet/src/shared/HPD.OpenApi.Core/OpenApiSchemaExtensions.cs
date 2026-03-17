using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Models.Interfaces;
using Microsoft.OpenApi.Writers;

namespace HPD.OpenApi.Core;

/// <summary>
/// Converts OpenAPI schemas to JSON Schema format.
///
/// Key insight: OpenAPI v3 schema IS effectively JSON Schema (Draft 7 superset).
/// SerializeAsV3 with InlineLocalReferences produces valid JSON Schema with $ref inlining.
///
/// Public so that future consumers of HPD.OpenApi.Core (e.g. HPD.Integrations.Http)
/// can convert schemas on-demand for typed parameter metadata in workflow configuration UIs.
/// </summary>
public static class OpenApiSchemaExtensions
{
    /// <summary>
    /// Converts an OpenAPI schema to a <see cref="JsonElement"/> representing the equivalent JSON Schema.
    /// Uses SerializeAsV3 with InlineLocalReferences to resolve $ref pointers inline.
    /// </summary>
    public static JsonElement ToJsonSchema(this IOpenApiSchema schema)
    {
        var sb = new StringBuilder();
        var writer = new OpenApiJsonWriter(new StringWriter(sb, CultureInfo.InvariantCulture));
        writer.Settings.InlineLocalReferences = true;
        schema.SerializeAsV3(writer);
        return JsonDocument.Parse(sb.ToString()).RootElement.Clone();
    }

    /// <summary>Converts a <see cref="JsonSchemaType"/> flags enum to its JSON Schema string representation.</summary>
    public static string? ToTypeString(this JsonSchemaType? type) => type switch
    {
        JsonSchemaType.String => "string",
        JsonSchemaType.Integer => "integer",
        JsonSchemaType.Number => "number",
        JsonSchemaType.Boolean => "boolean",
        JsonSchemaType.Array => "array",
        JsonSchemaType.Object => "object",
        JsonSchemaType.Null => "null",
        _ => null
    };
}
