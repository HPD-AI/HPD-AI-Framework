using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base.Files.Objects;
using HPD.Base.Observability;
using HPD.Base.Results;
using HPD.Base.Runtime.Results;

namespace HPD.Base.Files.InMemory.Observability;

internal static class HPDBaseFilesInMemoryTelemetry
{
    private static readonly Counter<long> Operations = HPDBaseFilesInMemoryObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.FilesProviderOperations,
        unit: "{operation}",
        description: "Counts HPD.BASE Files InMemory provider operations.");

    private static readonly Histogram<double> OperationDuration = HPDBaseFilesInMemoryObservability.Meter.CreateHistogram<double>(
        HPDBaseTelemetryInstruments.FilesProviderDuration,
        unit: "s",
        description: "Records HPD.BASE Files InMemory provider operation duration.");

    private static readonly Histogram<long> UploadBytes = HPDBaseFilesInMemoryObservability.Meter.CreateHistogram<long>(
        HPDBaseTelemetryInstruments.FilesUploadBytes,
        unit: "By",
        description: "Records HPD.BASE Files InMemory upload sizes.");

    private static readonly Histogram<long> DownloadBytes = HPDBaseFilesInMemoryObservability.Meter.CreateHistogram<long>(
        HPDBaseTelemetryInstruments.FilesDownloadBytes,
        unit: "By",
        description: "Records HPD.BASE Files InMemory download sizes.");

    public static async ValueTask<OperationResult<T>> TraceAsync<T>(
        string spanName,
        string operation,
        long? sizeBytes,
        bool? overwriteRequested,
        Func<ValueTask<OperationResult<T>>> invoke)
    {
        using var activity = Start(spanName, operation, sizeBytes, overwriteRequested);
        var startedAt = Stopwatch.GetTimestamp();
        var result = await invoke().ConfigureAwait(false);
        Finish(activity, result.Status, result.Error, operation, startedAt);
        RecordBytes(operation, sizeBytes, result);
        return result;
    }

    public static async ValueTask<OperationResult> TraceAsync(
        string spanName,
        string operation,
        long? sizeBytes,
        bool? overwriteRequested,
        Func<ValueTask<OperationResult>> invoke)
    {
        using var activity = Start(spanName, operation, sizeBytes, overwriteRequested);
        var startedAt = Stopwatch.GetTimestamp();
        var result = await invoke().ConfigureAwait(false);
        Finish(activity, result.Status, result.Error, operation, startedAt);
        if (operation == ProviderOperationValues.Upload && sizeBytes is >= 0)
        {
            UploadBytes.Record(sizeBytes.Value, Tags(operation, result.Status, result.Error));
        }

        return result;
    }

    private static Activity? Start(string spanName, string operation, long? sizeBytes, bool? overwriteRequested)
    {
        var activity = HPDBaseFilesInMemoryObservability.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleFiles);
        activity.SetTag(HPDBaseTelemetryTags.ProviderKind, HPDBaseTelemetryValues.ProviderFilesInMemory);
        activity.SetTag(HPDBaseTelemetryTags.OperationKind, operation);
        if (sizeBytes is not null)
        {
            activity.SetTag(HPDBaseTelemetryTags.FileSizeBucket, HPDBaseTelemetryBuckets.FileSize(sizeBytes));
        }

        if (overwriteRequested is not null)
        {
            activity.SetTag(HPDBaseTelemetryTags.FileOverwriteRequested, overwriteRequested);
        }

        return activity;
    }

    private static void Finish(Activity? activity, OperationStatus status, BaseError? error, string operation, long startedAt)
    {
        if (activity is not null)
        {
            activity.SetTag(HPDBaseTelemetryTags.ResultStatus, StatusValue(status));
            if (error is not null)
            {
                activity.SetTag(HPDBaseTelemetryTags.ErrorCode, error.Code);
                activity.SetTag(HPDBaseTelemetryTags.ErrorCategory, CategoryValue(error.Category));
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

        var tags = Tags(operation, status, error);
        Operations.Add(1, tags);
        OperationDuration.Record((double)(Stopwatch.GetTimestamp() - startedAt) / Stopwatch.Frequency, tags);
    }

    private static void RecordBytes<T>(string operation, long? sizeBytes, OperationResult<T> result)
    {
        if (operation == ProviderOperationValues.Upload && sizeBytes is >= 0)
        {
            UploadBytes.Record(sizeBytes.Value, Tags(operation, result.Status, result.Error));
        }
        else if (operation == ProviderOperationValues.DownloadOpen && result.Value is FileObjectDownloadResult { ContentLength: >= 0 } download)
        {
            DownloadBytes.Record(download.ContentLength.Value, Tags(operation, result.Status, result.Error));
        }
    }

    private static TagList Tags(string operation, OperationStatus status, BaseError? error)
    {
        var tags = new TagList
        {
            { HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleFiles },
            { HPDBaseTelemetryTags.ProviderKind, HPDBaseTelemetryValues.ProviderFilesInMemory },
            { HPDBaseTelemetryTags.OperationKind, operation },
            { HPDBaseTelemetryTags.ResultStatus, StatusValue(status) }
        };
        if (error is not null)
        {
            tags.Add(HPDBaseTelemetryTags.ErrorCode, error.Code);
            tags.Add(HPDBaseTelemetryTags.ErrorCategory, CategoryValue(error.Category));
        }

        return tags;
    }

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

internal static class ProviderOperationValues
{
    public const string Upload = "upload";
    public const string DownloadOpen = "downloadOpen";
    public const string MetadataGet = "metadataGet";
    public const string Delete = "delete";
    public const string List = "list";
}
