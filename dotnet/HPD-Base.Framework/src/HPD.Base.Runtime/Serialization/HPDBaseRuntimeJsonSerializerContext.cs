using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base.Descriptors;
using HPD.Base.Health;
using HPD.Base.Policy;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.Configuration;
using HPD.Base.Runtime.Observability;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Policy;
using HPD.Base.Runtime.Policy.Admin;
using HPD.Base.Runtime.Query;
using HPD.Base.Runtime.Schema;
using HPD.Base.Schema;
using HPD.Base.Serialization;

namespace HPD.Base.Runtime.Serialization;

using BaseEventPublishFailureModeConverter = LowerCamelJsonStringEnumConverter<BaseEventPublishFailureMode>;
using BaseQueryValidationUsageConverter = LowerCamelJsonStringEnumConverter<BaseQueryValidationUsage>;
using BaseRuntimeValidationFailureKindConverter = LowerCamelJsonStringEnumConverter<BaseRuntimeValidationFailureKind>;
using BaseRuntimeValidationSeverityConverter = LowerCamelJsonStringEnumConverter<BaseRuntimeValidationSeverity>;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    UseStringEnumConverter = true,
    Converters = new[]
    {
        typeof(RecordIdJsonConverter),
        typeof(RevisionTokenJsonConverter),
        typeof(BaseEventPublishFailureModeConverter),
        typeof(BaseQueryValidationUsageConverter),
        typeof(BaseRuntimeValidationFailureKindConverter),
        typeof(BaseRuntimeValidationSeverityConverter)
    })]
[JsonSerializable(typeof(BaseEventPublishFailureMode))]
[JsonSerializable(typeof(BaseQueryValidationUsage))]
[JsonSerializable(typeof(BaseRuntimeValidationFailureKind))]
[JsonSerializable(typeof(BaseRuntimeValidationSeverity))]
[JsonSerializable(typeof(BaseRuntimeValidationIssue))]
[JsonSerializable(typeof(BaseRuntimeValidationIssue[]))]
[JsonSerializable(typeof(BaseRuntimeValidationResult))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(BaseManifestRequest))]
[JsonSerializable(typeof(BaseManifestExpansionRequest))]
[JsonSerializable(typeof(ExpandedBaseManifest))]
[JsonSerializable(typeof(BaseDescriptorSnapshot))]
[JsonSerializable(typeof(BasePolicyRequest))]
[JsonSerializable(typeof(BasePolicyEvaluation))]
[JsonSerializable(typeof(BasePolicyExplainRequest))]
[JsonSerializable(typeof(BasePolicyExplainResponse))]
[JsonSerializable(typeof(BasePolicyExplainOperation))]
[JsonSerializable(typeof(BasePolicyExplainOutcome))]
[JsonSerializable(typeof(BasePolicyExplainOptions))]
[JsonSerializable(typeof(BasePolicyExplainDecision))]
[JsonSerializable(typeof(BasePolicyExplainRuntimeSummary))]
[JsonSerializable(typeof(BasePolicyExplainConstraintSummary))]
[JsonSerializable(typeof(BasePolicyExplainFilterSummary))]
[JsonSerializable(typeof(BasePolicyExplainFieldMaskSummary))]
[JsonSerializable(typeof(BasePolicyExplainObligationSummary))]
[JsonSerializable(typeof(BasePolicyExplainRedactionSummary))]
[JsonSerializable(typeof(BasePayloadValidationRequest))]
[JsonSerializable(typeof(BaseValidatedPayload))]
[JsonSerializable(typeof(ValidatedRecordQuery))]
[JsonSerializable(typeof(OperationResult<BaseRuntimeValidationResult>))]
[JsonSerializable(typeof(OperationResult<ExpandedBaseManifest>))]
[JsonSerializable(typeof(OperationResult<BasePolicyEvaluation>))]
[JsonSerializable(typeof(OperationResult<BasePolicyExplainResponse>))]
[JsonSerializable(typeof(OperationResult<BaseValidatedPayload>))]
[JsonSerializable(typeof(OperationResult<ValidatedRecordQuery>))]
[JsonSerializable(typeof(HPDBaseRuntimeOptions))]
[JsonSerializable(typeof(HPDBaseRuntimeObservabilityOptions))]
[JsonSerializable(typeof(HPDBaseRuntimeLimitOptions))]
[JsonSerializable(typeof(HPDBaseRuntimeEventOptions))]
[JsonSerializable(typeof(HPDBaseRuntimeRedactionOptions))]
[JsonSerializable(typeof(BaseManifest))]
[JsonSerializable(typeof(SchemaMetadata))]
[JsonSerializable(typeof(CapabilityDescriptor))]
[JsonSerializable(typeof(HealthDescriptor[]))]
[JsonSerializable(typeof(DiagnosticDescriptor[]))]
[JsonSerializable(typeof(CollectionDefinition[]))]
[JsonSerializable(typeof(PrincipalContext))]
[JsonSerializable(typeof(OperationContext))]
[JsonSerializable(typeof(CollectionDefinition))]
[JsonSerializable(typeof(RecordQuery))]
[JsonSerializable(typeof(RecordEnvelope))]
[JsonSerializable(typeof(RecordPayload))]
[JsonSerializable(typeof(RecordId))]
[JsonSerializable(typeof(AccessGrant[]))]
[JsonSerializable(typeof(PolicyDecision))]
[JsonSerializable(typeof(FilterExpression))]
[JsonSerializable(typeof(FieldMask))]
public partial class HPDBaseRuntimeJsonSerializerContext : JsonSerializerContext;
