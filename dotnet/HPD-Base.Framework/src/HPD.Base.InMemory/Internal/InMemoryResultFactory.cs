using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.Results;

namespace HPD.Base.InMemory.Internal;

internal static class InMemoryResultFactory
{
    public static OperationResult<T> NotFound<T>(string recordId) =>
        OperationResults.NotFound<T>(new BaseError
        {
            Code = InMemoryErrorCodes.NotFound,
            Message = "Record was not found.",
            Category = ErrorCategory.NotFound,
            Target = recordId
        });

    public static OperationResult<T> DuplicateId<T>(string recordId) =>
        OperationResults.Conflict<T>(new BaseError
        {
            Code = InMemoryErrorCodes.DuplicateId,
            Message = "A record with the requested id already exists in this collection.",
            Category = ErrorCategory.Conflict,
            Target = recordId,
            Conflict = new ConflictInfo { Kind = ConflictKind.Unique, Resource = recordId }
        });

    public static OperationResult<T> Unsupported<T>(string code, string message, string? target = null) =>
        OperationResults.Unsupported<T>(new BaseError
        {
            Code = code,
            Message = message,
            Category = ErrorCategory.Unsupported,
            Target = target
        });

    public static OperationResult<T> Validation<T>(string code, string message, string? target = null) =>
        OperationResults.ValidationFailed<T>(new BaseError
        {
            Code = code,
            Message = message,
            Category = ErrorCategory.Validation,
            Target = target
        });

    public static OperationResult<T> StoreError<T>(string code, string message, string? target = null) =>
        OperationResults.StoreError<T>(new BaseError
        {
            Code = code,
            Message = message,
            Category = ErrorCategory.Store,
            Target = target,
            Store = new StoreErrorInfo { Retryable = false }
        });

    public static OperationResult<T> RevisionConflict<T>(
        RevisionToken expected,
        RevisionToken? actual,
        string recordId) =>
        OperationResults.Conflict<T>(new BaseError
        {
            Code = BaseMutationErrorCodes.RevisionConflict,
            Message = "Record revision did not match the expected revision.",
            Category = ErrorCategory.Conflict,
            Target = recordId,
            Conflict = new ConflictInfo
            {
                Kind = ConflictKind.Revision,
                Resource = recordId,
                ExpectedRevision = expected.Value,
                ActualRevision = actual?.Value
            }
        });

    public static OperationResult<T> WithRevision<T>(OperationResult<T> result, RecordMetadata metadata) =>
        result with
        {
            Revision = new RevisionInfo
            {
                Revision = metadata.Revision?.Value,
                ETag = metadata.ETag,
                LastModified = metadata.UpdatedAt,
                Guarantee = RevisionGuarantee.Store
            }
        };
}
