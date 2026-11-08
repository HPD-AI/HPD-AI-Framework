using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace HPD.Agent.AspNetCore.OpenApi;

/// <summary>
/// Preserves nullable reference metadata that the default schema transformer
/// can omit for required properties such as a thread's optional active execution.
/// </summary>
internal sealed class HPDAgentOpenApiSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var property = context.JsonPropertyInfo;
        if (property is null ||
            property.PropertyType.IsValueType ||
            !(property.IsGetNullable || property.IsSetNullable))
        {
            return Task.CompletedTask;
        }

        schema.Type = (schema.Type ?? JsonSchemaType.Null) | JsonSchemaType.Null;
        return Task.CompletedTask;
    }
}
