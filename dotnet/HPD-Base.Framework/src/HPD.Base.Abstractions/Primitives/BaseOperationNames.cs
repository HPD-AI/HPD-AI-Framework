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
}

public static class BaseDtoIds
{
    public const string Manifest = "base.manifest";
    public const string CapabilityDescriptor = "base.capabilityDescriptor";
    public const string SchemaMetadata = "base.schemaMetadata";
    public const string RecordEnvelope = "base.recordEnvelope";
    public const string RecordPage = "base.recordPage";
    public const string BaseError = "base.error";
    public const string BaseEventEnvelope = "base.eventEnvelope";
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
