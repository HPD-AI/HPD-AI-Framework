using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Mvc;

namespace HPD.Base.AspNetCore.OpenApi;

internal static class HPDBaseOpenApiSchemaReferenceIds
{
    public static string? Create(JsonTypeInfo jsonTypeInfo) =>
        jsonTypeInfo.Type == typeof(ProblemDetails)
            ? nameof(ProblemDetails)
            : OpenApiOptions.CreateDefaultSchemaReferenceId(jsonTypeInfo);
}
