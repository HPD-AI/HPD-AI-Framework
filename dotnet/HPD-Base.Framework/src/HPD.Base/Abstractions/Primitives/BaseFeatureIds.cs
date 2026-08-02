namespace HPD.Base;

/// <summary>
/// First-party feature ids. Modules can add ids without changing this type.
/// </summary>
public static class BaseFeatureIds
{
    /// <summary>Provides the records list value.</summary>
    public const string RecordsList = "records.list";
    /// <summary>Provides the records query value.</summary>
    public const string RecordsQuery = "records.query";
    /// <summary>Provides the records get value.</summary>
    public const string RecordsGet = "records.get";
    /// <summary>Provides the records create value.</summary>
    public const string RecordsCreate = "records.create";
    /// <summary>Provides the records patch value.</summary>
    public const string RecordsPatch = "records.patch";
    /// <summary>Provides the records replace value.</summary>
    public const string RecordsReplace = "records.replace";
    /// <summary>Provides the records delete value.</summary>
    public const string RecordsDelete = "records.delete";
    /// <summary>Runtime understands ordered record batches.</summary>
    public const string RecordsBatch = "records.batch";
    /// <summary>Runtime understands atomic record-ID upsert.</summary>
    public const string RecordsUpsert = "records.upsert";
    /// <summary>Provides the records revision value.</summary>
    public const string RecordsRevision = "records.revision";
    /// <summary>Provides the records streaming value.</summary>
    public const string RecordsStreaming = "records.streaming";
    /// <summary>Provides the schema read value.</summary>
    public const string SchemaRead = "schema.read";
    /// <summary>Provides the capabilities read value.</summary>
    public const string CapabilitiesRead = "capabilities.read";
    /// <summary>Provides the health read value.</summary>
    public const string HealthRead = "health.read";
    /// <summary>Provides the diagnostics read value.</summary>
    public const string DiagnosticsRead = "diagnostics.read";
    /// <summary>Provides the events publish value.</summary>
    public const string EventsPublish = "events.publish";
    /// <summary>Provides the policy evaluate value.</summary>
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

/// <summary>Represents a base capability families.</summary>
public static class BaseCapabilityFamilies
{
    /// <summary>Provides the store value.</summary>
    public const string Store = "store";
    /// <summary>Provides the query value.</summary>
    public const string Query = "query";
    /// <summary>Provides the policy value.</summary>
    public const string Policy = "policy";
    /// <summary>Provides the schema value.</summary>
    public const string Schema = "schema";
    /// <summary>Provides the events value.</summary>
    public const string Events = "events";
    /// <summary>Provides the health value.</summary>
    public const string Health = "health";
    /// <summary>Provides the diagnostics value.</summary>
    public const string Diagnostics = "diagnostics";
    /// <summary>Provides the projection value.</summary>
    public const string Projection = "projection";
    /// <summary>Provides the files value.</summary>
    public const string Files = "files";
    /// <summary>Provides the realtime value.</summary>
    public const string Realtime = "realtime";
    /// <summary>Provides the batch value.</summary>
    public const string Batch = "batch";
    /// <summary>Provides the search value.</summary>
    public const string Search = "search";
    /// <summary>Provides the vector value.</summary>
    public const string Vector = "vector";
}
