using System.Text.Json;

namespace HPD.Base;

public sealed record RelationalCapabilityDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public string FamilyId { get; init; } = RelationalCapabilityFamilies.Relational;
    public required string Version { get; init; }
    public required RelationalMetadataCapability Metadata { get; init; }
    public required RelationalMappingCapability Mapping { get; init; }
    public required RelationalQueryPlanningCapability QueryPlanning { get; init; }
    public RelationalConstraintCapability? Constraints { get; init; }
    public RelationalJoinIncludeCapability? JoinsIncludes { get; init; }
    public RelationalTransactionCapability? Transactions { get; init; }
    public RelationalSchemaWriteCapability? SchemaWrite { get; init; }
    public RelationalNativePolicyCapability? NativePolicy { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalMetadataCapability
{
    public string FeatureId { get; init; } = RelationalFeatureIds.MetadataRead;
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    public bool StoreMetadata { get; init; }
    public bool NamespaceMetadata { get; init; }
    public bool TableMetadata { get; init; }
    public bool ViewMetadata { get; init; }
    public bool ColumnMetadata { get; init; }
    public bool NativeDefinitionsRedactedByDefault { get; init; } = true;
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalMappingCapability
{
    public string FeatureId { get; init; } = RelationalFeatureIds.CollectionMappingRead;
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    public bool CollectionMappings { get; init; }
    public bool FieldMappings { get; init; }
    public bool JsonColumnMappings { get; init; }
    public bool RelationMappings { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalQueryPlanningCapability
{
    public string FeatureId { get; init; } = RelationalFeatureIds.QueryPlanExplain;
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    public bool ExplainOnly { get; init; } = true;
    public bool NativePushdownSummary { get; init; }
    public bool ResidualSafetyDiagnostics { get; init; }
    public bool IncludePlanningDiagnostics { get; init; }
    public bool CountPageSafetyDiagnostics { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalConstraintCapability
{
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    public bool PrimaryKeys { get; init; }
    public bool ForeignKeys { get; init; }
    public bool UniqueConstraints { get; init; }
    public bool CheckConstraints { get; init; }
    public bool ProviderConstraints { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalJoinIncludeCapability
{
    public string FeatureId { get; init; } = RelationalFeatureIds.JoinsIncludes;
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    public bool NativeEngineSupportsJoins { get; init; }
    public bool IncludePlanExplanationSupported { get; init; }
    public bool CallableIncludeExecutionAvailable { get; init; }
    public string? Summary { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalTransactionCapability
{
    public string FeatureId { get; init; } = RelationalFeatureIds.Transactions;
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    public bool NativeEngineSupportsTransactions { get; init; }
    public bool CallableInterfaceAvailable { get; init; }
    public string? Summary { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalSchemaWriteCapability
{
    public string FeatureId { get; init; } = RelationalFeatureIds.SchemaWrite;
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    public bool NativeEngineSupportsDefinitionChanges { get; init; }
    public bool CallableInterfaceAvailable { get; init; }
    public bool DefinitionChangeRunnerAvailable { get; init; }
    public string? Summary { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalNativePolicyCapability
{
    public string FeatureId { get; init; } = RelationalFeatureIds.NativePolicyProjection;
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    public bool NativePolicyMechanismKnown { get; init; }
    public bool ProjectionExplainOnly { get; init; } = true;
    public bool CallablePolicyAdministrationAvailable { get; init; }
    public string? Summary { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
