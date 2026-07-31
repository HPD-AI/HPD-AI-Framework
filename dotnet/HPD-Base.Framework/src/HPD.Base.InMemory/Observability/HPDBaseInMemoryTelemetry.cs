using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base;

namespace HPD.Base.InMemory;

internal static class HPDBaseInMemoryTelemetry
{
    private static readonly Counter<long> Operations = HPDBaseInMemoryObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.StoreOperations,
        unit: "{operation}",
        description: "Counts HPD.BASE InMemory store operations.");

    private static readonly Histogram<double> OperationDuration = HPDBaseInMemoryObservability.Meter.CreateHistogram<double>(
        HPDBaseTelemetryInstruments.StoreOperationDuration,
        unit: "s",
        description: "Records HPD.BASE InMemory store operation duration.");

    public static async ValueTask<OperationResult<T>> TraceAsync<T>(
        string spanName,
        BaseOperationKind operation,
        string storeId,
        string collectionId,
        Func<ValueTask<OperationResult<T>>> invoke)
    {
        using var activity = HPDBaseInMemoryObservability.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        SetStartTags(activity, operation, storeId, collectionId);
        var startedAt = Stopwatch.GetTimestamp();
        var result = await invoke().ConfigureAwait(false);
        Finish(activity, result.Status, result.Error, operation, storeId, collectionId, startedAt);
        return result;
    }

    private static void SetStartTags(Activity? activity, BaseOperationKind operation, string storeId, string collectionId)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleInMemory);
        activity.SetTag(HPDBaseTelemetryTags.ProviderKind, HPDBaseTelemetryValues.ProviderInMemory);
        activity.SetTag(HPDBaseTelemetryTags.StoreId, storeId);
        activity.SetTag(HPDBaseTelemetryTags.CollectionId, collectionId);
        activity.SetTag(HPDBaseTelemetryTags.OperationKind, OperationValue(operation));
    }

    private static void Finish(Activity? activity, OperationStatus status, BaseError? error, BaseOperationKind operation, string storeId, string collectionId, long startedAt)
    {
        if (activity is not null)
        {
            activity.SetTag(HPDBaseTelemetryTags.ResultStatus, StatusValue(status));
            if (error is not null)
            {
                activity.SetTag(HPDBaseTelemetryTags.ErrorCode, error.Code);
                activity.SetTag(HPDBaseTelemetryTags.ErrorCategory, CategoryValue(error.Category));
                activity.SetTag(HPDBaseTelemetryTags.ErrorRetryable, error.Store?.Retryable);
            }

            if (status == OperationStatus.StoreError)
            {
                activity.SetStatus(ActivityStatusCode.Error, error?.Code);
            }
            else if (status.IsSuccess())
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
        }

        var tags = new TagList
        {
            { HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleInMemory },
            { HPDBaseTelemetryTags.ProviderKind, HPDBaseTelemetryValues.ProviderInMemory },
            { HPDBaseTelemetryTags.StoreId, storeId },
            { HPDBaseTelemetryTags.CollectionId, collectionId },
            { HPDBaseTelemetryTags.OperationKind, OperationValue(operation) },
            { HPDBaseTelemetryTags.ResultStatus, StatusValue(status) }
        };
        if (error is not null)
        {
            tags.Add(HPDBaseTelemetryTags.ErrorCode, error.Code);
            tags.Add(HPDBaseTelemetryTags.ErrorCategory, CategoryValue(error.Category));
        }

        Operations.Add(1, tags);
        OperationDuration.Record((double)(Stopwatch.GetTimestamp() - startedAt) / Stopwatch.Frequency, tags);
    }

    private static string OperationValue(BaseOperationKind value) => value switch
    {
        BaseOperationKind.List => "list",
        BaseOperationKind.Get => "get",
        BaseOperationKind.Create => "create",
        BaseOperationKind.Patch => "patch",
        BaseOperationKind.Replace => "replace",
        BaseOperationKind.Delete => "delete",
        RealtimeSubscribeStoreOperation => "streamOpen",
        _ => "unknown"
    };

    private const BaseOperationKind RealtimeSubscribeStoreOperation = BaseOperationKind.RealtimeSubscribe;

    private static string StatusValue(OperationStatus value) => value switch
    {
        OperationStatus.Ok => "ok",
        OperationStatus.Created => "created",
        OperationStatus.Updated => "updated",
        OperationStatus.Deleted => "deleted",
        OperationStatus.NoContent => "noContent",
        OperationStatus.NotFound => "notFound",
        OperationStatus.Conflict => "conflict",
        OperationStatus.ValidationFailed => "validationFailed",
        OperationStatus.PolicyDenied => "policyDenied",
        OperationStatus.Unauthorized => "unauthorized",
        OperationStatus.Unsupported => "unsupported",
        OperationStatus.CapabilityUnavailable => "capabilityUnavailable",
        OperationStatus.StoreError => "storeError",
        _ => "unknown"
    };

    private static string CategoryValue(ErrorCategory value) => value switch
    {
        ErrorCategory.None => "none",
        ErrorCategory.Validation => "validation",
        ErrorCategory.Authentication => "authentication",
        ErrorCategory.Authorization => "authorization",
        ErrorCategory.NotFound => "notFound",
        ErrorCategory.Conflict => "conflict",
        ErrorCategory.Unsupported => "unsupported",
        ErrorCategory.Capability => "capability",
        ErrorCategory.Store => "store",
        ErrorCategory.Unexpected => "unexpected",
        _ => "unknown"
    };
}
