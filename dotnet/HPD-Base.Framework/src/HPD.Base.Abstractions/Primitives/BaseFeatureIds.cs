namespace HPD.Base;

/// <summary>
/// First-party feature ids. Modules can add ids without changing this type.
/// </summary>
public static class BaseFeatureIds
{
    public const string RecordsList = "records.list";
    public const string RecordsQuery = "records.query";
    public const string RecordsGet = "records.get";
    public const string RecordsCreate = "records.create";
    public const string RecordsPatch = "records.patch";
    public const string RecordsReplace = "records.replace";
    public const string RecordsDelete = "records.delete";
    public const string RecordsRevision = "records.revision";
    public const string RecordsStreaming = "records.streaming";
    public const string SchemaRead = "schema.read";
    public const string CapabilitiesRead = "capabilities.read";
    public const string HealthRead = "health.read";
    public const string DiagnosticsRead = "diagnostics.read";
    public const string EventsPublish = "events.publish";
    public const string PolicyEvaluate = "policy.evaluate";
}

public static class BaseCapabilityFamilies
{
    public const string Store = "store";
    public const string Query = "query";
    public const string Policy = "policy";
    public const string Schema = "schema";
    public const string Events = "events";
    public const string Health = "health";
    public const string Diagnostics = "diagnostics";
    public const string Projection = "projection";
    public const string Files = "files";
    public const string Realtime = "realtime";
    public const string Batch = "batch";
    public const string Search = "search";
    public const string Vector = "vector";
}
