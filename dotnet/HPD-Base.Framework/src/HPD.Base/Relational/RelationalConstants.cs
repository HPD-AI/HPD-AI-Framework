namespace HPD.Base;

/// <summary>Represents a relational module IDs.</summary>
public static class RelationalModuleIds
{
    /// <summary>Provides the relational abstractions value.</summary>
    public const string RelationalAbstractions = "hpd.base.relational.abstractions";
}

/// <summary>Represents a relational capability families.</summary>
public static class RelationalCapabilityFamilies
{
    /// <summary>Provides the relational value.</summary>
    public const string Relational = "relational";
}

/// <summary>Represents a relational feature IDs.</summary>
public static class RelationalFeatureIds
{
    /// <summary>Provides the metadata read value.</summary>
    public const string MetadataRead = "relational.metadata.read";
    /// <summary>Provides the collection mapping read value.</summary>
    public const string CollectionMappingRead = "relational.mapping.collections.read";
    /// <summary>Provides the query plan explain value.</summary>
    public const string QueryPlanExplain = "relational.query.plan.explain";
    /// <summary>Provides the foreign keys value.</summary>
    public const string ForeignKeys = "relational.foreignKeys";
    /// <summary>Provides the views value.</summary>
    public const string Views = "relational.views";
    /// <summary>Provides the transactions value.</summary>
    public const string Transactions = "relational.transactions";
    /// <summary>Provides the native query pushdown value.</summary>
    public const string NativeQueryPushdown = "relational.query.nativePushdown";
    /// <summary>Provides the joins includes value.</summary>
    public const string JoinsIncludes = "relational.query.joinsIncludes";
    /// <summary>Provides the generated columns value.</summary>
    public const string GeneratedColumns = "relational.generatedColumns";
    /// <summary>Provides the JSON columns value.</summary>
    public const string JsonColumns = "relational.jsonColumns";
    /// <summary>Provides the schema write value.</summary>
    public const string SchemaWrite = "relational.schemaWrite";
    /// <summary>Provides the native policy projection value.</summary>
    public const string NativePolicyProjection = "relational.policy.nativeProjection";
}

/// <summary>Represents a relational DTO IDs.</summary>
public static class RelationalDtoIds
{
    /// <summary>Provides the store descriptor value.</summary>
    public const string StoreDescriptor = "hpd.base.relational.storeDescriptor.v1";
    /// <summary>Provides the collection mapping descriptor value.</summary>
    public const string CollectionMappingDescriptor = "hpd.base.relational.collectionMappingDescriptor.v1";
    /// <summary>Provides the query plan descriptor value.</summary>
    public const string QueryPlanDescriptor = "hpd.base.relational.queryPlanDescriptor.v1";
    /// <summary>Provides the capability descriptor value.</summary>
    public const string CapabilityDescriptor = "hpd.base.relational.capabilityDescriptor.v1";
}
