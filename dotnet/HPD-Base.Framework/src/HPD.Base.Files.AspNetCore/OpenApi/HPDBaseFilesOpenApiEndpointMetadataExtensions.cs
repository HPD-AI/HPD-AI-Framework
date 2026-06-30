using HPD.Base.Files.AspNetCore.Http;
using HPD.Base.Files.Objects;
using HPD.Base.Files.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace HPD.Base.Files.AspNetCore.OpenApi;

public static class HPDBaseFilesOpenApiEndpointMetadataExtensions
{
    private static readonly System.Reflection.MethodInfo s_openApiHandlerMethod =
        ((Func<HttpContext, Task>)OpenApiHandlerStub).Method;

    public static IEndpointConventionBuilder WithHPDBaseFilesOpenApi(this IEndpointConventionBuilder builder, string operationId)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var metadata = operationId switch
        {
            FileHttpRouteNames.Upload => Metadata(operationId, "Upload file object", "Uploads a file object stream into a BASE bucket.", FileFeatureIds.Upload),
            FileHttpRouteNames.Download => Metadata(operationId, "Download file object", "Streams a file object from a BASE bucket.", FileFeatureIds.Download),
            FileHttpRouteNames.Head => Metadata(operationId, "Head file object", "Returns file object headers without a response body.", FileFeatureIds.MetadataRead),
            FileHttpRouteNames.MetadataGet => Metadata(operationId, "Get file object metadata", "Returns public-safe file object metadata.", FileFeatureIds.MetadataRead),
            FileHttpRouteNames.Delete => Metadata(operationId, "Delete file object", "Deletes a file object after file policy allows the operation.", FileFeatureIds.Delete),
            FileHttpRouteNames.List => Metadata(operationId, "List file objects", "Lists public-safe file object metadata in a BASE bucket.", FileFeatureIds.List),
            _ => Metadata(operationId, operationId, "HPD.BASE files route.", FileFeatureIds.BucketDescribe)
        };

        builder.WithName(operationId);
        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(s_openApiHandlerMethod);
            endpointBuilder.Metadata.Add(metadata);
            endpointBuilder.Metadata.Add(new TagsAttribute("Files"));
            endpointBuilder.Metadata.Add(new HPDBaseFilesOpenApiTagsMetadata(metadata.Tags));
            endpointBuilder.Metadata.Add(new HPDBaseFilesOpenApiSummaryMetadata(metadata.Summary));
            endpointBuilder.Metadata.Add(new HPDBaseFilesOpenApiDescriptionMetadata(metadata.Description));
            AddProblemMetadata(endpointBuilder);

            if (operationId == FileHttpRouteNames.Upload)
                Produces<FileObjectUploadResult>(endpointBuilder, StatusCodes.Status201Created);
            else if (operationId == FileHttpRouteNames.Download)
                Produces(endpointBuilder, typeof(Stream), StatusCodes.Status200OK, "application/octet-stream");
            else if (operationId == FileHttpRouteNames.Head || operationId == FileHttpRouteNames.Delete)
                Produces(endpointBuilder, typeof(void), StatusCodes.Status204NoContent, "application/json");
            else if (operationId == FileHttpRouteNames.List)
                Produces<FileObjectListResult>(endpointBuilder);
            else
                Produces<FileObjectMetadata>(endpointBuilder);
        });

        return builder;
    }

    private static void AddProblemMetadata(EndpointBuilder builder)
    {
        Produces<ProblemDetails>(builder, StatusCodes.Status400BadRequest, "application/problem+json");
        Produces<ProblemDetails>(builder, StatusCodes.Status401Unauthorized, "application/problem+json");
        Produces<ProblemDetails>(builder, StatusCodes.Status403Forbidden, "application/problem+json");
        Produces<ProblemDetails>(builder, StatusCodes.Status404NotFound, "application/problem+json");
        Produces<ProblemDetails>(builder, StatusCodes.Status424FailedDependency, "application/problem+json");
    }

    private static void Produces<T>(EndpointBuilder builder, int statusCode = StatusCodes.Status200OK, string contentType = "application/json") =>
        Produces(builder, typeof(T), statusCode, contentType);

    private static void Produces(EndpointBuilder builder, Type? type, int statusCode, string contentType) =>
        builder.Metadata.Add(new ProducesMetadata(type, statusCode, contentType));

    private static HPDBaseFilesOpenApiMetadata Metadata(string operationId, string summary, string description, string featureId) =>
        new(operationId, summary, description, ["Files"], [featureId]);

    private sealed record HPDBaseFilesOpenApiTagsMetadata(IReadOnlyList<string> Tags) : ITagsMetadata;

    private sealed record HPDBaseFilesOpenApiSummaryMetadata(string Summary) : IEndpointSummaryMetadata;

    private sealed record HPDBaseFilesOpenApiDescriptionMetadata(string Description) : IEndpointDescriptionMetadata;

    private sealed record ProducesMetadata(Type? Type, int StatusCode, string ContentType) : IProducesResponseTypeMetadata
    {
        public IEnumerable<string> ContentTypes => Type == typeof(void) ? [] : [ContentType];
    }

    private static Task OpenApiHandlerStub(HttpContext httpContext)
    {
        _ = httpContext;
        return Task.CompletedTask;
    }
}
