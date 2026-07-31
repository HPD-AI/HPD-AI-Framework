namespace HPD.Base;

public static class BaseRouteIds
{
    public const string Manifest = "base.manifest";
    public const string Capabilities = "base.capabilities";
    public const string Schema = "base.schema";
    public const string Health = "base.health";
    public const string Diagnostics = "base.diagnostics";
    public const string RecordsList = "records.list";
    public const string RecordsQuery = "records.query";
    public const string RecordsGet = "records.get";
    public const string RecordsCreate = "records.create";
    public const string RecordsPatch = "records.patch";
    public const string RecordsReplace = "records.replace";
    public const string RecordsDelete = "records.delete";
    /// <summary>Ordered record-batch endpoint.</summary>
    public const string RecordsBatch = "records.batch";
    /// <summary>Atomic record-ID upsert endpoint.</summary>
    public const string RecordsUpsert = "records.upsert";
}

public static class BaseDtoIds
{
    public const string Manifest = "base.manifest";
    public const string CapabilityDescriptor = "base.capabilityDescriptor";
    public const string SchemaMetadata = "base.schemaMetadata";
    public const string RecordEnvelope = "base.recordEnvelope";
    public const string RecordPage = "base.recordPage";
    public const string BaseError = "base.error";
    public const string BaseRecordMutationEvent = "base.recordMutationEvent";
    /// <summary>Ordered record-batch request contract.</summary>
    public const string BaseRecordBatchRequest = "base.recordBatchRequest";
    /// <summary>Ordered record-batch result contract.</summary>
    public const string BaseRecordBatchResult = "base.recordBatchResult";
    /// <summary>Atomic record-ID upsert request contract.</summary>
    public const string RecordUpsertRequest = "base.recordUpsertRequest";
    /// <summary>Atomic record-ID upsert result contract.</summary>
    public const string RecordUpsertResult = "base.recordUpsertResult";
    public const string HealthDescriptor = "base.healthDescriptor";
    public const string DiagnosticDescriptor = "base.diagnosticDescriptor";
}

public static class BaseEventTypes
{
    public const string RecordCreated = "record.created";
    public const string RecordUpdated = "record.updated";
    public const string RecordPatched = "record.patched";
    public const string RecordDeleted = "record.deleted";
    public const string SchemaRefreshed = "schema.refreshed";
    public const string CapabilityChanged = "capability.changed";
    public const string HealthChanged = "health.changed";
    public const string DiagnosticEmitted = "diagnostic.emitted";
}
