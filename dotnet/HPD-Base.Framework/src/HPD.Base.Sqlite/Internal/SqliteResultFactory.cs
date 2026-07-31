using HPD.Base;
using System.Globalization;

namespace HPD.Base.Sqlite;

internal static class SqliteResultFactory
{
    public static OperationResult<T> NotFound<T>(string recordId) =>
        OperationResults.NotFound<T>(new BaseError { Code = SqliteErrorCodes.NotFound, Message = "Record was not found.", Category = ErrorCategory.NotFound, Target = recordId });

    public static OperationResult<T> DuplicateId<T>(string recordId) =>
        OperationResults.Conflict<T>(new BaseError
        {
            Code = SqliteErrorCodes.DuplicateId,
            Message = "A record with the requested id already exists in this collection.",
            Category = ErrorCategory.Conflict,
            Target = recordId,
            Conflict = new ConflictInfo { Kind = ConflictKind.Unique, Resource = recordId }
        });

    public static OperationResult<T> RevisionConflict<T>(RevisionToken expected, RevisionToken? actual, string recordId) =>
        OperationResults.Conflict<T>(new BaseError
        {
            Code = SqliteErrorCodes.RevisionMismatch,
            Message = "Record revision did not match the expected revision.",
            Category = ErrorCategory.Conflict,
            Target = recordId,
            Conflict = new ConflictInfo { Kind = ConflictKind.Revision, Resource = recordId, ExpectedRevision = expected.Value, ActualRevision = actual?.Value }
        });

    public static OperationResult<T> Unsupported<T>(string code, string message, string? target = null) =>
        OperationResults.Unsupported<T>(new BaseError { Code = code, Message = message, Category = ErrorCategory.Unsupported, Target = target });

    public static OperationResult<T> Validation<T>(string code, string message, string? target = null) =>
        OperationResults.ValidationFailed<T>(new BaseError { Code = code, Message = message, Category = ErrorCategory.Validation, Target = target });

    public static OperationResult<T> StoreError<T>(string code, string message, string? target = null) =>
        OperationResults.StoreError<T>(new BaseError { Code = code, Message = message, Category = ErrorCategory.Store, Target = target, Store = new StoreErrorInfo { Retryable = code is SqliteErrorCodes.DatabaseBusy or SqliteErrorCodes.DatabaseLocked } });

    public static OperationResult<T> StoreError<T>(
        string code,
        string message,
        string storeId,
        int nativeCode,
        int nativeSubcode,
        string? nativeMessage,
        string? target = null) =>
        OperationResults.StoreError<T>(new BaseError
        {
            Code = code,
            Message = message,
            Category = ErrorCategory.Store,
            Target = target,
            Store = new StoreErrorInfo
            {
                StoreId = storeId,
                NativeCode = nativeCode.ToString(CultureInfo.InvariantCulture),
                NativeSubcode = nativeSubcode.ToString(CultureInfo.InvariantCulture),
                NativeCategory = "sqlite",
                NativeMessage = nativeMessage,
                Retryable = code is SqliteErrorCodes.DatabaseBusy or SqliteErrorCodes.DatabaseLocked
            }
        });

    public static OperationResult<T> CapabilityUnavailable<T>(string code, string message, string capability, string storeId, string? target = null) =>
        OperationResults.CapabilityUnavailable<T>(new BaseError
        {
            Code = code,
            Message = message,
            Category = ErrorCategory.Capability,
            Target = target,
            Capability = new CapabilityErrorInfo { Capability = capability, Reason = CapabilityFailureReason.Misconfigured, StoreId = storeId }
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
