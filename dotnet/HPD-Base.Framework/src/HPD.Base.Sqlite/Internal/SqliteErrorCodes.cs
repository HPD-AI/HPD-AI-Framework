namespace HPD.Base.Sqlite;

internal static class SqliteErrorCodes
{
    /// <summary>Provides the invalid collection ID value.</summary>
    public const string InvalidCollectionId = "sqlite.validation.collectionId";
    /// <summary>Provides the invalid record ID value.</summary>
    public const string InvalidRecordId = "sqlite.validation.recordId";
    /// <summary>Provides the invalid field value.</summary>
    public const string InvalidField = "sqlite.validation.field";
    /// <summary>Provides the invalid revision token value.</summary>
    public const string InvalidRevisionToken = "sqlite.validation.revisionToken";
    /// <summary>Provides the invalid options value.</summary>
    public const string InvalidOptions = "sqlite.validation.options";
    /// <summary>Provides the collection not registered value.</summary>
    public const string CollectionNotRegistered = "sqlite.collection.notRegistered";
    /// <summary>Provides the not found value.</summary>
    public const string NotFound = "base.runtime.record.notFound";
    /// <summary>Provides the duplicate ID value.</summary>
    public const string DuplicateId = "sqlite.record.duplicateId";
    /// <summary>Provides the revision mismatch value.</summary>
    public const string RevisionMismatch = "base.runtime.revision.conflict";
    /// <summary>Provides the unsupported query value.</summary>
    public const string UnsupportedQuery = "sqlite.query.unsupported";
    /// <summary>Provides the unsafe query value.</summary>
    public const string UnsafeQuery = "sqlite.query.unsafePlan";
    /// <summary>Provides the idempotency unsupported value.</summary>
    /// <summary>Provides the requested ID unsupported value.</summary>
    public const string RequestedIdUnsupported = "sqlite.record.requestedIdUnsupported";
    /// <summary>Provides the database busy value.</summary>
    public const string DatabaseBusy = "sqlite.database.busy";
    /// <summary>Provides the database locked value.</summary>
    public const string DatabaseLocked = "sqlite.database.locked";
    /// <summary>Provides the database read only value.</summary>
    public const string DatabaseReadOnly = "sqlite.database.readOnly";
    /// <summary>Provides the database io error value.</summary>
    public const string DatabaseIoError = "sqlite.database.ioError";
    /// <summary>Provides the database full value.</summary>
    public const string DatabaseFull = "sqlite.database.full";
    /// <summary>Provides the database cant open value.</summary>
    public const string DatabaseCantOpen = "sqlite.database.cantOpen";
    /// <summary>Provides the database auth denied value.</summary>
    public const string DatabaseAuthDenied = "sqlite.database.authDenied";
    /// <summary>Provides the database unavailable value.</summary>
    public const string DatabaseUnavailable = "sqlite.database.unavailable";
    /// <summary>Provides the database corrupt value.</summary>
    public const string DatabaseCorrupt = "sqlite.database.corrupt";
    /// <summary>Provides the constraint failed value.</summary>
    public const string ConstraintFailed = "sqlite.constraint.failed";
    /// <summary>Provides the operation cancelled value.</summary>
    public const string OperationCancelled = "sqlite.operation.cancelled";
    /// <summary>Provides the session closed value.</summary>
    public const string SessionClosed = "sqlite.mutation.sessionClosed";
    /// <summary>Provides the schema missing value.</summary>
    public const string SchemaMissing = "sqlite.schema.missing";
}
