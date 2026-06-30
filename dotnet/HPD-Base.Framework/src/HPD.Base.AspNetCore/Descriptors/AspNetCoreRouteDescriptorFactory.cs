using HPD.Base;
using HPD.Base.AspNetCore.Http;
using HPD.Base.Descriptors;

namespace HPD.Base.AspNetCore.Descriptors;

internal static class AspNetCoreRouteDescriptorFactory
{
    public static RouteDescriptor[] Create() =>
    [
        Route(BaseRouteIds.Manifest, HttpMethodKind.Get, "/base/manifest", BaseDtoIds.Manifest),
        Route(BaseHttpRouteNames.AdminManifest, HttpMethodKind.Get, "/base/admin/manifest", BaseDtoIds.Manifest, VisibilityLevel.Admin, RouteAuthRequirement.Admin, requiredFeatureIds: AspNetCoreProjectionFeatureIds.ProjectionAspNetAdmin),
        Route(BaseRouteIds.Capabilities, HttpMethodKind.Get, "/base/capabilities", BaseDtoIds.CapabilityDescriptor, requiredFeatureIds: BaseFeatureIds.CapabilitiesRead),
        Route(BaseHttpRouteNames.AdminCapabilities, HttpMethodKind.Get, "/base/admin/capabilities", BaseDtoIds.CapabilityDescriptor, VisibilityLevel.Admin, RouteAuthRequirement.Admin, requiredFeatureIds: [BaseFeatureIds.CapabilitiesRead, AspNetCoreProjectionFeatureIds.ProjectionAspNetAdmin]),
        Route(BaseRouteIds.Schema, HttpMethodKind.Get, "/base/schema", BaseDtoIds.SchemaMetadata, requiredFeatureIds: BaseFeatureIds.SchemaRead),
        Route(BaseHttpRouteNames.AdminSchema, HttpMethodKind.Get, "/base/admin/schema", BaseDtoIds.SchemaMetadata, VisibilityLevel.Admin, RouteAuthRequirement.Admin, requiredFeatureIds: [BaseFeatureIds.SchemaRead, AspNetCoreProjectionFeatureIds.ProjectionAspNetAdmin]),
        Route(BaseHttpRouteNames.CollectionsList, HttpMethodKind.Get, "/base/collections", AspNetCoreDtoContractDescriptorFactory.CollectionDefinitionArray, requiredFeatureIds: BaseFeatureIds.SchemaRead),
        Route(BaseHttpRouteNames.CollectionsGet, HttpMethodKind.Get, "/base/collections/{collectionId}", AspNetCoreDtoContractDescriptorFactory.CollectionDefinition, requiredFeatureIds: BaseFeatureIds.SchemaRead),
        Route(BaseHttpRouteNames.AdminCollectionsList, HttpMethodKind.Get, "/base/admin/collections", AspNetCoreDtoContractDescriptorFactory.CollectionDefinitionArray, VisibilityLevel.Admin, RouteAuthRequirement.Admin, requiredFeatureIds: [BaseFeatureIds.SchemaRead, AspNetCoreProjectionFeatureIds.ProjectionAspNetAdmin]),
        Route(BaseHttpRouteNames.AdminCollectionsGet, HttpMethodKind.Get, "/base/admin/collections/{collectionId}", AspNetCoreDtoContractDescriptorFactory.CollectionDefinition, VisibilityLevel.Admin, RouteAuthRequirement.Admin, requiredFeatureIds: [BaseFeatureIds.SchemaRead, AspNetCoreProjectionFeatureIds.ProjectionAspNetAdmin]),
        Route(BaseRouteIds.Health, HttpMethodKind.Get, "/base/health", AspNetCoreDtoContractDescriptorFactory.HealthDescriptorArray, requiredFeatureIds: BaseFeatureIds.HealthRead),
        Route(BaseHttpRouteNames.AdminHealth, HttpMethodKind.Get, "/base/admin/health", AspNetCoreDtoContractDescriptorFactory.HealthDescriptorArray, VisibilityLevel.Admin, RouteAuthRequirement.Admin, requiredFeatureIds: [BaseFeatureIds.HealthRead, AspNetCoreProjectionFeatureIds.ProjectionAspNetAdmin]),
        Route(BaseRouteIds.Diagnostics, HttpMethodKind.Get, "/base/diagnostics", AspNetCoreDtoContractDescriptorFactory.DiagnosticDescriptorArray, requiredFeatureIds: BaseFeatureIds.DiagnosticsRead),
        Route(BaseHttpRouteNames.AdminDiagnostics, HttpMethodKind.Get, "/base/admin/diagnostics", AspNetCoreDtoContractDescriptorFactory.DiagnosticDescriptorArray, VisibilityLevel.Admin, RouteAuthRequirement.Admin, requiredFeatureIds: [BaseFeatureIds.DiagnosticsRead, AspNetCoreProjectionFeatureIds.ProjectionAspNetAdmin]),
        Route(BaseHttpRouteNames.AdminPolicyExplain, HttpMethodKind.Post, "/base/admin/policy/explain", AspNetCoreDtoContractDescriptorFactory.BasePolicyExplainResponse, VisibilityLevel.Admin, RouteAuthRequirement.Admin, AspNetCoreDtoContractDescriptorFactory.BasePolicyExplainRequest, requiredFeatureIds: ["policy.explain.admin", AspNetCoreProjectionFeatureIds.ProjectionAspNetAdmin]),
        Route(BaseRouteIds.RecordsList, HttpMethodKind.Get, "/base/collections/{collectionId}/records", BaseDtoIds.RecordPage, requiredFeatureIds: [BaseFeatureIds.RecordsList, AspNetCoreProjectionFeatureIds.ProjectionAspNet]),
        Route(BaseRouteIds.RecordsQuery, HttpMethodKind.Post, "/base/collections/{collectionId}/query", BaseDtoIds.RecordPage, requestDtoId: AspNetCoreDtoContractDescriptorFactory.RecordQuery, requiredFeatureIds: [BaseFeatureIds.RecordsQuery, AspNetCoreProjectionFeatureIds.ProjectionAspNet]),
        Route(BaseRouteIds.RecordsGet, HttpMethodKind.Get, "/base/collections/{collectionId}/records/{id}", BaseDtoIds.RecordEnvelope, requiredFeatureIds: [BaseFeatureIds.RecordsGet, AspNetCoreProjectionFeatureIds.ProjectionAspNet]),
        Route(BaseRouteIds.RecordsCreate, HttpMethodKind.Post, "/base/collections/{collectionId}/records", BaseDtoIds.RecordEnvelope, requestDtoId: AspNetCoreDtoContractDescriptorFactory.RecordCreateRequest, requiredFeatureIds: [BaseFeatureIds.RecordsCreate, AspNetCoreProjectionFeatureIds.ProjectionAspNet]),
        Route(BaseRouteIds.RecordsPatch, HttpMethodKind.Patch, "/base/collections/{collectionId}/records/{id}", BaseDtoIds.RecordEnvelope, requestDtoId: AspNetCoreDtoContractDescriptorFactory.RecordPatchRequest, requiredFeatureIds: [BaseFeatureIds.RecordsPatch, AspNetCoreProjectionFeatureIds.ProjectionAspNet]),
        Route(BaseRouteIds.RecordsReplace, HttpMethodKind.Put, "/base/collections/{collectionId}/records/{id}", BaseDtoIds.RecordEnvelope, requestDtoId: AspNetCoreDtoContractDescriptorFactory.RecordReplaceRequest, requiredFeatureIds: [BaseFeatureIds.RecordsReplace, AspNetCoreProjectionFeatureIds.ProjectionAspNet]),
        Route(BaseRouteIds.RecordsDelete, HttpMethodKind.Delete, "/base/collections/{collectionId}/records/{id}", AspNetCoreDtoContractDescriptorFactory.DeleteResult, requestDtoId: AspNetCoreDtoContractDescriptorFactory.RecordDeleteRequest, requiredFeatureIds: [BaseFeatureIds.RecordsDelete, AspNetCoreProjectionFeatureIds.ProjectionAspNet])
    ];

    private static RouteDescriptor Route(
        string operationId,
        HttpMethodKind method,
        string path,
        string responseDtoId,
        VisibilityLevel visibility = VisibilityLevel.Public,
        RouteAuthRequirement authRequirement = RouteAuthRequirement.None,
        string? requestDtoId = null,
        params string[] requiredFeatureIds) => new()
        {
            OperationId = operationId,
            Method = method,
            Path = path,
            Visibility = visibility,
            AuthRequirement = authRequirement,
            RequestDtoId = requestDtoId,
            ResponseDtoId = responseDtoId,
            ErrorDtoId = AspNetCoreDtoContractDescriptorFactory.ProblemDetails,
            RequiredFeatureIds = requiredFeatureIds.Length == 0
                ? [AspNetCoreProjectionFeatureIds.ProjectionAspNet]
                : requiredFeatureIds
        };
}
