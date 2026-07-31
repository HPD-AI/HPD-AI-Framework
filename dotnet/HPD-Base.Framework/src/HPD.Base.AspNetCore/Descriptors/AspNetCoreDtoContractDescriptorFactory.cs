using HPD.Base;

namespace HPD.Base.AspNetCore;

internal static class AspNetCoreDtoContractDescriptorFactory
{
    public const string ExpandedManifest = "base.expandedManifest";
    public const string CollectionDefinition = "base.collectionDefinition";
    public const string CollectionDefinitionArray = "base.collectionDefinitionArray";
    public const string HealthDescriptorArray = "base.healthDescriptorArray";
    public const string DiagnosticDescriptorArray = "base.diagnosticDescriptorArray";
    public const string DeleteResult = "base.deleteResult";
    public const string RecordCreateRequest = "base.recordCreateRequest";
    public const string RecordPatchRequest = "base.recordPatchRequest";
    public const string RecordReplaceRequest = "base.recordReplaceRequest";
    public const string RecordDeleteRequest = "base.recordDeleteRequest";
    public const string RecordQuery = "base.recordQuery";
    public const string BasePolicyExplainRequest = "base.policyExplainRequest";
    public const string BasePolicyExplainResponse = "base.policyExplainResponse";
    public const string ProblemDetails = "hpd.base.aspnet.problemDetails";

    public static DtoContractDescriptor[] Create() =>
    [
        Dto(BaseDtoIds.Manifest),
        Dto(ExpandedManifest),
        Dto(BaseDtoIds.CapabilityDescriptor),
        Dto(BaseDtoIds.SchemaMetadata),
        Dto(CollectionDefinition),
        Dto(CollectionDefinitionArray),
        Dto(BaseDtoIds.HealthDescriptor),
        Dto(HealthDescriptorArray),
        Dto(BaseDtoIds.DiagnosticDescriptor),
        Dto(DiagnosticDescriptorArray),
        Dto(BaseDtoIds.RecordEnvelope),
        Dto(BaseDtoIds.RecordPage),
        Dto(DeleteResult),
        Dto(RecordCreateRequest),
        Dto(RecordPatchRequest),
        Dto(RecordReplaceRequest),
        Dto(RecordDeleteRequest),
        Dto(BaseDtoIds.BaseRecordBatchRequest),
        Dto(BaseDtoIds.BaseRecordBatchResult),
        Dto(BaseDtoIds.RecordUpsertRequest),
        Dto(BaseDtoIds.RecordUpsertResult),
        Dto(RecordQuery),
        Dto(BasePolicyExplainRequest, VisibilityLevel.Admin),
        Dto(BasePolicyExplainResponse, VisibilityLevel.Admin),
        Dto(BaseDtoIds.BaseError),
        Dto(ProblemDetails)
    ];

    private static DtoContractDescriptor Dto(string id, VisibilityLevel visibility = VisibilityLevel.Public) => new()
    {
        Id = id,
        ContractVersion = "1.0",
        Visibility = visibility,
        JsonContextOwner = "HPD.Base.AspNetCore"
    };
}
