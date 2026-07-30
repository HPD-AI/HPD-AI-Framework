using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base.Descriptors;
using HPD.Base.Events;
using HPD.Base.Health;
using HPD.Base.Policy;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using HPD.Base.Stores;

namespace HPD.Base.Serialization;

using BaseOperationKindConverter = LowerCamelJsonStringEnumConverter<BaseOperationKind>;
using OperationModeConverter = LowerCamelJsonStringEnumConverter<OperationMode>;
using VisibilityLevelConverter = LowerCamelJsonStringEnumConverter<VisibilityLevel>;
using ManifestLinkKindConverter = LowerCamelJsonStringEnumConverter<ManifestLinkKind>;
using HttpMethodKindConverter = LowerCamelJsonStringEnumConverter<HttpMethodKind>;
using RuntimeModeConverter = LowerCamelJsonStringEnumConverter<RuntimeMode>;
using BaseModuleKindConverter = LowerCamelJsonStringEnumConverter<BaseModuleKind>;
using ModuleStatusConverter = LowerCamelJsonStringEnumConverter<ModuleStatus>;
using DependencyFailureBehaviorConverter = LowerCamelJsonStringEnumConverter<DependencyFailureBehavior>;
using ProjectionKindConverter = LowerCamelJsonStringEnumConverter<ProjectionKind>;
using ProjectionStatusConverter = LowerCamelJsonStringEnumConverter<ProjectionStatus>;
using ProjectionEntrypointKindConverter = LowerCamelJsonStringEnumConverter<ProjectionEntrypointKind>;
using RouteAuthRequirementConverter = LowerCamelJsonStringEnumConverter<RouteAuthRequirement>;
using CapabilityStatusConverter = LowerCamelJsonStringEnumConverter<CapabilityStatus>;
using CapabilityScopeConverter = LowerCamelJsonStringEnumConverter<CapabilityScope>;
using SupportLevelConverter = LowerCamelJsonStringEnumConverter<SupportLevel>;
using HealthStatusConverter = LowerCamelJsonStringEnumConverter<HealthStatus>;
using HealthScopeConverter = LowerCamelJsonStringEnumConverter<HealthScope>;
using HealthMetricValueKindConverter = LowerCamelJsonStringEnumConverter<HealthMetricValueKind>;
using DiagnosticSeverityConverter = LowerCamelJsonStringEnumConverter<DiagnosticSeverity>;
using DiagnosticCategoryConverter = LowerCamelJsonStringEnumConverter<DiagnosticCategory>;
using SchemaMetadataRoleConverter = LowerCamelJsonStringEnumConverter<SchemaMetadataRole>;
using SchemaModeConverter = LowerCamelJsonStringEnumConverter<SchemaMode>;
using UnknownFieldPolicyConverter = LowerCamelJsonStringEnumConverter<UnknownFieldPolicy>;
using ValidationModeConverter = LowerCamelJsonStringEnumConverter<ValidationMode>;
using FieldCardinalityKindConverter = LowerCamelJsonStringEnumConverter<FieldCardinalityKind>;
using DefaultValueKindConverter = LowerCamelJsonStringEnumConverter<DefaultValueKind>;
using GenerationKindConverter = LowerCamelJsonStringEnumConverter<GenerationKind>;
using ValidationRuleKindConverter = LowerCamelJsonStringEnumConverter<ValidationRuleKind>;
using ValidationSeverityConverter = LowerCamelJsonStringEnumConverter<ValidationSeverity>;
using ValidationAppliesToConverter = LowerCamelJsonStringEnumConverter<ValidationAppliesTo>;
using RelationKindConverter = LowerCamelJsonStringEnumConverter<RelationKind>;
using RelationCardinalityConverter = LowerCamelJsonStringEnumConverter<RelationCardinality>;
using DeleteBehaviorConverter = LowerCamelJsonStringEnumConverter<DeleteBehavior>;
using FileReferenceShapeConverter = LowerCamelJsonStringEnumConverter<FileReferenceShape>;
using FileCleanupPolicyConverter = LowerCamelJsonStringEnumConverter<FileCleanupPolicy>;
using IndexKindConverter = LowerCamelJsonStringEnumConverter<IndexKind>;
using IndexStatusConverter = LowerCamelJsonStringEnumConverter<IndexStatus>;
using IndexPartKindConverter = LowerCamelJsonStringEnumConverter<IndexPartKind>;
using IndexSortDirectionConverter = LowerCamelJsonStringEnumConverter<IndexSortDirection>;
using IndexNullOrderConverter = LowerCamelJsonStringEnumConverter<IndexNullOrder>;
using EnforcementOwnerConverter = LowerCamelJsonStringEnumConverter<EnforcementOwner>;
using SchemaSourceKindConverter = LowerCamelJsonStringEnumConverter<SchemaSourceKind>;
using RecordPayloadKindConverter = LowerCamelJsonStringEnumConverter<RecordPayloadKind>;
using RecordIncludeKindConverter = LowerCamelJsonStringEnumConverter<RecordIncludeKind>;
using FilterNodeKindConverter = LowerCamelJsonStringEnumConverter<FilterNodeKind>;
using FilterOperatorConverter = LowerCamelJsonStringEnumConverter<FilterOperator>;
using QueryValueKindConverter = LowerCamelJsonStringEnumConverter<QueryValueKind>;
using QuerySortDirectionConverter = LowerCamelJsonStringEnumConverter<QuerySortDirection>;
using QueryNullOrderConverter = LowerCamelJsonStringEnumConverter<QueryNullOrder>;
using QueryPaginationModeConverter = LowerCamelJsonStringEnumConverter<QueryPaginationMode>;
using QueryCursorDirectionConverter = LowerCamelJsonStringEnumConverter<QueryCursorDirection>;
using QueryCountModeConverter = LowerCamelJsonStringEnumConverter<QueryCountMode>;
using QueryOperatorPlacementConverter = LowerCamelJsonStringEnumConverter<QueryOperatorPlacement>;
using FilterUsageConverter = LowerCamelJsonStringEnumConverter<FilterUsage>;
using QueryExecutionModeConverter = LowerCamelJsonStringEnumConverter<QueryExecutionMode>;
using PrincipalAuthenticationStateConverter = LowerCamelJsonStringEnumConverter<PrincipalAuthenticationState>;
using PolicyResourceKindConverter = LowerCamelJsonStringEnumConverter<PolicyResourceKind>;
using PolicyEffectConverter = LowerCamelJsonStringEnumConverter<PolicyEffect>;
using PolicyOutcomeConverter = LowerCamelJsonStringEnumConverter<PolicyOutcome>;
using FieldMaskModeConverter = LowerCamelJsonStringEnumConverter<FieldMaskMode>;
using ObligationEnforcementConverter = LowerCamelJsonStringEnumConverter<ObligationEnforcement>;
using PushdownModeConverter = LowerCamelJsonStringEnumConverter<PushdownMode>;
using PushdownTrustConverter = LowerCamelJsonStringEnumConverter<PushdownTrust>;
using AccessSubjectKindConverter = LowerCamelJsonStringEnumConverter<AccessSubjectKind>;
using GrantEffectConverter = LowerCamelJsonStringEnumConverter<GrantEffect>;
using ResourceScopeKindConverter = LowerCamelJsonStringEnumConverter<ResourceScopeKind>;
using IdAuthorityConverter = LowerCamelJsonStringEnumConverter<IdAuthority>;
using TimestampAuthorityConverter = LowerCamelJsonStringEnumConverter<TimestampAuthority>;
using ConsistencyModelConverter = LowerCamelJsonStringEnumConverter<ConsistencyModel>;
using OperationStatusConverter = LowerCamelJsonStringEnumConverter<OperationStatus>;
using ErrorCategoryConverter = LowerCamelJsonStringEnumConverter<ErrorCategory>;
using ConflictKindConverter = LowerCamelJsonStringEnumConverter<ConflictKind>;
using CapabilityFailureReasonConverter = LowerCamelJsonStringEnumConverter<CapabilityFailureReason>;
using RevisionGuaranteeConverter = LowerCamelJsonStringEnumConverter<RevisionGuarantee>;
using EventResourceKindConverter = LowerCamelJsonStringEnumConverter<EventResourceKind>;
using EventDeliveryGuaranteeConverter = LowerCamelJsonStringEnumConverter<EventDeliveryGuarantee>;
using BaseRecordMutationKindConverter = LowerCamelJsonStringEnumConverter<BaseRecordMutationKind>;
using BaseCommittedRecordMutationKindConverter = LowerCamelJsonStringEnumConverter<BaseCommittedRecordMutationKind>;
using BaseRecordBatchExecutionModeConverter = LowerCamelJsonStringEnumConverter<BaseRecordBatchExecutionMode>;
using BaseRecordBatchOutcomeConverter = LowerCamelJsonStringEnumConverter<BaseRecordBatchOutcome>;
using BaseRecordBatchItemDispositionConverter = LowerCamelJsonStringEnumConverter<BaseRecordBatchItemDisposition>;
using RecordUpsertUpdateModeConverter = LowerCamelJsonStringEnumConverter<RecordUpsertUpdateMode>;
using RecordUpsertExistenceConditionConverter = LowerCamelJsonStringEnumConverter<RecordUpsertExistenceCondition>;
using RecordUpsertOutcomeConverter = LowerCamelJsonStringEnumConverter<RecordUpsertOutcome>;
using BaseTransactionIsolationConverter = LowerCamelJsonStringEnumConverter<BaseTransactionIsolation>;
using RecordMutationExecutionOutcomeConverter = LowerCamelJsonStringEnumConverter<RecordMutationExecutionOutcome>;
using AtomicMutationProcessingOutcomeConverter = LowerCamelJsonStringEnumConverter<AtomicMutationProcessingOutcome>;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    UseStringEnumConverter = true,
    Converters = new[]
    {
        typeof(RecordIdJsonConverter),
        typeof(RevisionTokenJsonConverter),
        typeof(BaseOperationKindConverter),
        typeof(OperationModeConverter),
        typeof(VisibilityLevelConverter),
        typeof(ManifestLinkKindConverter),
        typeof(HttpMethodKindConverter),
        typeof(RuntimeModeConverter),
        typeof(BaseModuleKindConverter),
        typeof(ModuleStatusConverter),
        typeof(DependencyFailureBehaviorConverter),
        typeof(ProjectionKindConverter),
        typeof(ProjectionStatusConverter),
        typeof(ProjectionEntrypointKindConverter),
        typeof(RouteAuthRequirementConverter),
        typeof(CapabilityStatusConverter),
        typeof(CapabilityScopeConverter),
        typeof(SupportLevelConverter),
        typeof(HealthStatusConverter),
        typeof(HealthScopeConverter),
        typeof(HealthMetricValueKindConverter),
        typeof(DiagnosticSeverityConverter),
        typeof(DiagnosticCategoryConverter),
        typeof(SchemaMetadataRoleConverter),
        typeof(SchemaModeConverter),
        typeof(UnknownFieldPolicyConverter),
        typeof(ValidationModeConverter),
        typeof(FieldCardinalityKindConverter),
        typeof(DefaultValueKindConverter),
        typeof(GenerationKindConverter),
        typeof(ValidationRuleKindConverter),
        typeof(ValidationSeverityConverter),
        typeof(ValidationAppliesToConverter),
        typeof(RelationKindConverter),
        typeof(RelationCardinalityConverter),
        typeof(DeleteBehaviorConverter),
        typeof(FileReferenceShapeConverter),
        typeof(FileCleanupPolicyConverter),
        typeof(IndexKindConverter),
        typeof(IndexStatusConverter),
        typeof(IndexPartKindConverter),
        typeof(IndexSortDirectionConverter),
        typeof(IndexNullOrderConverter),
        typeof(EnforcementOwnerConverter),
        typeof(SchemaSourceKindConverter),
        typeof(RecordPayloadKindConverter),
        typeof(RecordIncludeKindConverter),
        typeof(FilterNodeKindConverter),
        typeof(FilterOperatorConverter),
        typeof(QueryValueKindConverter),
        typeof(QuerySortDirectionConverter),
        typeof(QueryNullOrderConverter),
        typeof(QueryPaginationModeConverter),
        typeof(QueryCursorDirectionConverter),
        typeof(QueryCountModeConverter),
        typeof(QueryOperatorPlacementConverter),
        typeof(FilterUsageConverter),
        typeof(QueryExecutionModeConverter),
        typeof(PrincipalAuthenticationStateConverter),
        typeof(PolicyResourceKindConverter),
        typeof(PolicyEffectConverter),
        typeof(PolicyOutcomeConverter),
        typeof(FieldMaskModeConverter),
        typeof(ObligationEnforcementConverter),
        typeof(PushdownModeConverter),
        typeof(PushdownTrustConverter),
        typeof(AccessSubjectKindConverter),
        typeof(GrantEffectConverter),
        typeof(ResourceScopeKindConverter),
        typeof(IdAuthorityConverter),
        typeof(TimestampAuthorityConverter),
        typeof(ConsistencyModelConverter),
        typeof(OperationStatusConverter),
        typeof(ErrorCategoryConverter),
        typeof(ConflictKindConverter),
        typeof(CapabilityFailureReasonConverter),
        typeof(RevisionGuaranteeConverter),
        typeof(EventResourceKindConverter),
        typeof(EventDeliveryGuaranteeConverter),
        typeof(BaseRecordMutationKindConverter),
        typeof(BaseCommittedRecordMutationKindConverter),
        typeof(BaseRecordBatchExecutionModeConverter),
        typeof(BaseRecordBatchOutcomeConverter),
        typeof(BaseRecordBatchItemDispositionConverter),
        typeof(RecordUpsertUpdateModeConverter),
        typeof(RecordUpsertExistenceConditionConverter),
        typeof(RecordUpsertOutcomeConverter),
        typeof(BaseTransactionIsolationConverter),
        typeof(RecordMutationExecutionOutcomeConverter),
        typeof(AtomicMutationProcessingOutcomeConverter)
    })]
