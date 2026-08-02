namespace HPD.Base;

internal static class VolatileErrorCodes
{
    /// <summary>Provides the not found value.</summary>
    public const string NotFound = "base.volatile.record.notFound";
    /// <summary>Provides the duplicate ID value.</summary>
    public const string DuplicateId = "base.volatile.record.duplicateId";
    /// <summary>Provides the invalid record ID value.</summary>
    public const string InvalidRecordId = "base.volatile.recordId.invalid";
    /// <summary>Provides the invalid collection ID value.</summary>
    public const string InvalidCollectionId = "base.volatile.collectionId.invalid";
    /// <summary>Provides the idempotency unsupported value.</summary>
    public const string IdempotencyUnsupported = "base.volatile.create.idempotencyUnsupported";
    /// <summary>Provides the requested ID unsupported value.</summary>
    public const string RequestedIdUnsupported = "base.volatile.create.requestedIdUnsupported";
    /// <summary>Provides the payload required value.</summary>
    public const string PayloadRequired = "base.volatile.payload.required";
    /// <summary>Provides the object payload required value.</summary>
    public const string ObjectPayloadRequired = "base.volatile.payload.objectRequired";
    /// <summary>Provides the patch unsupported shape value.</summary>
    public const string PatchUnsupportedShape = "base.volatile.patch.unsupportedShape";
    /// <summary>Provides the empty patch value.</summary>
    public const string EmptyPatch = "base.volatile.patch.empty";
    /// <summary>Provides the invalid field value.</summary>
    public const string InvalidField = "base.volatile.field.invalid";
    /// <summary>Provides the unsupported query value.</summary>
    public const string UnsupportedQuery = "base.volatile.query.unsupported";
    /// <summary>Provides the invalid query value.</summary>
    public const string InvalidQuery = "base.volatile.query.invalid";
    /// <summary>Provides the mutation processor failed value.</summary>
    public const string MutationProcessorFailed = "base.volatile.mutation.processorFailed";
    /// <summary>Provides the session closed value.</summary>
    public const string SessionClosed = "base.volatile.mutation.sessionClosed";
    /// <summary>Provides the session operation cancelled value.</summary>
    public const string SessionOperationCancelled = "base.volatile.mutation.sessionOperationCancelled";
}
