using Microsoft.Extensions.Logging;

namespace HPD.Base;

internal static partial class HPDBaseRuntimeLog
{
    /// <summary>Executes the store result malformed operation.</summary>
    [LoggerMessage(
        EventId = 1001,
        EventName = "StoreResultMalformed",
        Level = LogLevel.Error,
        Message = "The record store returned a malformed successful result for {OperationKind}.")]
    public static partial void StoreResultMalformed(ILogger logger, string operationKind);

    /// <summary>Executes the store unavailable operation.</summary>
    [LoggerMessage(
        EventId = 1002,
        EventName = "StoreUnavailable",
        Level = LogLevel.Warning,
        Message = "The required record store is unavailable for {OperationKind} ({CapabilityReason}).")]
    public static partial void StoreUnavailable(ILogger logger, string operationKind, string capabilityReason);

    /// <summary>Executes the mutation event dispatch failed operation.</summary>
    [LoggerMessage(
        EventId = 1004,
        EventName = "MutationEventDispatchFailed",
        Level = LogLevel.Warning,
        Message = "Mutation event dispatch degraded for {OperationKind} with {ErrorCategory} ({ErrorCode}).")]
    public static partial void MutationEventDispatchFailed(
        ILogger logger,
        string operationKind,
        string errorCategory,
        string errorCode);

    /// <summary>Executes the health contributor failed operation.</summary>
    [LoggerMessage(
        EventId = 1005,
        EventName = "HealthContributorFailed",
        Level = LogLevel.Warning,
        Message = "A BASE health contributor failed.")]
    public static partial void HealthContributorFailed(ILogger logger);

    /// <summary>Executes the diagnostic contributor failed operation.</summary>
    [LoggerMessage(
        EventId = 1006,
        EventName = "DiagnosticContributorFailed",
        Level = LogLevel.Warning,
        Message = "A BASE diagnostic contributor failed.")]
    public static partial void DiagnosticContributorFailed(ILogger logger);

    /// <summary>Executes the store failure malformed operation.</summary>
    [LoggerMessage(
        EventId = 1008,
        EventName = "StoreFailureMalformed",
        Level = LogLevel.Error,
        Message = "The record store returned a failed result without required error details for {OperationKind}.")]
    public static partial void StoreFailureMalformed(ILogger logger, string operationKind);

    /// <summary>Executes the store dependency unavailable operation.</summary>
    [LoggerMessage(
        EventId = 1009,
        EventName = "StoreDependencyUnavailable",
        Level = LogLevel.Warning,
        Message = "The record store dependency is temporarily unavailable for {OperationKind} ({ErrorCode}).")]
    public static partial void StoreDependencyUnavailable(ILogger logger, string operationKind, string errorCode);

    /// <summary>Executes the store dependency failed operation.</summary>
    [LoggerMessage(
        EventId = 1010,
        EventName = "StoreDependencyFailed",
        Level = LogLevel.Error,
        Message = "The record store dependency failed for {OperationKind} ({ErrorCode}).")]
    public static partial void StoreDependencyFailed(ILogger logger, string operationKind, string errorCode);

    /// <summary>Executes the operation kind operation.</summary>
    public static string OperationKind(BaseOperationKind operation) => operation switch
    {
        BaseOperationKind.List => "list",
        BaseOperationKind.Query => "query",
        BaseOperationKind.Get => "get",
        BaseOperationKind.Create => "create",
        BaseOperationKind.Patch => "patch",
        BaseOperationKind.Replace => "replace",
        BaseOperationKind.Upsert => "upsert",
        BaseOperationKind.Delete => "delete",
        BaseOperationKind.Batch => "batch",
        BaseOperationKind.SchemaRead => "schemaRead",
        BaseOperationKind.SchemaWrite => "schemaWrite",
        BaseOperationKind.FileRead => "fileRead",
        BaseOperationKind.FileWrite => "fileWrite",
        BaseOperationKind.RealtimeSubscribe => "realtimeSubscribe",
        BaseOperationKind.AdminInspect => "adminInspect",
        _ => "unknown"
    };
}
