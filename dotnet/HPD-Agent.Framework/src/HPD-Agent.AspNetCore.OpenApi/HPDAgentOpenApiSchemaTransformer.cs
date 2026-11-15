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
        if (context.JsonTypeInfo?.Type is { } schemaType)
            HPDAgentOpenApiPortableSchema.ApplyAuthenticationDiscriminator(schema, schemaType);

        var property = context.JsonPropertyInfo;
        if (property is null)
            return Task.CompletedTask;

        var isNullableValueType = Nullable.GetUnderlyingType(property.PropertyType) is not null;
        var isNullableReferenceType = !property.PropertyType.IsValueType &&
            (property.IsGetNullable || property.IsSetNullable);
        if (!isNullableValueType && !isNullableReferenceType)
            return Task.CompletedTask;

        if (schema.Type is { } type)
            schema.Type = type | JsonSchemaType.Null;
        return Task.CompletedTask;
    }
}
