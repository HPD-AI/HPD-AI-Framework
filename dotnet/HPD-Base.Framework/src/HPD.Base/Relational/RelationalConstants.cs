namespace HPD.Base;

public static class RelationalModuleIds
{
    public const string RelationalAbstractions = "hpd.base.relational.abstractions";
}

public static class RelationalCapabilityFamilies
{
    public const string Relational = "relational";
}

public static class RelationalFeatureIds
{
    public const string MetadataRead = "relational.metadata.read";
    public const string CollectionMappingRead = "relational.mapping.collections.read";
    public const string QueryPlanExplain = "relational.query.plan.explain";
    public const string ForeignKeys = "relational.foreignKeys";
    public const string Views = "relational.views";
    public const string Transactions = "relational.transactions";
    public const string NativeQueryPushdown = "relational.query.nativePushdown";
    public const string JoinsIncludes = "relational.query.joinsIncludes";
    public const string GeneratedColumns = "relational.generatedColumns";
    public const string JsonColumns = "relational.jsonColumns";
    public const string SchemaWrite = "relational.schemaWrite";
    public const string NativePolicyProjection = "relational.policy.nativeProjection";
}

public static class RelationalDtoIds
{
    public const string StoreDescriptor = "hpd.base.relational.storeDescriptor.v1";
    public const string CollectionMappingDescriptor = "hpd.base.relational.collectionMappingDescriptor.v1";
    public const string QueryPlanDescriptor = "hpd.base.relational.queryPlanDescriptor.v1";
    public const string CapabilityDescriptor = "hpd.base.relational.capabilityDescriptor.v1";
}
