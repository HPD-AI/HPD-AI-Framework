using Microsoft.Extensions.Logging;

namespace HPD.Base.Sqlite;

internal static partial class HPDBaseSqliteLog
{
    /// <summary>Executes the database open failed operation.</summary>
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

    /// <summary>Executes the database busy operation.</summary>
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

    /// <summary>Executes the database locked operation.</summary>
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

    /// <summary>Executes the schema missing operation.</summary>
    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Error,
        EventName = "SchemaMissing",
        Message = "The required SQLite schema is missing ({ErrorCode}).")]
    public static partial void SchemaMissing(
        ILogger logger,
        string errorCode);

    /// <summary>Executes the schema diagnostic warning operation.</summary>
    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Warning,
        EventName = "SchemaDiagnosticWarning",
        Message = "SQLite schema diagnostics reported a degraded state ({ErrorCode}).")]
    public static partial void SchemaDiagnosticWarning(
        ILogger logger,
        string errorCode);

    /// <summary>Executes the query plan rejected operation.</summary>
    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Debug,
        EventName = "QueryPlanRejected",
        Message = "SQLite rejected an unsafe or unsupported query plan ({PlanStatus}, {ErrorCode}).")]
    public static partial void QueryPlanRejected(
        ILogger logger,
        string planStatus,
        string errorCode);

    /// <summary>Executes the provider operation failed operation.</summary>
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

    /// <summary>Reports bounded administration work retained after the caller's wait ended.</summary>
    [LoggerMessage(
        EventId = 3009,
        Level = LogLevel.Warning,
        EventName = "AdministrationQuarantined",
        Message = "SQLite administration work remains in bounded quarantine for {OperationKind}.")]
    public static partial void AdministrationQuarantined(
        ILogger logger,
        string operationKind);

    /// <summary>Executes the operation kind operation.</summary>
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
