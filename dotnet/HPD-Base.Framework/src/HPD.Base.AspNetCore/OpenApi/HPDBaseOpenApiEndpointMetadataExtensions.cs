using HPD.Base.AspNetCore.Http;
using HPD.Base.AspNetCore.Descriptors;
using HPD.Base.Descriptors;
using HPD.Base.Health;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Policy.Admin;
using HPD.Base.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace HPD.Base.AspNetCore.OpenApi;

internal static class HPDBaseOpenApiEndpointMetadataExtensions
{
    private static readonly System.Reflection.MethodInfo s_openApiHandlerMethod =
        ((Func<HttpContext, Task>)OpenApiHandlerStub).Method;

    private static readonly IReadOnlyDictionary<string, RouteDescriptor> s_routeDescriptors =
        AspNetCoreRouteDescriptorFactory.Create().ToDictionary(static descriptor => descriptor.OperationId, StringComparer.Ordinal);

    public static IEndpointConventionBuilder WithHPDBaseOpenApi(this IEndpointConventionBuilder builder, string operationId)
    {
        var metadata = Create(operationId);
        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(s_openApiHandlerMethod);
            endpointBuilder.Metadata.Add(metadata);
            endpointBuilder.Metadata.Add(new HPDBaseOpenApiTagsMetadata(metadata.Tags));
            endpointBuilder.Metadata.Add(new HPDBaseOpenApiSummaryMetadata(metadata.Summary));
            endpointBuilder.Metadata.Add(new HPDBaseOpenApiDescriptionMetadata(metadata.Description));
            AddProblemMetadata(endpointBuilder);
            ApplyTypedMetadata(endpointBuilder, operationId);
        });

        return builder;
    }

    private static void ApplyTypedMetadata(EndpointBuilder builder, string operationId)
    {
        switch (operationId)
        {
            case BaseRouteIds.Manifest:
            case BaseHttpRouteNames.AdminManifest:
                Produces<BaseManifest>(builder);
                break;
            case BaseRouteIds.Capabilities:
            case BaseHttpRouteNames.AdminCapabilities:
                Produces<CapabilityDescriptor>(builder);
                break;
            case BaseRouteIds.Schema:
            case BaseHttpRouteNames.AdminSchema:
                Produces<SchemaMetadata>(builder);
                break;
            case BaseHttpRouteNames.CollectionsList:
            case BaseHttpRouteNames.AdminCollectionsList:
                Produces<CollectionDefinition[]>(builder);
                break;
            case BaseHttpRouteNames.CollectionsGet:
            case BaseHttpRouteNames.AdminCollectionsGet:
                Produces<CollectionDefinition>(builder);
                break;
            case BaseRouteIds.Health:
            case BaseHttpRouteNames.AdminHealth:
                Produces<HealthDescriptor[]>(builder);
                break;
            case BaseRouteIds.Diagnostics:
            case BaseHttpRouteNames.AdminDiagnostics:
                Produces<DiagnosticDescriptor[]>(builder);
                break;
            case BaseRouteIds.RecordsList:
                Produces<RecordPage>(builder);
                break;
            case BaseRouteIds.RecordsQuery:
                Accepts<RecordQuery>(builder, isOptional: true);
                Produces<RecordPage>(builder);
                break;
            case BaseRouteIds.RecordsGet:
                Produces<RecordEnvelope>(builder);
                break;
            case BaseRouteIds.RecordsCreate:
                Accepts<RecordCreateRequest>(builder);
                Produces<RecordEnvelope>(builder, StatusCodes.Status201Created);
                break;
            case BaseRouteIds.RecordsPatch:
                Accepts<RecordPatchRequest>(builder);
                Produces<RecordEnvelope>(builder);
                break;
            case BaseRouteIds.RecordsReplace:
                Accepts<RecordReplaceRequest>(builder);
                Produces<RecordEnvelope>(builder);
                break;
            case BaseRouteIds.RecordsDelete:
                Accepts<RecordDeleteRequest>(builder, isOptional: true);
                Produces<DeleteResult>(builder);
                break;
            case BaseRouteIds.RecordsBatch:
                Accepts<BaseRecordBatchRequest>(builder);
                Produces<BaseRecordBatchResult>(builder);
                break;
            case BaseRouteIds.RecordsUpsert:
                Accepts<RecordUpsertRequest>(builder);
                Produces<RecordUpsertResult>(builder);
                break;
            case BaseHttpRouteNames.AdminPolicyExplain:
                Accepts<BasePolicyExplainRequest>(builder);
                Produces<BasePolicyExplainResponse>(builder);
                break;
        }
    }

    private static void AddProblemMetadata(EndpointBuilder builder)
    {
        Produces<ProblemDetails>(builder, StatusCodes.Status400BadRequest, "application/problem+json");
        Produces<ProblemDetails>(builder, StatusCodes.Status401Unauthorized, "application/problem+json");
        Produces<ProblemDetails>(builder, StatusCodes.Status403Forbidden, "application/problem+json");
        Produces<ProblemDetails>(builder, StatusCodes.Status404NotFound, "application/problem+json");
        Produces<ProblemDetails>(builder, StatusCodes.Status409Conflict, "application/problem+json");
        Produces<ProblemDetails>(builder, StatusCodes.Status429TooManyRequests, "application/problem+json");
        Produces<ProblemDetails>(builder, StatusCodes.Status500InternalServerError, "application/problem+json");
    }

    private static void Accepts<T>(EndpointBuilder builder, bool isOptional = false) =>
        builder.Metadata.Add(new AcceptsMetadata(["application/json"], typeof(T), isOptional));

    private static void Produces<T>(EndpointBuilder builder, int statusCode = StatusCodes.Status200OK, string contentType = "application/json") =>
        builder.Metadata.Add(new HPDBaseOpenApiProducesMetadata(typeof(T), statusCode, contentType));

    private static HPDBaseOpenApiRouteMetadata Create(string operationId) =>
        operationId switch
        {
            BaseRouteIds.Manifest => Public(operationId, "BASE manifest", "Returns the public BASE manifest.", "Metadata"),
            BaseRouteIds.Capabilities => Public(operationId, "BASE capabilities", "Returns public BASE runtime capabilities.", "Metadata"),
            BaseRouteIds.Schema => Public(operationId, "BASE schema", "Returns public schema metadata.", "Metadata"),
            BaseHttpRouteNames.CollectionsList => Public(operationId, "List collections", "Lists public collection definitions.", "Collections"),
            BaseHttpRouteNames.CollectionsGet => Public(operationId, "Get collection", "Returns one public collection definition.", "Collections"),
            BaseRouteIds.Health => Public(operationId, "BASE health", "Returns public health descriptors.", "Health"),
            BaseRouteIds.Diagnostics => Public(operationId, "BASE diagnostics", "Returns public diagnostic descriptors.", "Diagnostics"),
            BaseRouteIds.RecordsList => Record(operationId, "List records", "Lists records in a collection using query-string filters."),
            BaseRouteIds.RecordsQuery => Record(operationId, "Query records", "Lists records in a collection using a JSON query body."),
            BaseRouteIds.RecordsGet => Record(operationId, "Get record", "Returns one record by id."),
            BaseRouteIds.RecordsCreate => Record(operationId, "Create record", "Creates a record in a collection."),
            BaseRouteIds.RecordsPatch => Record(operationId, "Patch record", "Patches a record in a collection."),
            BaseRouteIds.RecordsReplace => Record(operationId, "Replace record", "Replaces a record in a collection."),
            BaseRouteIds.RecordsDelete => Record(operationId, "Delete record", "Deletes a record from a collection."),
            BaseRouteIds.RecordsBatch => Record(operationId, "Mutate records in a batch", "Executes a bounded ordered record-mutation batch."),
            BaseRouteIds.RecordsUpsert => Record(operationId, "Upsert record", "Atomically creates or updates one record by id."),
            BaseHttpRouteNames.AdminManifest => Admin(operationId, "Admin BASE manifest", "Returns the admin BASE manifest.", "Admin Metadata"),
            BaseHttpRouteNames.AdminCapabilities => Admin(operationId, "Admin BASE capabilities", "Returns admin BASE runtime capabilities.", "Admin Metadata"),
            BaseHttpRouteNames.AdminSchema => Admin(operationId, "Admin BASE schema", "Returns admin schema metadata.", "Admin Metadata"),
            BaseHttpRouteNames.AdminCollectionsList => Admin(operationId, "Admin list collections", "Lists admin collection definitions.", "Admin Collections"),
            BaseHttpRouteNames.AdminCollectionsGet => Admin(operationId, "Admin get collection", "Returns one admin collection definition.", "Admin Collections"),
            BaseHttpRouteNames.AdminHealth => Admin(operationId, "Admin BASE health", "Returns admin health descriptors.", "Admin Health"),
            BaseHttpRouteNames.AdminDiagnostics => Admin(operationId, "Admin BASE diagnostics", "Returns admin diagnostic descriptors.", "Admin Diagnostics"),
            BaseHttpRouteNames.AdminPolicyExplain => Admin(operationId, "Explain policy", "Explains an admin policy decision for diagnostics.", "Admin Policy"),
            _ => Public(operationId, operationId, "HPD.BASE endpoint.", "BASE")
        };

    private static HPDBaseOpenApiRouteMetadata Public(string operationId, string summary, string description, string tag) =>
        FromDescriptor(operationId, IsAdmin: false, IsRecord: false, summary, description, [tag]);

    private static HPDBaseOpenApiRouteMetadata Record(string operationId, string summary, string description) =>
        FromDescriptor(operationId, IsAdmin: false, IsRecord: true, summary, description, ["Records"]);

    private static HPDBaseOpenApiRouteMetadata Admin(string operationId, string summary, string description, string tag) =>
        FromDescriptor(operationId, IsAdmin: true, IsRecord: false, summary, description, [tag]);

    private static HPDBaseOpenApiRouteMetadata FromDescriptor(
        string operationId,
        bool IsAdmin,
        bool IsRecord,
        string summary,
        string description,
        string[] tags)
    {
        if (!s_routeDescriptors.TryGetValue(operationId, out var descriptor))
        {
            return new(
                operationId,
                IsAdmin,
                IsRecord,
                summary,
                description,
                tags,
                VisibilityLevel.Public.ToString(),
                RouteAuthRequirement.None.ToString(),
                RequestDtoId: null,
                ResponseDtoId: operationId,
                ErrorDtoId: AspNetCoreDtoContractDescriptorFactory.ProblemDetails,
                RequiredFeatureIds: []);
        }

        return new(
            operationId,
            IsAdmin,
            IsRecord,
            summary,
            description,
            tags,
            descriptor.Visibility.ToString(),
            descriptor.AuthRequirement.ToString(),
            descriptor.RequestDtoId,
            descriptor.ResponseDtoId,
            descriptor.ErrorDtoId ?? AspNetCoreDtoContractDescriptorFactory.ProblemDetails,
            descriptor.RequiredFeatureIds ?? []);
    }

    private sealed record HPDBaseOpenApiTagsMetadata(IReadOnlyList<string> Tags) : ITagsMetadata;

    private sealed record HPDBaseOpenApiSummaryMetadata(string Summary) : IEndpointSummaryMetadata;

    private sealed record HPDBaseOpenApiDescriptionMetadata(string Description) : IEndpointDescriptionMetadata;

    private sealed record HPDBaseOpenApiProducesMetadata(
        Type Type,
        int StatusCode,
        string ContentType) : IProducesResponseTypeMetadata
    {
        public string? Description => null;

        public IEnumerable<string> ContentTypes => [ContentType];
    }

    private static Task OpenApiHandlerStub(HttpContext httpContext)
    {
        _ = httpContext;
        return Task.CompletedTask;
    }
}
