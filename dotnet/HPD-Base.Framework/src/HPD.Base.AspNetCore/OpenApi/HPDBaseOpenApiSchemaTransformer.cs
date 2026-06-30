using System.Text.Json;
using HPD.Base.Records;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace HPD.Base.AspNetCore.OpenApi;

internal sealed class HPDBaseOpenApiSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;

        if (type == typeof(RecordId) || type == typeof(RevisionToken))
        {
            schema.Type = JsonSchemaType.String;
            schema.Properties?.Clear();
            schema.Required?.Clear();
            schema.Description ??= type == typeof(RecordId)
                ? "Opaque HPD.BASE record id."
                : "Opaque HPD.BASE revision token.";
        }
        else if (type == typeof(JsonElement))
        {
            schema.Type = null;
            schema.Properties?.Clear();
            schema.Required?.Clear();
            schema.Description ??= "Arbitrary JSON value.";
        }
        else if (type == typeof(RecordPayload))
        {
            schema.Description ??= "Record payload containing either arbitrary JSON or a field map.";
            if (schema.Properties is not null)
            {
                if (schema.Properties.TryGetValue("json", out var jsonSchema) && jsonSchema is OpenApiSchema concreteJsonSchema)
                {
                    concreteJsonSchema.Type = null;
                    concreteJsonSchema.Properties?.Clear();
                    concreteJsonSchema.Description ??= "Arbitrary JSON document payload.";
                }

                if (schema.Properties.TryGetValue("fields", out var fieldsSchema))
                    fieldsSchema.Description ??= "Field map whose values may be any JSON value.";
            }
        }
        else if (type == typeof(ProblemDetails))
        {
            schema.Description ??= "RFC 7807 problem details with HPD.BASE extensions.";
            schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
            schema.Properties.TryAdd("hpd.status", StringSchema("HPD operation status."));
            schema.Properties.TryAdd("hpd.error.code", StringSchema("Stable HPD error code."));
            schema.Properties.TryAdd("hpd.error.category", StringSchema("HPD error category."));
            schema.Properties.TryAdd("hpd.error.target", StringSchema("Field or resource targeted by the error."));
            schema.Properties.TryAdd("hpd.error.correlationId", StringSchema("Correlation id associated with the error."));
            schema.Properties.TryAdd("hpd.validation", AnySchema("Validation details."));
            schema.Properties.TryAdd("hpd.conflict", AnySchema("Conflict details."));
            schema.Properties.TryAdd("hpd.capability", AnySchema("Capability failure details."));
            schema.Properties.TryAdd("hpd.policy", AnySchema("Policy failure details."));
            schema.Properties.TryAdd("hpd.store", AnySchema("Store failure details."));
            schema.Properties.TryAdd("hpd.warnings", AnySchema("Warning details."));
            schema.Properties.TryAdd("hpd.diagnostics", AnySchema("Diagnostic details."));
        }

        return Task.CompletedTask;
    }

    private static OpenApiSchema StringSchema(string description) =>
        new() { Type = JsonSchemaType.String, Description = description };

    private static OpenApiSchema AnySchema(string description) =>
        new() { Description = description };
}
