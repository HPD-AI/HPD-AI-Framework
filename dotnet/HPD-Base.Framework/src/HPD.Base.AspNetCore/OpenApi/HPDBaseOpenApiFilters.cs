using HPD.Base;
using HPD.Base.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Routing;

namespace HPD.Base.AspNetCore.OpenApi;

internal static class HPDBaseOpenApiFilters
{
    private static readonly HashSet<string> s_publicOperationIds =
    [
        BaseRouteIds.Manifest,
        BaseRouteIds.Capabilities,
        BaseRouteIds.Schema,
        BaseHttpRouteNames.CollectionsList,
        BaseHttpRouteNames.CollectionsGet,
        BaseRouteIds.Health,
        BaseRouteIds.Diagnostics,
        BaseRouteIds.RecordsList,
        BaseRouteIds.RecordsQuery,
        BaseRouteIds.RecordsGet,
        BaseRouteIds.RecordsCreate,
        BaseRouteIds.RecordsPatch,
        BaseRouteIds.RecordsReplace,
        BaseRouteIds.RecordsDelete
    ];

    private static readonly HashSet<string> s_adminOperationIds =
    [
        BaseHttpRouteNames.AdminManifest,
        BaseHttpRouteNames.AdminCapabilities,
        BaseHttpRouteNames.AdminSchema,
        BaseHttpRouteNames.AdminCollectionsList,
        BaseHttpRouteNames.AdminCollectionsGet,
        BaseHttpRouteNames.AdminHealth,
        BaseHttpRouteNames.AdminDiagnostics,
        BaseHttpRouteNames.AdminPolicyExplain
    ];

    public static bool Public(ApiDescription description) =>
        description.ActionDescriptor.EndpointMetadata.OfType<HPDBaseOpenApiRouteMetadata>().FirstOrDefault() is { IsAdmin: false }
        || (OperationId(description) is { } operationId && s_publicOperationIds.Contains(operationId));

    public static bool Admin(ApiDescription description) =>
        description.ActionDescriptor.EndpointMetadata.OfType<HPDBaseOpenApiRouteMetadata>().FirstOrDefault() is { IsAdmin: true }
        || (OperationId(description) is { } operationId && s_adminOperationIds.Contains(operationId));

    private static string? OperationId(ApiDescription description) =>
        description.ActionDescriptor.EndpointMetadata.OfType<IEndpointNameMetadata>().FirstOrDefault()?.EndpointName
        ?? description.ActionDescriptor.AttributeRouteInfo?.Name;
}
