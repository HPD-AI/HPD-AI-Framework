namespace HPD.Base.Sqlite;

internal static class SqliteErrorCodes
{
    public const string InvalidCollectionId = "sqlite.validation.collectionId";
    public const string InvalidRecordId = "sqlite.validation.recordId";
    public const string InvalidField = "sqlite.validation.field";
    public const string InvalidRevisionToken = "sqlite.validation.revisionToken";
    public const string InvalidOptions = "sqlite.validation.options";
    public const string CollectionNotRegistered = "sqlite.collection.notRegistered";
    public const string NotFound = "base.runtime.record.notFound";
    public const string DuplicateId = "sqlite.record.duplicateId";
    public const string RevisionMismatch = "base.runtime.revision.conflict";
    public const string UnsupportedQuery = "sqlite.query.unsupported";
    public const string UnsafeQuery = "sqlite.query.unsafePlan";
    public const string IdempotencyUnsupported = "sqlite.record.idempotencyUnsupported";
    public const string RequestedIdUnsupported = "sqlite.record.requestedIdUnsupported";
    public const string DatabaseBusy = "sqlite.database.busy";
    public const string DatabaseLocked = "sqlite.database.locked";
    public const string DatabaseReadOnly = "sqlite.database.readOnly";
    public const string DatabaseIoError = "sqlite.database.ioError";
    public const string DatabaseFull = "sqlite.database.full";
    public const string DatabaseCantOpen = "sqlite.database.cantOpen";
    public const string DatabaseAuthDenied = "sqlite.database.authDenied";
    public const string DatabaseUnavailable = "sqlite.database.unavailable";
    public const string DatabaseCorrupt = "sqlite.database.corrupt";
    public const string ConstraintFailed = "sqlite.constraint.failed";
    public const string OperationCancelled = "sqlite.operation.cancelled";
    public const string SessionClosed = "sqlite.mutation.sessionClosed";
    public const string SchemaMissing = "sqlite.schema.missing";
}