[JsonSerializable(typeof(BaseOperationKind))]
[JsonSerializable(typeof(OperationMode))]
[JsonSerializable(typeof(VisibilityLevel))]
[JsonSerializable(typeof(ManifestLinkKind))]
[JsonSerializable(typeof(HttpMethodKind))]
[JsonSerializable(typeof(RuntimeMode))]
[JsonSerializable(typeof(BaseModuleKind))]
[JsonSerializable(typeof(ModuleStatus))]
[JsonSerializable(typeof(DependencyFailureBehavior))]
[JsonSerializable(typeof(ProjectionKind))]
[JsonSerializable(typeof(ProjectionStatus))]
[JsonSerializable(typeof(ProjectionEntrypointKind))]
[JsonSerializable(typeof(RouteAuthRequirement))]
[JsonSerializable(typeof(CapabilityStatus))]
[JsonSerializable(typeof(CapabilityScope))]
[JsonSerializable(typeof(SupportLevel))]
[JsonSerializable(typeof(HealthStatus))]
[JsonSerializable(typeof(HealthScope))]
[JsonSerializable(typeof(HealthMetricValueKind))]
[JsonSerializable(typeof(DiagnosticSeverity))]
[JsonSerializable(typeof(DiagnosticCategory))]
[JsonSerializable(typeof(SchemaMetadataRole))]
[JsonSerializable(typeof(SchemaMode))]
[JsonSerializable(typeof(UnknownFieldPolicy))]
[JsonSerializable(typeof(ValidationMode))]
[JsonSerializable(typeof(FieldCardinalityKind))]
[JsonSerializable(typeof(DefaultValueKind))]
[JsonSerializable(typeof(GenerationKind))]
[JsonSerializable(typeof(ValidationRuleKind))]
[JsonSerializable(typeof(ValidationSeverity))]
[JsonSerializable(typeof(ValidationAppliesTo))]
[JsonSerializable(typeof(RelationKind))]
[JsonSerializable(typeof(RelationCardinality))]
[JsonSerializable(typeof(DeleteBehavior))]
[JsonSerializable(typeof(FileReferenceShape))]
[JsonSerializable(typeof(FileCleanupPolicy))]
[JsonSerializable(typeof(IndexKind))]
[JsonSerializable(typeof(IndexStatus))]
[JsonSerializable(typeof(IndexPartKind))]
[JsonSerializable(typeof(IndexSortDirection))]
[JsonSerializable(typeof(IndexNullOrder))]
[JsonSerializable(typeof(EnforcementOwner))]
[JsonSerializable(typeof(SchemaSourceKind))]
[JsonSerializable(typeof(RecordPayloadKind))]
[JsonSerializable(typeof(RecordIncludeKind))]
[JsonSerializable(typeof(FilterNodeKind))]
[JsonSerializable(typeof(FilterOperator))]
[JsonSerializable(typeof(QueryValueKind))]
[JsonSerializable(typeof(QuerySortDirection))]
[JsonSerializable(typeof(QueryNullOrder))]
[JsonSerializable(typeof(QueryPaginationMode))]
[JsonSerializable(typeof(QueryCursorDirection))]
[JsonSerializable(typeof(QueryCountMode))]
[JsonSerializable(typeof(QueryOperatorPlacement))]
[JsonSerializable(typeof(FilterUsage))]
[JsonSerializable(typeof(QueryExecutionMode))]
[JsonSerializable(typeof(PrincipalAuthenticationState))]
[JsonSerializable(typeof(PolicyResourceKind))]
[JsonSerializable(typeof(PolicyEffect))]
[JsonSerializable(typeof(PolicyOutcome))]
[JsonSerializable(typeof(FieldMaskMode))]
[JsonSerializable(typeof(ObligationEnforcement))]
[JsonSerializable(typeof(PushdownMode))]
[JsonSerializable(typeof(PushdownTrust))]
[JsonSerializable(typeof(AccessSubjectKind))]
[JsonSerializable(typeof(GrantEffect))]
[JsonSerializable(typeof(ResourceScopeKind))]
[JsonSerializable(typeof(IdAuthority))]
[JsonSerializable(typeof(TimestampAuthority))]
[JsonSerializable(typeof(ConsistencyModel))]
[JsonSerializable(typeof(OperationStatus))]
[JsonSerializable(typeof(ErrorCategory))]
[JsonSerializable(typeof(ConflictKind))]
[JsonSerializable(typeof(CapabilityFailureReason))]
[JsonSerializable(typeof(RevisionGuarantee))]
[JsonSerializable(typeof(EventResourceKind))]
[JsonSerializable(typeof(EventDeliveryGuarantee))]
[JsonSerializable(typeof(BaseRecordMutationKind))]
[JsonSerializable(typeof(BaseCommittedRecordMutationKind))]
[JsonSerializable(typeof(BaseRecordBatchExecutionMode))]
[JsonSerializable(typeof(BaseRecordBatchExecutionMode[]))]
[JsonSerializable(typeof(BaseRecordBatchOutcome))]
[JsonSerializable(typeof(BaseRecordBatchItemDisposition))]
[JsonSerializable(typeof(RecordUpsertUpdateMode))]
[JsonSerializable(typeof(RecordUpsertUpdateMode[]))]
[JsonSerializable(typeof(RecordUpsertExistenceCondition))]
[JsonSerializable(typeof(RecordUpsertOutcome))]
[JsonSerializable(typeof(BaseTransactionIsolation))]
[JsonSerializable(typeof(RecordMutationExecutionOutcome))]
[JsonSerializable(typeof(AtomicMutationProcessingOutcome))]
[JsonSerializable(typeof(RecordId))]
[JsonSerializable(typeof(RevisionToken))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, RecordIncludeValue>))]
[JsonSerializable(typeof(BaseManifest))]
[JsonSerializable(typeof(ManifestLinkDescriptor))]
[JsonSerializable(typeof(RuntimeDescriptor))]
[JsonSerializable(typeof(CompatibilityDescriptor))]
[JsonSerializable(typeof(CollectionSummaryDescriptor))]
[JsonSerializable(typeof(CapabilitySummaryDescriptor))]
[JsonSerializable(typeof(BaseModuleDescriptor))]
[JsonSerializable(typeof(ModuleCompatibility))]
[JsonSerializable(typeof(ModuleDependency))]
[JsonSerializable(typeof(ProjectionDescriptor))]
[JsonSerializable(typeof(ProjectionEntrypointDescriptor))]
[JsonSerializable(typeof(RouteDescriptor))]
[JsonSerializable(typeof(DtoContractDescriptor))]
[JsonSerializable(typeof(EventTypeDescriptor))]
[JsonSerializable(typeof(HealthRefDescriptor))]
[JsonSerializable(typeof(DiagnosticRefDescriptor))]
[JsonSerializable(typeof(FieldAnnotationDescriptor))]
[JsonSerializable(typeof(CapabilityDescriptor))]
[JsonSerializable(typeof(CapabilityFamilyDescriptor))]
[JsonSerializable(typeof(CapabilityFeatureDescriptor))]
[JsonSerializable(typeof(CapabilityLimitDescriptor))]
[JsonSerializable(typeof(CapabilityDependencyDescriptor))]
[JsonSerializable(typeof(CapabilityConstraintSet))]
[JsonSerializable(typeof(StoreReadCapabilityConstraints))]
[JsonSerializable(typeof(StoreMutationCapabilityConstraints))]
[JsonSerializable(typeof(StoreRevisionCapabilityConstraints))]
[JsonSerializable(typeof(StoreStreamingCapabilityConstraints))]
[JsonSerializable(typeof(QueryFilterCapabilityConstraints))]
[JsonSerializable(typeof(QuerySortCapabilityConstraints))]
[JsonSerializable(typeof(QueryPaginationCapabilityConstraints))]
[JsonSerializable(typeof(QueryCountCapabilityConstraints))]
[JsonSerializable(typeof(QuerySelectCapabilityConstraints))]
[JsonSerializable(typeof(QueryIncludeCapabilityConstraints))]
[JsonSerializable(typeof(PolicyEvaluationCapabilityConstraints))]
[JsonSerializable(typeof(SchemaReadCapabilityConstraints))]
[JsonSerializable(typeof(EventStreamCapabilityConstraints))]
[JsonSerializable(typeof(ProjectionCapabilityConstraints))]
[JsonSerializable(typeof(FileCapabilityConstraints))]
[JsonSerializable(typeof(RealtimeCapabilityConstraints))]
[JsonSerializable(typeof(BatchCapabilityConstraints))]
[JsonSerializable(typeof(UpsertCapabilityConstraints))]
[JsonSerializable(typeof(SearchCapabilityConstraints))]
[JsonSerializable(typeof(VectorCapabilityConstraints))]
[JsonSerializable(typeof(SchemaMetadata))]
[JsonSerializable(typeof(SchemaMetadata[]))]
[JsonSerializable(typeof(CollectionDefinition))]
[JsonSerializable(typeof(CollectionDefinition[]))]
[JsonSerializable(typeof(CollectionOperationMatrix))]
[JsonSerializable(typeof(CollectionVisibility))]
[JsonSerializable(typeof(SchemaSourceDescriptor))]
[JsonSerializable(typeof(SchemaSourceDescriptor[]))]
[JsonSerializable(typeof(SchemaRelationSummary))]
[JsonSerializable(typeof(SchemaRelationSummary[]))]
[JsonSerializable(typeof(FieldDefinition))]
[JsonSerializable(typeof(FieldDefinition[]))]
[JsonSerializable(typeof(CardinalityDescriptor))]
[JsonSerializable(typeof(DefaultValueDescriptor))]
[JsonSerializable(typeof(GenerationDescriptor))]
[JsonSerializable(typeof(ConstraintAnnotations))]
[JsonSerializable(typeof(ValidationAnnotations))]
[JsonSerializable(typeof(ValidationRule))]
[JsonSerializable(typeof(ValidationRule[]))]
[JsonSerializable(typeof(RelationAnnotation))]
[JsonSerializable(typeof(RelationIncludeAnnotation))]
[JsonSerializable(typeof(FileAnnotation))]
[JsonSerializable(typeof(FieldVisibilityAnnotation))]
[JsonSerializable(typeof(UiAnnotation))]
[JsonSerializable(typeof(SdkAnnotation))]
[JsonSerializable(typeof(StoreAnnotation))]
[JsonSerializable(typeof(IndexDefinition))]
[JsonSerializable(typeof(IndexDefinition[]))]
[JsonSerializable(typeof(IndexPart))]
[JsonSerializable(typeof(IndexPart[]))]
[JsonSerializable(typeof(RecordEnvelope))]
[JsonSerializable(typeof(RecordEnvelope[]))]
[JsonSerializable(typeof(RecordPayload))]
[JsonSerializable(typeof(RecordMetadata))]
[JsonSerializable(typeof(RecordPolicyMetadata))]
[JsonSerializable(typeof(RecordIncludeValue))]
[JsonSerializable(typeof(RecordIncludeValue[]))]
[JsonSerializable(typeof(RecordPage))]
[JsonSerializable(typeof(PageInfo))]
[JsonSerializable(typeof(CountInfo))]
[JsonSerializable(typeof(RecordCreateRequest))]
[JsonSerializable(typeof(RecordPatchRequest))]
[JsonSerializable(typeof(RecordReplaceRequest))]
[JsonSerializable(typeof(RecordDeleteRequest))]
[JsonSerializable(typeof(RecordUpsertRequest))]
[JsonSerializable(typeof(RecordUpsertResult))]
[JsonSerializable(typeof(BaseRecordBatchRequest))]
[JsonSerializable(typeof(BaseRecordBatchItem))]
[JsonSerializable(typeof(BaseRecordBatchItem[]))]
[JsonSerializable(typeof(BaseRecordBatchResult))]
[JsonSerializable(typeof(BaseRecordBatchItemResult))]
[JsonSerializable(typeof(BaseRecordBatchItemResult[]))]
[JsonSerializable(typeof(DeleteResult))]
[JsonSerializable(typeof(StoreCapabilityDescriptor))]
[JsonSerializable(typeof(RecordReadCapability))]
[JsonSerializable(typeof(RecordMutationCapability))]
[JsonSerializable(typeof(RevisionCapability))]
[JsonSerializable(typeof(StoreBatchCapability))]
[JsonSerializable(typeof(StoreUpsertCapability))]
[JsonSerializable(typeof(StreamingCapability))]
[JsonSerializable(typeof(RecordMutationExecutionRequest))]
[JsonSerializable(typeof(AtomicMutationProcessingResult))]
[JsonSerializable(typeof(BaseRecordMutationFact))]
[JsonSerializable(typeof(BaseRecordMutationFact[]))]
[JsonSerializable(typeof(RecordMutationSessionContext))]
[JsonSerializable(typeof(RecordMutationSessionResult))]
[JsonSerializable(typeof(RecordMutationExecutionResult))]
[JsonSerializable(typeof(RecordQuery))]
[JsonSerializable(typeof(QuerySort))]
[JsonSerializable(typeof(QuerySort[]))]
[JsonSerializable(typeof(QueryPage))]
[JsonSerializable(typeof(QueryInclude))]
[JsonSerializable(typeof(QueryInclude[]))]
[JsonSerializable(typeof(QueryExtension))]
[JsonSerializable(typeof(QueryExtension[]))]
[JsonSerializable(typeof(FilterExpression))]
[JsonSerializable(typeof(FilterExpression[]))]
[JsonSerializable(typeof(QueryValue))]
[JsonSerializable(typeof(QueryValue[]))]
[JsonSerializable(typeof(QueryOperatorDescriptor))]
[JsonSerializable(typeof(QueryOperatorDescriptor[]))]
[JsonSerializable(typeof(QueryCapability))]
[JsonSerializable(typeof(FilterCapability))]
[JsonSerializable(typeof(SortCapability))]
[JsonSerializable(typeof(PaginationCapability))]
[JsonSerializable(typeof(CountCapability))]
[JsonSerializable(typeof(SelectCapability))]
[JsonSerializable(typeof(QueryIncludeCapability))]
[JsonSerializable(typeof(PolicyEvaluationRequest))]
[JsonSerializable(typeof(PolicyResource))]
[JsonSerializable(typeof(PolicyDecision))]
[JsonSerializable(typeof(PolicyConstraints))]
[JsonSerializable(typeof(FieldMask))]
[JsonSerializable(typeof(PolicyObligation))]
[JsonSerializable(typeof(PolicyObligation[]))]
[JsonSerializable(typeof(PolicyPushdown))]
[JsonSerializable(typeof(PolicyAuditInfo))]
[JsonSerializable(typeof(PrincipalContext))]
[JsonSerializable(typeof(ClaimValue))]
[JsonSerializable(typeof(ClaimValue[]))]
[JsonSerializable(typeof(TenantMembership))]
[JsonSerializable(typeof(TenantMembership[]))]
[JsonSerializable(typeof(OperationContext))]
[JsonSerializable(typeof(RequestContext))]
[JsonSerializable(typeof(AccessSubject))]
[JsonSerializable(typeof(AccessSubject[]))]
[JsonSerializable(typeof(AccessGrant))]
[JsonSerializable(typeof(AccessGrant[]))]
[JsonSerializable(typeof(ResourceScope))]
[JsonSerializable(typeof(OperationResult))]
[JsonSerializable(typeof(OperationResult<RecordPage>))]
[JsonSerializable(typeof(OperationResult<RecordEnvelope>))]
[JsonSerializable(typeof(OperationResult<DeleteResult>))]
[JsonSerializable(typeof(OperationResult<RecordUpsertResult>))]
[JsonSerializable(typeof(OperationResult<BaseRecordBatchResult>))]
[JsonSerializable(typeof(OperationResult<RecordMutationSessionResult>))]
[JsonSerializable(typeof(OperationResult<EventPublishResult>))]
[JsonSerializable(typeof(OperationResult<BaseManifest>))]
[JsonSerializable(typeof(OperationResult<CapabilityDescriptor>))]
[JsonSerializable(typeof(OperationResult<SchemaMetadata>))]
[JsonSerializable(typeof(OperationResult<PolicyDecision>))]
[JsonSerializable(typeof(OperationResult<HealthDescriptor[]>))]
[JsonSerializable(typeof(OperationResult<DiagnosticDescriptor[]>))]
[JsonSerializable(typeof(BaseError))]
[JsonSerializable(typeof(ValidationIssue))]
[JsonSerializable(typeof(ValidationIssue[]))]
[JsonSerializable(typeof(ConflictInfo))]
[JsonSerializable(typeof(CapabilityErrorInfo))]
[JsonSerializable(typeof(PolicyErrorInfo))]
[JsonSerializable(typeof(StoreErrorInfo))]
[JsonSerializable(typeof(OperationWarning))]
[JsonSerializable(typeof(OperationWarning[]))]
[JsonSerializable(typeof(OperationDiagnostics))]
[JsonSerializable(typeof(RevisionInfo))]
[JsonSerializable(typeof(EventReference))]
[JsonSerializable(typeof(EventReference[]))]
[JsonSerializable(typeof(BaseRecordMutationEvent))]
[JsonSerializable(typeof(BaseMutationJournalEntry))]
[JsonSerializable(typeof(BaseMutationJournalEntry[]))]
[JsonSerializable(typeof(BaseMutationJournalReadRequest))]
[JsonSerializable(typeof(BaseMutationJournalPage))]
[JsonSerializable(typeof(BaseMutationJournalPosition))]
[JsonSerializable(typeof(BaseMutationJournalBounds))]
[JsonSerializable(typeof(EventResource))]
[JsonSerializable(typeof(EventPrincipalSummary))]
[JsonSerializable(typeof(RecordSnapshot))]
[JsonSerializable(typeof(EventPublishResult))]
[JsonSerializable(typeof(HealthDescriptor))]
[JsonSerializable(typeof(HealthDescriptor[]))]
[JsonSerializable(typeof(HealthDependency))]
[JsonSerializable(typeof(HealthDependency[]))]
[JsonSerializable(typeof(HealthMetric))]
[JsonSerializable(typeof(HealthMetric[]))]
[JsonSerializable(typeof(DiagnosticDescriptor))]
[JsonSerializable(typeof(DiagnosticDescriptor[]))]
public partial class HPDBaseJsonSerializerContext : JsonSerializerContext
{
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(Default.Options)
        {
            TypeInfoResolver = Default
        };

        return options;
    }
}
