using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HPD.Base;

internal static class HPDBaseFilesTelemetry
{
    private static readonly Counter<long> Operations = HPDBaseFilesObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.FilesOperations,
        unit: "{operation}",
        description: "Counts HPD.BASE Files service operations.");

    private static readonly Histogram<double> OperationDuration = HPDBaseFilesObservability.Meter.CreateHistogram<double>(
        HPDBaseTelemetryInstruments.FilesOperationDuration,
        unit: "s",
        description: "Records HPD.BASE Files service operation duration.");

    private static readonly Histogram<long> UploadBytes = HPDBaseFilesObservability.Meter.CreateHistogram<long>(
        HPDBaseTelemetryInstruments.FilesUploadBytes,
        unit: "By",
        description: "Records declared or provider-known HPD.BASE Files upload sizes.");

    private static readonly Histogram<long> DownloadBytes = HPDBaseFilesObservability.Meter.CreateHistogram<long>(
        HPDBaseTelemetryInstruments.FilesDownloadBytes,
        unit: "By",
        description: "Records provider-known HPD.BASE Files download sizes.");

    private static readonly Counter<long> PolicyEvaluations = HPDBaseFilesObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.FilesPolicyEvaluations,
        unit: "{operation}",
        description: "Counts HPD.BASE Files policy evaluations.");

    private static readonly Counter<long> ValidationFailures = HPDBaseFilesObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.FilesValidationFailures,
        unit: "{error}",
        description: "Counts HPD.BASE Files validation failures.");

    public static async ValueTask<OperationResult<T>> TraceAsync<T>(
        string spanName,
        string operation,
        FileOperationContext context,
        long? sizeBytes,
        bool? overwriteRequested,
        Func<ValueTask<OperationResult<T>>> invoke)
    {
        using var activity = Start(spanName, operation, context, sizeBytes, overwriteRequested);
        var startedAt = Stopwatch.GetTimestamp();
        var result = await invoke().ConfigureAwait(false);
        Finish(activity, result.Status, result.Error, operation, startedAt);
        RecordBytes(operation, sizeBytes, result);
        return result;
    }

    public static async ValueTask<OperationResult> TraceAsync(
        string spanName,
        string operation,
        FileOperationContext context,
        long? sizeBytes,
        bool? overwriteRequested,
        Func<ValueTask<OperationResult>> invoke)
    {
        using var activity = Start(spanName, operation, context, sizeBytes, overwriteRequested);
        var startedAt = Stopwatch.GetTimestamp();
        var result = await invoke().ConfigureAwait(false);
        Finish(activity, result.Status, result.Error, operation, startedAt);
        RecordBytes(operation, sizeBytes, result.Status, result.Error);
        return result;
    }

    private static Activity? Start(string spanName, string operation, FileOperationContext context, long? sizeBytes, bool? overwriteRequested)
    {
        var activity = HPDBaseFilesObservability.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleFiles);
        activity.SetTag(HPDBaseTelemetryTags.OperationKind, operation);
        activity.SetTag(HPDBaseTelemetryTags.CorrelationIdPresent, !string.IsNullOrWhiteSpace(context.CorrelationId));
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
            SetErrorTags(activity, error);
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
        if (status == OperationStatus.ValidationFailed)
        {
            ValidationFailures.Add(1, tags);
        }
    }

    public static void RecordPolicyEvaluation(string operation, OperationStatus status, BaseError? error) =>
        PolicyEvaluations.Add(1, Tags(operation, status, error));

    private static void RecordBytes<T>(string operation, long? requestedSizeBytes, OperationResult<T> result)
    {
        var bytes = operation switch
        {
            FileOperationValues.Upload => requestedSizeBytes,
            FileOperationValues.DownloadOpen when result.Value is FileObjectDownloadResult download => download.ContentLength,
            _ => null
        };

        if (bytes is not null && bytes >= 0)
        {
            var tags = Tags(operation, result.Status, result.Error);
            if (operation == FileOperationValues.Upload)
            {
                UploadBytes.Record(bytes.Value, tags);
            }
            else if (operation == FileOperationValues.DownloadOpen)
            {
                DownloadBytes.Record(bytes.Value, tags);
            }
        }
    }

    private static void RecordBytes(string operation, long? requestedSizeBytes, OperationStatus status, BaseError? error)
    {
        if (operation == FileOperationValues.Upload && requestedSizeBytes is >= 0)
        {
            UploadBytes.Record(requestedSizeBytes.Value, Tags(operation, status, error));
        }
    }

    private static void SetErrorTags(Activity activity, BaseError? error)
    {
        if (error is null)
        {
            return;
        }

        activity.SetTag(HPDBaseTelemetryTags.ErrorCode, error.Code);
        activity.SetTag(HPDBaseTelemetryTags.ErrorCategory, CategoryValue(error.Category));
    }

    private static TagList Tags(string operation, OperationStatus status, BaseError? error)
    {
        var tags = new TagList
        {
            { HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleFiles },
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

internal static class FileOperationValues
{
    public const string Upload = "upload";
    public const string DownloadOpen = "downloadOpen";
    public const string MetadataGet = "metadataGet";
    public const string Delete = "delete";
    public const string List = "list";
}
