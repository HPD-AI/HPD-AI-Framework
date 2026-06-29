namespace HPD.Base.InMemory.Internal;

internal static class InMemoryErrorCodes
{
    public const string NotFound = "base.inmemory.record.notFound";
    public const string DuplicateId = "base.inmemory.record.duplicateId";
    public const string InvalidRecordId = "base.inmemory.recordId.invalid";
    public const string InvalidCollectionId = "base.inmemory.collectionId.invalid";
    public const string IdempotencyUnsupported = "base.inmemory.create.idempotencyUnsupported";
    public const string RequestedIdUnsupported = "base.inmemory.create.requestedIdUnsupported";
    public const string PayloadRequired = "base.inmemory.payload.required";
    public const string ObjectPayloadRequired = "base.inmemory.payload.objectRequired";
    public const string PatchUnsupportedShape = "base.inmemory.patch.unsupportedShape";
    public const string EmptyPatch = "base.inmemory.patch.empty";
    public const string InvalidField = "base.inmemory.field.invalid";
    public const string RevisionMismatch = "base.inmemory.revision.mismatch";
    public const string ExpectedRevisionConflict = "base.inmemory.revision.expectedConflict";
    public const string UnsupportedQuery = "base.inmemory.query.unsupported";
    public const string InvalidQuery = "base.inmemory.query.invalid";
    public const string StoreError = "base.inmemory.store.error";
}
