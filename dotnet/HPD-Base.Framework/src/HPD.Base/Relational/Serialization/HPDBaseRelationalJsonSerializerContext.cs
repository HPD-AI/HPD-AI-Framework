using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base.Descriptors;
using HPD.Base.Query;
using HPD.Base.Relational.Capabilities;
using HPD.Base.Relational.Descriptors;
using HPD.Base.Relational.Planning;
using HPD.Base.Results;
using HPD.Base.Serialization;

namespace HPD.Base.Relational.Serialization;

using VisibilityLevelConverter = LowerCamelJsonStringEnumConverter<VisibilityLevel>;
using CapabilityStatusConverter = LowerCamelJsonStringEnumConverter<CapabilityStatus>;
using QueryCountModeConverter = LowerCamelJsonStringEnumConverter<QueryCountMode>;
using QueryPaginationModeConverter = LowerCamelJsonStringEnumConverter<QueryPaginationMode>;
using QueryCursorDirectionConverter = LowerCamelJsonStringEnumConverter<QueryCursorDirection>;
using QuerySortDirectionConverter = LowerCamelJsonStringEnumConverter<QuerySortDirection>;
using QueryNullOrderConverter = LowerCamelJsonStringEnumConverter<QueryNullOrder>;
using FilterNodeKindConverter = LowerCamelJsonStringEnumConverter<FilterNodeKind>;
using FilterOperatorConverter = LowerCamelJsonStringEnumConverter<FilterOperator>;
using QueryValueKindConverter = LowerCamelJsonStringEnumConverter<QueryValueKind>;
using RelationalObjectKindConverter = LowerCamelJsonStringEnumConverter<RelationalObjectKind>;
using RelationalNamespaceKindConverter = LowerCamelJsonStringEnumConverter<RelationalNamespaceKind>;
using RelationalTableKindConverter = LowerCamelJsonStringEnumConverter<RelationalTableKind>;
using RelationalViewKindConverter = LowerCamelJsonStringEnumConverter<RelationalViewKind>;
using RelationalViewMaterializationKindConverter = LowerCamelJsonStringEnumConverter<RelationalViewMaterializationKind>;
using RelationalColumnTypeFamilyConverter = LowerCamelJsonStringEnumConverter<RelationalColumnTypeFamily>;
using RelationalGeneratedColumnKindConverter = LowerCamelJsonStringEnumConverter<RelationalGeneratedColumnKind>;
using RelationalJsonStorageKindConverter = LowerCamelJsonStringEnumConverter<RelationalJsonStorageKind>;
using RelationalMappingKindConverter = LowerCamelJsonStringEnumConverter<RelationalMappingKind>;
using RelationalPayloadMappingKindConverter = LowerCamelJsonStringEnumConverter<RelationalPayloadMappingKind>;
using RelationalRecordIdMappingKindConverter = LowerCamelJsonStringEnumConverter<RelationalRecordIdMappingKind>;
using RelationalConstraintEnforcementKindConverter = LowerCamelJsonStringEnumConverter<RelationalConstraintEnforcementKind>;
using RelationalColumnWriteBehaviorConverter = LowerCamelJsonStringEnumConverter<RelationalColumnWriteBehavior>;
using RelationalFieldConversionKindConverter = LowerCamelJsonStringEnumConverter<RelationalFieldConversionKind>;
using RelationalQueryPlanStatusConverter = LowerCamelJsonStringEnumConverter<RelationalQueryPlanStatus>;
using RelationalPushdownSupportConverter = LowerCamelJsonStringEnumConverter<RelationalPushdownSupport>;
using RelationalResidualKindConverter = LowerCamelJsonStringEnumConverter<RelationalResidualKind>;
using RelationalPolicyPlanKindConverter = LowerCamelJsonStringEnumConverter<RelationalPolicyPlanKind>;
using RelationalPlanDiagnosticSeverityConverter = LowerCamelJsonStringEnumConverter<RelationalPlanDiagnosticSeverity>;
using OperationStatusConverter = LowerCamelJsonStringEnumConverter<OperationStatus>;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    UseStringEnumConverter = true,
    Converters = new[]
    {
        typeof(RecordIdJsonConverter),
        typeof(RevisionTokenJsonConverter),
        typeof(VisibilityLevelConverter),
        typeof(CapabilityStatusConverter),
        typeof(QueryCountModeConverter),
        typeof(QueryPaginationModeConverter),
        typeof(QueryCursorDirectionConverter),
        typeof(QuerySortDirectionConverter),
        typeof(QueryNullOrderConverter),
        typeof(FilterNodeKindConverter),
        typeof(FilterOperatorConverter),
        typeof(QueryValueKindConverter),
        typeof(RelationalObjectKindConverter),
        typeof(RelationalNamespaceKindConverter),
        typeof(RelationalTableKindConverter),
        typeof(RelationalViewKindConverter),
        typeof(RelationalViewMaterializationKindConverter),
        typeof(RelationalColumnTypeFamilyConverter),
        typeof(RelationalGeneratedColumnKindConverter),
        typeof(RelationalJsonStorageKindConverter),
        typeof(RelationalMappingKindConverter),
        typeof(RelationalPayloadMappingKindConverter),
        typeof(RelationalRecordIdMappingKindConverter),
        typeof(RelationalConstraintEnforcementKindConverter),
        typeof(RelationalColumnWriteBehaviorConverter),
        typeof(RelationalFieldConversionKindConverter),
        typeof(RelationalQueryPlanStatusConverter),
        typeof(RelationalPushdownSupportConverter),
        typeof(RelationalResidualKindConverter),
        typeof(RelationalPolicyPlanKindConverter),
        typeof(RelationalPlanDiagnosticSeverityConverter),
        typeof(OperationStatusConverter)
    })]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(RecordQuery))]
