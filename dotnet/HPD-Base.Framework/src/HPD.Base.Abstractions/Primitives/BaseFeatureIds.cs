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
    /// <summary>Runtime understands ordered record batches.</summary>
    public const string RecordsBatch = "records.batch";
    /// <summary>Runtime understands atomic record-ID upsert.</summary>
    public const string RecordsUpsert = "records.upsert";
    public const string RecordsRevision = "records.revision";
    public const string RecordsStreaming = "records.streaming";
    public const string SchemaRead = "schema.read";
    public const string CapabilitiesRead = "capabilities.read";
    public const string HealthRead = "health.read";
    public const string DiagnosticsRead = "diagnostics.read";
    public const string EventsPublish = "events.publish";
    public const string PolicyEvaluate = "policy.evaluate";
    /// <summary>Ordered independent execution is available.</summary>
    public const string BatchOrderedIndependent = "batch.ordered.independent";
    /// <summary>Ordered stop-on-failure execution is available.</summary>
    public const string BatchOrderedStopOnFailure = "batch.ordered.stopOnFailure";
    /// <summary>Atomic batch execution is available.</summary>
    public const string BatchAtomic = "batch.atomic";
    /// <summary>Per-item partial results are available.</summary>
    public const string BatchPartialResults = "batch.partialResults";
    /// <summary>A store provides real atomic batch execution.</summary>
    public const string StoreBatchAtomic = "store.batch.atomic";
    /// <summary>A store provides cross-collection atomic batch execution.</summary>
    public const string StoreBatchCrossCollection = "store.batch.crossCollection";
    /// <summary>A store provides atomic record-ID upsert.</summary>
    public const string StoreRecordUpsertAtomic = "store.record.upsert.atomic";
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
