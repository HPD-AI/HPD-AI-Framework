namespace HPD.Base;

/// <summary>Represents a base route IDs.</summary>
public static class BaseRouteIds
{
    /// <summary>Provides the manifest value.</summary>
    public const string Manifest = "base.manifest";
    /// <summary>Provides the capabilities value.</summary>
    public const string Capabilities = "base.capabilities";
    /// <summary>Provides the schema value.</summary>
    public const string Schema = "base.schema";
    /// <summary>Provides the health value.</summary>
    public const string Health = "base.health";
    /// <summary>Provides the diagnostics value.</summary>
    public const string Diagnostics = "base.diagnostics";
    /// <summary>Provides the records list value.</summary>
    public const string RecordsList = "base.records.list";
    /// <summary>Provides the records query value.</summary>
    public const string RecordsQuery = "base.records.query";
    /// <summary>Provides the records get value.</summary>
    public const string RecordsGet = "base.records.get";
    /// <summary>Provides the records create value.</summary>
    public const string RecordsCreate = "base.records.create";
    /// <summary>Provides the records patch value.</summary>
    public const string RecordsPatch = "base.records.patch";
    /// <summary>Provides the records replace value.</summary>
    public const string RecordsReplace = "base.records.replace";
    /// <summary>Provides the records delete value.</summary>
    public const string RecordsDelete = "base.records.delete";
    /// <summary>Ordered record-batch endpoint.</summary>
    public const string RecordsBatch = "base.records.batch";
    /// <summary>Atomic record-ID upsert endpoint.</summary>
    public const string RecordsUpsert = "base.records.upsert";
}

/// <summary>Represents a base DTO IDs.</summary>
public static class BaseDtoIds
{
    /// <summary>Provides the manifest value.</summary>
    public const string Manifest = "base.manifest";
    /// <summary>Provides the capability descriptor value.</summary>
    public const string CapabilityDescriptor = "base.capabilityDescriptor";
    /// <summary>Provides the schema metadata value.</summary>
    public const string SchemaMetadata = "base.schemaMetadata";
    /// <summary>Provides the record envelope value.</summary>
    public const string RecordEnvelope = "base.recordEnvelope";
    /// <summary>Provides the record page value.</summary>
    public const string RecordPage = "base.recordPage";
    /// <summary>Provides the base error value.</summary>
    public const string BaseError = "base.error";
    /// <summary>Provides the base record mutation event value.</summary>
    public const string BaseRecordMutationEvent = "base.recordMutationEvent";
    /// <summary>Ordered record-batch request contract.</summary>
    public const string BaseRecordBatchRequest = "base.recordBatchRequest";
    /// <summary>Ordered record-batch result contract.</summary>
    public const string BaseRecordBatchResult = "base.recordBatchResult";
    /// <summary>Atomic record-ID upsert request contract.</summary>
    public const string RecordUpsertRequest = "base.recordUpsertRequest";
    /// <summary>Atomic record-ID upsert result contract.</summary>
    public const string RecordUpsertResult = "base.recordUpsertResult";
    /// <summary>Provides the health descriptor value.</summary>
    public const string HealthDescriptor = "base.healthDescriptor";
    /// <summary>Provides the diagnostic descriptor value.</summary>
    public const string DiagnosticDescriptor = "base.diagnosticDescriptor";
}

/// <summary>Represents a base event types.</summary>
public static class BaseEventTypes
{
    /// <summary>Provides the record created value.</summary>
    public const string RecordCreated = "record.created";
    /// <summary>Provides the record updated value.</summary>
    public const string RecordUpdated = "record.updated";
    /// <summary>Provides the record patched value.</summary>
    public const string RecordPatched = "record.patched";
    /// <summary>Provides the record deleted value.</summary>
    public const string RecordDeleted = "record.deleted";
    /// <summary>Provides the schema refreshed value.</summary>
    public const string SchemaRefreshed = "schema.refreshed";
    /// <summary>Provides the capability changed value.</summary>
    public const string CapabilityChanged = "capability.changed";
    /// <summary>Provides the health changed value.</summary>
    public const string HealthChanged = "health.changed";
    /// <summary>Provides the diagnostic emitted value.</summary>
    public const string DiagnosticEmitted = "diagnostic.emitted";
}
