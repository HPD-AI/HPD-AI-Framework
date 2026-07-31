using Microsoft.Extensions.Logging;

namespace HPD.Base.Sqlite;

internal static partial class HPDBaseSqliteLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Error,
        EventName = "DatabaseOpenFailed",
        Message = "The SQLite database could not be opened ({ErrorCode}, native {NativeErrorCode}/{NativeExtendedErrorCode}).")]
    public static partial void DatabaseOpenFailed(
        ILogger logger,
        string errorCode,
        int nativeErrorCode,
        int nativeExtendedErrorCode);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Warning,
        EventName = "DatabaseBusy",
        Message = "The SQLite database is busy; retryable: {Retryable} (native {NativeErrorCode}/{NativeExtendedErrorCode}).")]
    public static partial void DatabaseBusy(
        ILogger logger,
        bool retryable,
        int nativeErrorCode,
        int nativeExtendedErrorCode);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Warning,
        EventName = "DatabaseLocked",
        Message = "The SQLite database is locked; retryable: {Retryable} (native {NativeErrorCode}/{NativeExtendedErrorCode}).")]
    public static partial void DatabaseLocked(
        ILogger logger,
        bool retryable,
        int nativeErrorCode,
        int nativeExtendedErrorCode);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Error,
        EventName = "SchemaMissing",
        Message = "The required SQLite schema is missing ({ErrorCode}).")]
    public static partial void SchemaMissing(
        ILogger logger,
        string errorCode);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Warning,
        EventName = "SchemaDiagnosticWarning",
        Message = "SQLite schema diagnostics reported a degraded state ({ErrorCode}).")]
    public static partial void SchemaDiagnosticWarning(
        ILogger logger,
        string errorCode);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Debug,
        EventName = "QueryPlanRejected",
        Message = "SQLite rejected an unsafe or unsupported query plan ({PlanStatus}, {ErrorCode}).")]
    public static partial void QueryPlanRejected(
        ILogger logger,
        string planStatus,
        string errorCode);

    [LoggerMessage(
        EventId = 3008,
        Level = LogLevel.Error,
        EventName = "ProviderOperationFailed",
        Message = "The SQLite provider operation failed for {OperationKind} with {ErrorCategory} ({ErrorCode}, native {NativeErrorCode}/{NativeExtendedErrorCode}).")]
    public static partial void ProviderOperationFailed(
        ILogger logger,
        string operationKind,
        string errorCategory,
        string errorCode,
        int nativeErrorCode,
        int nativeExtendedErrorCode);

    public static string OperationKind(BaseOperationKind operation) => operation switch
    {
        BaseOperationKind.List => "list",
        BaseOperationKind.Get => "get",
        BaseOperationKind.Create => "create",
        BaseOperationKind.Patch => "patch",
        BaseOperationKind.Replace => "replace",
        BaseOperationKind.Delete => "delete",
        _ => "unknown"
    };
}
