namespace HPD.Base;

internal static class VolatileErrorCodes
{
    public const string NotFound = "base.volatile.record.notFound";
    public const string DuplicateId = "base.volatile.record.duplicateId";
    public const string InvalidRecordId = "base.volatile.recordId.invalid";
    public const string InvalidCollectionId = "base.volatile.collectionId.invalid";
    public const string IdempotencyUnsupported = "base.volatile.create.idempotencyUnsupported";
    public const string RequestedIdUnsupported = "base.volatile.create.requestedIdUnsupported";
    public const string PayloadRequired = "base.volatile.payload.required";
    public const string ObjectPayloadRequired = "base.volatile.payload.objectRequired";
    public const string PatchUnsupportedShape = "base.volatile.patch.unsupportedShape";
    public const string EmptyPatch = "base.volatile.patch.empty";
    public const string InvalidField = "base.volatile.field.invalid";
    public const string UnsupportedQuery = "base.volatile.query.unsupported";
    public const string InvalidQuery = "base.volatile.query.invalid";
    public const string MutationProcessorFailed = "base.volatile.mutation.processorFailed";
    public const string SessionClosed = "base.volatile.mutation.sessionClosed";
    public const string SessionOperationCancelled = "base.volatile.mutation.sessionOperationCancelled";
}
