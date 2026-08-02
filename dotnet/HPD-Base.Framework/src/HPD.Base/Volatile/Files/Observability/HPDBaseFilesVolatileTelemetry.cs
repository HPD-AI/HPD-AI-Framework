using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HPD.Base;

internal static class HPDBaseFilesVolatileTelemetry
{
    private static readonly Counter<long> Operations = HPDBaseFilesVolatileObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.FilesProviderOperations,
        unit: "{operation}",
        description: "Counts HPD.BASE Files Volatile provider operations.");

    private static readonly Histogram<double> OperationDuration = HPDBaseFilesVolatileObservability.Meter.CreateHistogram<double>(
        HPDBaseTelemetryInstruments.FilesProviderDuration,
        unit: "s",
        description: "Records HPD.BASE Files Volatile provider operation duration.");

    private static readonly Histogram<long> UploadBytes = HPDBaseFilesVolatileObservability.Meter.CreateHistogram<long>(
        HPDBaseTelemetryInstruments.FilesUploadBytes,
        unit: "By",
        description: "Records HPD.BASE Files Volatile upload sizes.");

    private static readonly Histogram<long> DownloadBytes = HPDBaseFilesVolatileObservability.Meter.CreateHistogram<long>(
        HPDBaseTelemetryInstruments.FilesDownloadBytes,
        unit: "By",
        description: "Records HPD.BASE Files Volatile download sizes.");

    /// <summary>Executes the trace async operation.</summary>
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

    /// <summary>Executes the trace async operation.</summary>
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
        var activity = HPDBaseFilesVolatileObservability.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleFiles);
        activity.SetTag(HPDBaseTelemetryTags.ProviderKind, HPDBaseTelemetryValues.ProviderFilesVolatile);
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
            { HPDBaseTelemetryTags.ProviderKind, HPDBaseTelemetryValues.ProviderFilesVolatile },
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
    /// <summary>Provides the upload value.</summary>
    public const string Upload = "upload";
    /// <summary>Provides the download open value.</summary>
    public const string DownloadOpen = "downloadOpen";
    /// <summary>Provides the metadata get value.</summary>
    public const string MetadataGet = "metadataGet";
    /// <summary>Provides the delete value.</summary>
    public const string Delete = "delete";
    /// <summary>Provides the list value.</summary>
    public const string List = "list";
}
