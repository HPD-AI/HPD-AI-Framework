namespace HPD.Base;

internal static class InMemoryErrorCodes
{
    /// <summary>Provides the not found value.</summary>
    public const string NotFound = "base.inmemory.record.notFound";
    /// <summary>Provides the duplicate ID value.</summary>
    public const string DuplicateId = "base.inmemory.record.duplicateId";
    /// <summary>Provides the invalid record ID value.</summary>
    public const string InvalidRecordId = "base.inmemory.recordId.invalid";
    /// <summary>Provides the invalid collection ID value.</summary>
    public const string InvalidCollectionId = "base.inmemory.collectionId.invalid";
    /// <summary>Provides the idempotency unsupported value.</summary>
    /// <summary>Provides the requested ID unsupported value.</summary>
    public const string RequestedIdUnsupported = "base.inmemory.create.requestedIdUnsupported";
    /// <summary>Provides the payload required value.</summary>
    public const string PayloadRequired = "base.inmemory.payload.required";
    /// <summary>Provides the object payload required value.</summary>
    public const string ObjectPayloadRequired = "base.inmemory.payload.objectRequired";
    /// <summary>Provides the patch unsupported shape value.</summary>
    public const string PatchUnsupportedShape = "base.inmemory.patch.unsupportedShape";
    /// <summary>Provides the empty patch value.</summary>
    public const string EmptyPatch = "base.inmemory.patch.empty";
    /// <summary>Provides the invalid field value.</summary>
    public const string InvalidField = "base.inmemory.field.invalid";
    /// <summary>Provides the unsupported query value.</summary>
    public const string UnsupportedQuery = "base.inmemory.query.unsupported";
    /// <summary>Provides the invalid query value.</summary>
    public const string InvalidQuery = "base.inmemory.query.invalid";
    /// <summary>Provides the mutation processor failed value.</summary>
    public const string MutationProcessorFailed = "base.inmemory.mutation.processorFailed";
    /// <summary>Provides the session closed value.</summary>
    public const string SessionClosed = "base.inmemory.mutation.sessionClosed";
    /// <summary>Provides the session operation cancelled value.</summary>
    public const string SessionOperationCancelled = "base.inmemory.mutation.sessionOperationCancelled";
}