[JsonSerializable(typeof(QuerySort))]
[JsonSerializable(typeof(QueryPage))]
[JsonSerializable(typeof(QueryInclude))]
[JsonSerializable(typeof(QueryExtension))]
[JsonSerializable(typeof(FilterExpression))]
[JsonSerializable(typeof(QueryValue))]
[JsonSerializable(typeof(VisibilityLevel))]
[JsonSerializable(typeof(CapabilityStatus))]
[JsonSerializable(typeof(RelationalObjectKind))]
[JsonSerializable(typeof(RelationalNamespaceKind))]
[JsonSerializable(typeof(RelationalTableKind))]
[JsonSerializable(typeof(RelationalViewKind))]
[JsonSerializable(typeof(RelationalViewMaterializationKind))]
[JsonSerializable(typeof(RelationalColumnTypeFamily))]
[JsonSerializable(typeof(RelationalGeneratedColumnKind))]
[JsonSerializable(typeof(RelationalJsonStorageKind))]
[JsonSerializable(typeof(RelationalMappingKind))]
[JsonSerializable(typeof(RelationalPayloadMappingKind))]
[JsonSerializable(typeof(RelationalRecordIdMappingKind))]
[JsonSerializable(typeof(RelationalConstraintEnforcementKind))]
[JsonSerializable(typeof(RelationalColumnWriteBehavior))]
[JsonSerializable(typeof(RelationalFieldConversionKind))]
[JsonSerializable(typeof(RelationalQueryPlanStatus))]
[JsonSerializable(typeof(RelationalPushdownSupport))]
[JsonSerializable(typeof(RelationalResidualKind))]
[JsonSerializable(typeof(RelationalPolicyPlanKind))]
[JsonSerializable(typeof(RelationalPlanDiagnosticSeverity))]
[JsonSerializable(typeof(OperationStatus))]
[JsonSerializable(typeof(RelationalStoreDescriptor))]
[JsonSerializable(typeof(RelationalStoreDescriptor[]))]
[JsonSerializable(typeof(RelationalProviderDescriptor))]
[JsonSerializable(typeof(RelationalDatabaseDescriptor))]
[JsonSerializable(typeof(RelationalDatabaseDescriptor[]))]
[JsonSerializable(typeof(RelationalCatalogDescriptor))]
[JsonSerializable(typeof(RelationalCatalogDescriptor[]))]
[JsonSerializable(typeof(RelationalSchemaDescriptor))]
[JsonSerializable(typeof(RelationalSchemaDescriptor[]))]
[JsonSerializable(typeof(RelationalTableDescriptor))]
[JsonSerializable(typeof(RelationalTableDescriptor[]))]
[JsonSerializable(typeof(RelationalViewDescriptor))]
[JsonSerializable(typeof(RelationalViewDescriptor[]))]
[JsonSerializable(typeof(RelationalViewMaterializationDescriptor))]
[JsonSerializable(typeof(RelationalColumnDescriptor))]
[JsonSerializable(typeof(RelationalColumnDescriptor[]))]
[JsonSerializable(typeof(RelationalColumnTypeDescriptor))]
[JsonSerializable(typeof(RelationalPrimaryKeyDescriptor))]
[JsonSerializable(typeof(RelationalPrimaryKeyDescriptor[]))]
[JsonSerializable(typeof(RelationalForeignKeyDescriptor))]
[JsonSerializable(typeof(RelationalForeignKeyDescriptor[]))]
[JsonSerializable(typeof(RelationalForeignKeyColumnMapping))]
[JsonSerializable(typeof(RelationalUniqueConstraintDescriptor))]
[JsonSerializable(typeof(RelationalUniqueConstraintDescriptor[]))]
[JsonSerializable(typeof(RelationalCheckConstraintDescriptor))]
[JsonSerializable(typeof(RelationalCheckConstraintDescriptor[]))]
[JsonSerializable(typeof(RelationalProviderConstraintDescriptor))]
[JsonSerializable(typeof(RelationalProviderConstraintDescriptor[]))]
[JsonSerializable(typeof(RelationalIndexDescriptor))]
[JsonSerializable(typeof(RelationalIndexDescriptor[]))]
[JsonSerializable(typeof(RelationalIndexPartDescriptor))]
[JsonSerializable(typeof(RelationalGeneratedColumnDescriptor))]
[JsonSerializable(typeof(RelationalGeneratedColumnDescriptor[]))]
[JsonSerializable(typeof(RelationalJsonColumnDescriptor))]
[JsonSerializable(typeof(RelationalJsonColumnDescriptor[]))]
[JsonSerializable(typeof(RelationalCollectionMappingDescriptor))]
[JsonSerializable(typeof(RelationalCollectionMappingDescriptor[]))]
[JsonSerializable(typeof(RelationalFieldMappingDescriptor))]
[JsonSerializable(typeof(RelationalFieldMappingDescriptor[]))]
[JsonSerializable(typeof(RelationalRelationMappingDescriptor))]
[JsonSerializable(typeof(RelationalRelationMappingDescriptor[]))]
[JsonSerializable(typeof(RelationalCapabilityDescriptor))]
[JsonSerializable(typeof(RelationalMetadataCapability))]
[JsonSerializable(typeof(RelationalMappingCapability))]
[JsonSerializable(typeof(RelationalQueryPlanningCapability))]
[JsonSerializable(typeof(RelationalConstraintCapability))]
[JsonSerializable(typeof(RelationalJoinIncludeCapability))]
[JsonSerializable(typeof(RelationalTransactionCapability))]
[JsonSerializable(typeof(RelationalSchemaWriteCapability))]
[JsonSerializable(typeof(RelationalNativePolicyCapability))]
[JsonSerializable(typeof(RelationalQueryPlanRequest))]
[JsonSerializable(typeof(RelationalQueryPlanDescriptor))]
[JsonSerializable(typeof(RelationalQueryPushdownDescriptor))]
[JsonSerializable(typeof(RelationalResidualDescriptor))]
[JsonSerializable(typeof(RelationalCountPlanDescriptor))]
[JsonSerializable(typeof(RelationalPagePlanDescriptor))]
[JsonSerializable(typeof(RelationalSortPlanDescriptor))]
[JsonSerializable(typeof(RelationalIncludePlanDescriptor))]
[JsonSerializable(typeof(RelationalIncludePlanDescriptor[]))]
[JsonSerializable(typeof(RelationalPolicyPlanDescriptor))]
[JsonSerializable(typeof(RelationalQueryPlanStageDescriptor))]
[JsonSerializable(typeof(RelationalQueryPlanStageDescriptor[]))]
[JsonSerializable(typeof(RelationalPlanDiagnostic))]
[JsonSerializable(typeof(RelationalPlanDiagnostic[]))]
[JsonSerializable(typeof(OperationResult<RelationalStoreDescriptor>))]
[JsonSerializable(typeof(OperationResult<RelationalTableDescriptor[]>))]
[JsonSerializable(typeof(OperationResult<RelationalViewDescriptor[]>))]
[JsonSerializable(typeof(OperationResult<RelationalCollectionMappingDescriptor?>))]
[JsonSerializable(typeof(OperationResult<RelationalCollectionMappingDescriptor[]>))]
[JsonSerializable(typeof(OperationResult<RelationalQueryPlanDescriptor>))]
public partial class HPDBaseRelationalJsonSerializerContext : JsonSerializerContext
{
}
