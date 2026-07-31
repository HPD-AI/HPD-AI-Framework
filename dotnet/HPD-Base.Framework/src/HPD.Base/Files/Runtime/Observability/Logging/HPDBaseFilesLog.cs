using Microsoft.Extensions.Logging;

namespace HPD.Base.Files.Observability.Logging;

internal static partial class HPDBaseFilesLog
{
    [LoggerMessage(
        EventId = 4000,
        EventName = "FileProviderUnavailable",
        Level = LogLevel.Warning,
        Message = "The required file provider is unavailable for {OperationKind} ({CapabilityReason}).")]
    public static partial void FileProviderUnavailable(
        ILogger logger,
        string operationKind,
        string capabilityReason);

    [LoggerMessage(
        EventId = 4001,
        EventName = "FileProviderOperationFailed",
        Level = LogLevel.Error,
        Message = "The file provider operation failed for {OperationKind} with {ErrorCategory} ({ErrorCode}).")]
    public static partial void FileProviderOperationFailed(
        ILogger logger,
        string operationKind,
        string errorCategory,
        string errorCode);

    [LoggerMessage(
        EventId = 4002,
        EventName = "FilePolicyDenied",
        Level = LogLevel.Debug,
        Message = "File policy denied {OperationKind} ({PolicyReasonCode}).")]
    public static partial void FilePolicyDenied(
        ILogger logger,
        string operationKind,
        string policyReasonCode);

    [LoggerMessage(
        EventId = 4003,
        EventName = "FileValidationRejected",
        Level = LogLevel.Debug,
        Message = "File validation rejected {OperationKind} ({ErrorCode}).")]
    public static partial void FileValidationRejected(
        ILogger logger,
        string operationKind,
        string errorCode);
}
