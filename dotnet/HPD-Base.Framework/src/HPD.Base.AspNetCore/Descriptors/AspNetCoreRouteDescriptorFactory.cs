using HPD.Base;
using HPD.Base.AspNetCore;

namespace HPD.Base.AspNetCore;

internal static class AspNetCoreRouteDescriptorFactory
{
    /// <summary>Executes the create operation.</summary>
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
        Route(BaseRouteIds.RecordsQuery, HttpMethodKind.Post, "/base/collections/{collectionId}/records:query", BaseDtoIds.RecordPage, requestDtoId: AspNetCoreDtoContractDescriptorFactory.RecordQuery, requiredFeatureIds: [BaseFeatureIds.RecordsQuery, AspNetCoreProjectionFeatureIds.ProjectionAspNet]),
        Route(BaseRouteIds.RecordsGet, HttpMethodKind.Get, "/base/collections/{collectionId}/records/{id}", BaseDtoIds.RecordEnvelope, requiredFeatureIds: [BaseFeatureIds.RecordsGet, AspNetCoreProjectionFeatureIds.ProjectionAspNet]),
        Route(BaseRouteIds.RecordsCreate, HttpMethodKind.Post, "/base/collections/{collectionId}/records", BaseDtoIds.RecordEnvelope, requestDtoId: AspNetCoreDtoContractDescriptorFactory.RecordCreateRequest, requiredFeatureIds: [BaseFeatureIds.RecordsCreate, AspNetCoreProjectionFeatureIds.ProjectionAspNet]),
        Route(BaseRouteIds.RecordsPatch, HttpMethodKind.Patch, "/base/collections/{collectionId}/records/{id}", BaseDtoIds.RecordEnvelope, requestDtoId: AspNetCoreDtoContractDescriptorFactory.RecordPatchRequest, requiredFeatureIds: [BaseFeatureIds.RecordsPatch, AspNetCoreProjectionFeatureIds.ProjectionAspNet]),
        Route(BaseRouteIds.RecordsReplace, HttpMethodKind.Put, "/base/collections/{collectionId}/records/{id}", BaseDtoIds.RecordEnvelope, requestDtoId: AspNetCoreDtoContractDescriptorFactory.RecordReplaceRequest, requiredFeatureIds: [BaseFeatureIds.RecordsReplace, AspNetCoreProjectionFeatureIds.ProjectionAspNet]),
        Route(BaseRouteIds.RecordsDelete, HttpMethodKind.Delete, "/base/collections/{collectionId}/records/{id}", AspNetCoreDtoContractDescriptorFactory.DeleteResult, requestDtoId: AspNetCoreDtoContractDescriptorFactory.RecordDeleteRequest, requiredFeatureIds: [BaseFeatureIds.RecordsDelete, AspNetCoreProjectionFeatureIds.ProjectionAspNet]),
        Route(BaseRouteIds.RecordsBatch, HttpMethodKind.Post, "/base/records/batch", BaseDtoIds.BaseRecordBatchResult, requestDtoId: BaseDtoIds.BaseRecordBatchRequest, requiredFeatureIds: [BaseFeatureIds.RecordsBatch, AspNetCoreProjectionFeatureIds.ProjectionAspNet]),
        Route(BaseRouteIds.RecordsUpsert, HttpMethodKind.Put, "/base/collections/{collectionId}/records/{id}:upsert", BaseDtoIds.RecordUpsertResult, requestDtoId: BaseDtoIds.RecordUpsertRequest, requiredFeatureIds: [BaseFeatureIds.RecordsUpsert, AspNetCoreProjectionFeatureIds.ProjectionAspNet]),
        Route("base.clientGeneration.application", HttpMethodKind.Get, "/base/client-generation", AspNetCoreDtoContractDescriptorFactory.ClientGenerationSnapshotV2, VisibilityLevel.Internal, RouteAuthRequirement.HostPolicy),
        Route("base.clientGeneration.controlPlane", HttpMethodKind.Get, "/base/client-generation", AspNetCoreDtoContractDescriptorFactory.ClientGenerationSnapshotV2, VisibilityLevel.Admin, RouteAuthRequirement.Admin),
        Route("base.admin.purge", HttpMethodKind.Post, "/base/administration/purge", "base.admin.purge.result", VisibilityLevel.Admin, RouteAuthRequirement.Admin, AspNetCoreDtoContractDescriptorFactory.PurgeRequest),
        Route("base.admin.backup.create", HttpMethodKind.Post, "/base/administration/backups:create", "base.admin.backup.manifest", VisibilityLevel.Admin, RouteAuthRequirement.Admin, AspNetCoreDtoContractDescriptorFactory.BackupCreateRequest),
        Route("base.admin.backup.validate", HttpMethodKind.Post, "/base/administration/backups:validate", "base.admin.backup.manifest", VisibilityLevel.Admin, RouteAuthRequirement.Admin, AspNetCoreDtoContractDescriptorFactory.BackupValidationRequest),
        Route("base.admin.backup.restore", HttpMethodKind.Post, "/base/administration/backups:restore", "base.admin.backup.restore.result", VisibilityLevel.Admin, RouteAuthRequirement.Admin, AspNetCoreDtoContractDescriptorFactory.RestoreRequest)
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
