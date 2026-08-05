using HPD.Base;

namespace HPD.Base.AspNetCore;

internal static class AspNetCoreDtoContractDescriptorFactory
{
    /// <summary>Provides the expanded manifest value.</summary>
    public const string ExpandedManifest = "base.expandedManifest";
    /// <summary>Provides the collection definition value.</summary>
    public const string CollectionDefinition = "base.collectionDefinition";
    /// <summary>Provides the collection definition array value.</summary>
    public const string CollectionDefinitionArray = "base.collectionDefinitionArray";
    /// <summary>Provides the health descriptor array value.</summary>
    public const string HealthDescriptorArray = "base.healthDescriptorArray";
    /// <summary>Provides the diagnostic descriptor array value.</summary>
    public const string DiagnosticDescriptorArray = "base.diagnosticDescriptorArray";
    /// <summary>Provides the delete result value.</summary>
    public const string DeleteResult = "base.deleteResult";
    /// <summary>Provides the record create request value.</summary>
    public const string RecordCreateRequest = "base.recordCreateRequest";
    /// <summary>Provides the record patch request value.</summary>
    public const string RecordPatchRequest = "base.recordPatchRequest";
    /// <summary>Provides the record replace request value.</summary>
    public const string RecordReplaceRequest = "base.recordReplaceRequest";
    /// <summary>Provides the record delete request value.</summary>
    public const string RecordDeleteRequest = "base.recordDeleteRequest";
    /// <summary>Provides the record query value.</summary>
    public const string RecordQuery = "base.recordQuery";
    /// <summary>Provides the base policy explain request value.</summary>
    public const string BasePolicyExplainRequest = "base.policyExplainRequest";
    /// <summary>Provides the base policy explain response value.</summary>
    public const string BasePolicyExplainResponse = "base.policyExplainResponse";
    /// <summary>Provides the problem details value.</summary>
    public const string ProblemDetails = "hpd.base.aspnet.problemDetails";

    /// <summary>Executes the create operation.</summary>
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
