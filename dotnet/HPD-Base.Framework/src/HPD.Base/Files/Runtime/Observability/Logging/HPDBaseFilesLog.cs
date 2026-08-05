using Microsoft.Extensions.Logging;

namespace HPD.Base;

internal static partial class HPDBaseFilesLog
{
    /// <summary>Executes the file provider unavailable operation.</summary>
    [LoggerMessage(
        EventId = 4000,
        EventName = "FileProviderUnavailable",
        Level = LogLevel.Warning,
        Message = "The required file provider is unavailable for {OperationKind} ({CapabilityReason}).")]
    public static partial void FileProviderUnavailable(
        ILogger logger,
        string operationKind,
        string capabilityReason);

    /// <summary>Executes the file provider operation failed operation.</summary>
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

    /// <summary>Executes the file policy denied operation.</summary>
    [LoggerMessage(
        EventId = 4002,
        EventName = "FilePolicyDenied",
        Level = LogLevel.Debug,
        Message = "File policy denied {OperationKind} ({PolicyReasonCode}).")]
    public static partial void FilePolicyDenied(
        ILogger logger,
        string operationKind,
        string policyReasonCode);

    /// <summary>Executes the file validation rejected operation.</summary>
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
