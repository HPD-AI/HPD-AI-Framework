using System.Text.Json;

namespace HPD.Base;

/// <summary>Represents a relational capability descriptor.</summary>
public sealed record RelationalCapabilityDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the family ID.</summary>
    public string FamilyId { get; init; } = RelationalCapabilityFamilies.Relational;
    /// <summary>Gets or sets the version.</summary>
    public required string Version { get; init; }
    /// <summary>Gets or sets the metadata.</summary>
    public required RelationalMetadataCapability Metadata { get; init; }
    /// <summary>Gets or sets the mapping.</summary>
    public required RelationalMappingCapability Mapping { get; init; }
    /// <summary>Gets or sets the query planning.</summary>
    public required RelationalQueryPlanningCapability QueryPlanning { get; init; }
    /// <summary>Gets or sets the constraints.</summary>
    public RelationalConstraintCapability? Constraints { get; init; }
    /// <summary>Gets or sets the joins includes.</summary>
    public RelationalJoinIncludeCapability? JoinsIncludes { get; init; }
    /// <summary>Gets or sets the transactions.</summary>
    public RelationalTransactionCapability? Transactions { get; init; }
    /// <summary>Gets or sets the schema write.</summary>
    public RelationalSchemaWriteCapability? SchemaWrite { get; init; }
    /// <summary>Gets or sets the native policy.</summary>
    public RelationalNativePolicyCapability? NativePolicy { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational metadata capability.</summary>
public sealed record RelationalMetadataCapability
{
    /// <summary>Gets or sets the feature ID.</summary>
    public string FeatureId { get; init; } = RelationalFeatureIds.MetadataRead;
    /// <summary>Gets or sets the status.</summary>
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    /// <summary>Gets or sets the store metadata.</summary>
    public bool StoreMetadata { get; init; }
    /// <summary>Gets or sets the namespace metadata.</summary>
    public bool NamespaceMetadata { get; init; }
    /// <summary>Gets or sets the table metadata.</summary>
    public bool TableMetadata { get; init; }
    /// <summary>Gets or sets the view metadata.</summary>
    public bool ViewMetadata { get; init; }
    /// <summary>Gets or sets the column metadata.</summary>
    public bool ColumnMetadata { get; init; }
    /// <summary>Gets or sets the native definitions redacted by default.</summary>
    public bool NativeDefinitionsRedactedByDefault { get; init; } = true;
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational mapping capability.</summary>
public sealed record RelationalMappingCapability
{
    /// <summary>Gets or sets the feature ID.</summary>
    public string FeatureId { get; init; } = RelationalFeatureIds.CollectionMappingRead;
    /// <summary>Gets or sets the status.</summary>
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    /// <summary>Gets or sets the collection mappings.</summary>
    public bool CollectionMappings { get; init; }
    /// <summary>Gets or sets the field mappings.</summary>
    public bool FieldMappings { get; init; }
    /// <summary>Gets or sets the JSON column mappings.</summary>
    public bool JsonColumnMappings { get; init; }
    /// <summary>Gets or sets the relation mappings.</summary>
    public bool RelationMappings { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational query planning capability.</summary>
public sealed record RelationalQueryPlanningCapability
{
    /// <summary>Gets or sets the feature ID.</summary>
    public string FeatureId { get; init; } = RelationalFeatureIds.QueryPlanExplain;
    /// <summary>Gets or sets the status.</summary>
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    /// <summary>Gets or sets the explain only.</summary>
    public bool ExplainOnly { get; init; } = true;
    /// <summary>Gets or sets the native pushdown summary.</summary>
    public bool NativePushdownSummary { get; init; }
    /// <summary>Gets or sets the residual safety diagnostics.</summary>
    public bool ResidualSafetyDiagnostics { get; init; }
    /// <summary>Gets or sets the include planning diagnostics.</summary>
    public bool IncludePlanningDiagnostics { get; init; }
    /// <summary>Gets or sets the count page safety diagnostics.</summary>
    public bool CountPageSafetyDiagnostics { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational constraint capability.</summary>
public sealed record RelationalConstraintCapability
{
    /// <summary>Gets or sets the status.</summary>
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    /// <summary>Gets or sets the primary keys.</summary>
    public bool PrimaryKeys { get; init; }
    /// <summary>Gets or sets the foreign keys.</summary>
    public bool ForeignKeys { get; init; }
    /// <summary>Gets or sets the unique constraints.</summary>
    public bool UniqueConstraints { get; init; }
    /// <summary>Gets or sets the check constraints.</summary>
    public bool CheckConstraints { get; init; }
    /// <summary>Gets or sets the provider constraints.</summary>
    public bool ProviderConstraints { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational join include capability.</summary>
public sealed record RelationalJoinIncludeCapability
{
    /// <summary>Gets or sets the feature ID.</summary>
    public string FeatureId { get; init; } = RelationalFeatureIds.JoinsIncludes;
    /// <summary>Gets or sets the status.</summary>
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    /// <summary>Gets or sets the native engine supports joins.</summary>
    public bool NativeEngineSupportsJoins { get; init; }
    /// <summary>Gets or sets the include plan explanation supported.</summary>
    public bool IncludePlanExplanationSupported { get; init; }
    /// <summary>Gets or sets the callable include execution available.</summary>
    public bool CallableIncludeExecutionAvailable { get; init; }
    /// <summary>Gets or sets the summary.</summary>
    public string? Summary { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational transaction capability.</summary>
public sealed record RelationalTransactionCapability
{
    /// <summary>Gets or sets the feature ID.</summary>
    public string FeatureId { get; init; } = RelationalFeatureIds.Transactions;
    /// <summary>Gets or sets the status.</summary>
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    /// <summary>Gets or sets the native engine supports transactions.</summary>
    public bool NativeEngineSupportsTransactions { get; init; }
    /// <summary>Gets or sets the callable interface available.</summary>
    public bool CallableInterfaceAvailable { get; init; }
    /// <summary>Gets or sets the summary.</summary>
    public string? Summary { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational schema write capability.</summary>
public sealed record RelationalSchemaWriteCapability
{
    /// <summary>Gets or sets the feature ID.</summary>
    public string FeatureId { get; init; } = RelationalFeatureIds.SchemaWrite;
    /// <summary>Gets or sets the status.</summary>
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    /// <summary>Gets or sets the native engine supports definition changes.</summary>
    public bool NativeEngineSupportsDefinitionChanges { get; init; }
    /// <summary>Gets or sets the callable interface available.</summary>
    public bool CallableInterfaceAvailable { get; init; }
    /// <summary>Gets or sets the definition change runner available.</summary>
    public bool DefinitionChangeRunnerAvailable { get; init; }
    /// <summary>Gets or sets the summary.</summary>
    public string? Summary { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational native policy capability.</summary>
public sealed record RelationalNativePolicyCapability
{
    /// <summary>Gets or sets the feature ID.</summary>
    public string FeatureId { get; init; } = RelationalFeatureIds.NativePolicyProjection;
    /// <summary>Gets or sets the status.</summary>
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Unavailable;
    /// <summary>Gets or sets the native policy mechanism known.</summary>
    public bool NativePolicyMechanismKnown { get; init; }
    /// <summary>Gets or sets the projection explain only.</summary>
    public bool ProjectionExplainOnly { get; init; } = true;
    /// <summary>Gets or sets the callable policy administration available.</summary>
    public bool CallablePolicyAdministrationAvailable { get; init; }
    /// <summary>Gets or sets the summary.</summary>
    public string? Summary { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
