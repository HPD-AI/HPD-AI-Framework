using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base.Observability;
using HPD.Base.Results;
using HPD.Base.Runtime.Results;

namespace HPD.Base.Runtime.Observability;

internal static class HPDBaseRuntimeTelemetry
{
    private static readonly Counter<long> Operations = HPDBaseRuntimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RuntimeOperations,
        unit: "{operation}",
        description: "Counts HPD.BASE runtime operations.");

    private static readonly Histogram<double> OperationDuration = HPDBaseRuntimeObservability.Meter.CreateHistogram<double>(
        HPDBaseTelemetryInstruments.RuntimeOperationDuration,
        unit: "s",
        description: "Records HPD.BASE runtime operation duration.");

    private static readonly Counter<long> Failures = HPDBaseRuntimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RuntimeFailures,
        unit: "{error}",
        description: "Counts HPD.BASE runtime failures.");

    private static readonly Counter<long> StoreInvocations = HPDBaseRuntimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RuntimeStoreInvocations,
        unit: "{operation}",
        description: "Counts HPD.BASE runtime store invocations.");

    private static readonly Histogram<double> StoreDuration = HPDBaseRuntimeObservability.Meter.CreateHistogram<double>(
        HPDBaseTelemetryInstruments.RuntimeStoreDuration,
        unit: "s",
        description: "Records HPD.BASE runtime store invocation duration.");

    private static readonly Counter<long> PolicyEvaluations = HPDBaseRuntimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RuntimePolicyEvaluations,
        unit: "{operation}",
        description: "Counts HPD.BASE runtime policy evaluations.");

    private static readonly Histogram<double> PolicyDuration = HPDBaseRuntimeObservability.Meter.CreateHistogram<double>(
        HPDBaseTelemetryInstruments.RuntimePolicyDuration,
        unit: "s",
        description: "Records HPD.BASE runtime policy evaluation duration.");

    private static readonly Counter<long> ValidationFailures = HPDBaseRuntimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RuntimeValidationFailures,
        unit: "{error}",
        description: "Counts HPD.BASE runtime validation failures.");

    private static readonly Counter<long> EventDispatches = HPDBaseRuntimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RuntimeEventsDispatched,
        unit: "{event}",
        description: "Counts HPD.BASE runtime event dispatch attempts.");

    private static readonly Histogram<double> EventDispatchDuration = HPDBaseRuntimeObservability.Meter.CreateHistogram<double>(
        HPDBaseTelemetryInstruments.RuntimeEventsDispatchDuration,
        unit: "s",
        description: "Records HPD.BASE runtime event dispatch duration.");

    private static readonly Counter<long> HealthReads = HPDBaseRuntimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RuntimeHealthReads,
        unit: "{operation}",
        description: "Counts HPD.BASE runtime health reads.");

    private static readonly Counter<long> DiagnosticReads = HPDBaseRuntimeObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.RuntimeDiagnosticsReads,
        unit: "{operation}",
        description: "Counts HPD.BASE runtime diagnostic reads.");

    public static Activity? StartRuntimeOperation(string spanName, BaseOperationKind operation, string collectionId, OperationContext context)
    {
        var activity = HPDBaseRuntimeObservability.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        if (activity is not null)
        {
            SetCommonTags(activity, operation, collectionId, context);
        }

        return activity;
    }

    public static Activity? StartStoreInvocation(OperationContext context)
    {
        var activity = HPDBaseRuntimeObservability.ActivitySource.StartActivity(HPDBaseTelemetrySpans.RuntimeStoreInvoke, ActivityKind.Internal);
        if (activity is not null)
        {
            SetCommonTags(activity, context.Operation, context.CollectionId, context);
        }

        return activity;
    }

    public static Activity? StartPolicyEvaluation(OperationContext context, string resourceKind)
    {
        var activity = HPDBaseRuntimeObservability.ActivitySource.StartActivity(HPDBaseTelemetrySpans.RuntimePolicyEvaluate, ActivityKind.Internal);
        if (activity is not null)
        {
            SetCommonTags(activity, context.Operation, context.CollectionId, context);
            activity.SetTag(HPDBaseTelemetryTags.PolicyResourceKind, resourceKind);
        }

        return activity;
    }

    public static Activity? StartEventDispatch(OperationContext context, string eventType)
    {
        var activity = HPDBaseRuntimeObservability.ActivitySource.StartActivity(HPDBaseTelemetrySpans.RuntimeEventsDispatch, ActivityKind.Internal);
        if (activity is not null)
        {
            SetCommonTags(activity, context.Operation, context.CollectionId, context);
            activity.SetTag(HPDBaseTelemetryTags.EventType, eventType);
        }

        return activity;
    }

    public static async ValueTask<OperationResult<T>> TraceRuntimeReadAsync<T>(
        string spanName,
        BaseOperationKind operation,
        string? collectionId,
        VisibilityLevel view,
        bool correlationIdPresent,
        bool countAsHealthRead,
        bool countAsDiagnosticRead,
        Func<ValueTask<OperationResult<T>>> invoke)
    {
        using var activity = HPDBaseRuntimeObservability.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleRuntime);
            activity.SetTag(HPDBaseTelemetryTags.OperationKind, ToTelemetryValue(operation));
            activity.SetTag(HPDBaseTelemetryTags.VisibilityLevel, ToTelemetryValue(view));
            activity.SetTag(HPDBaseTelemetryTags.CorrelationIdPresent, correlationIdPresent);
            if (!string.IsNullOrWhiteSpace(collectionId))
            {
                activity.SetTag(HPDBaseTelemetryTags.CollectionId, collectionId);
            }
        }

        var startedAt = Stopwatch.GetTimestamp();
        var result = await invoke().ConfigureAwait(false);
        SetResultTags(activity, result.Status, result.Error);
        var tags = Tags(operation, collectionId, result.Status, result.Error);
        tags.Add(HPDBaseTelemetryTags.VisibilityLevel, ToTelemetryValue(view));
        Operations.Add(1, tags);
        OperationDuration.Record(ElapsedSeconds(startedAt), tags);
        if (!result.Status.IsSuccess())
        {
            Failures.Add(1, tags);
            if (result.Status == OperationStatus.ValidationFailed)
            {
                ValidationFailures.Add(1, tags);
            }
        }

        if (countAsHealthRead)
        {
            HealthReads.Add(1, tags);
        }

        if (countAsDiagnosticRead)
        {
            DiagnosticReads.Add(1, tags);
        }

        return result;
    }

    public static async ValueTask<BaseRuntimeValidationResult> TraceRuntimeValidationAsync(
        Func<ValueTask<BaseRuntimeValidationResult>> invoke)
    {
        using var activity = HPDBaseRuntimeObservability.ActivitySource.StartActivity(HPDBaseTelemetrySpans.RuntimeValidate, ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleRuntime);
            activity.SetTag(HPDBaseTelemetryTags.OperationKind, "validate");
        }

        var startedAt = Stopwatch.GetTimestamp();
        var result = await invoke().ConfigureAwait(false);
        var status = result.Succeeded ? OperationStatus.Ok : OperationStatus.ValidationFailed;
        var error = result.Succeeded
            ? null
            : new BaseError
            {
                Code = "base.runtime.validation.failed",
                Message = "Runtime validation failed.",
                Category = ErrorCategory.Validation
            };
        SetResultTags(activity, status, error);
        var tags = Tags(BaseOperationKind.AdminInspect, null, status, error);
        Operations.Add(1, tags);
        OperationDuration.Record(ElapsedSeconds(startedAt), tags);
        if (!result.Succeeded)
        {
            Failures.Add(1, tags);
            ValidationFailures.Add(1, tags);
        }

        return result;
    }

    public static OperationResult<T> FinishRuntimeOperation<T>(
        Activity? activity,
        OperationResult<T> result,
        BaseOperationKind operation,
        string collectionId,
        OperationContext context,
        long startedAt)
    {
        var enriched = Enrich(result, context, activity);
        SetResultTags(activity, enriched.Status, enriched.Error);
        RecordRuntimeMetrics(enriched.Status, enriched.Error, operation, collectionId, elapsedSeconds: ElapsedSeconds(startedAt));
        return enriched;
    }

    public static OperationResult<T> FinishStoreInvocation<T>(
        Activity? activity,
        OperationResult<T> result,
        OperationContext context,
        long startedAt)
    {
        SetResultTags(activity, result.Status, result.Error);
        StoreInvocations.Add(1, Tags(context.Operation, context.CollectionId, result.Status, result.Error));
        StoreDuration.Record(ElapsedSeconds(startedAt), Tags(context.Operation, context.CollectionId, result.Status, result.Error));
        return result;
    }

    public static OperationResult<T> FinishPolicyEvaluation<T>(
        Activity? activity,
        OperationResult<T> result,
        OperationContext context,
        long startedAt)
    {
        SetResultTags(activity, result.Status, result.Error);
        PolicyEvaluations.Add(1, Tags(context.Operation, context.CollectionId, result.Status, result.Error));
        PolicyDuration.Record(ElapsedSeconds(startedAt), Tags(context.Operation, context.CollectionId, result.Status, result.Error));
        return result;
    }

    public static OperationResult<T> FinishEventDispatch<T>(
        Activity? activity,
        OperationResult<T> result,
        OperationContext context,
        long startedAt)
    {
        SetResultTags(activity, result.Status, result.Error);
        EventDispatches.Add(1, Tags(context.Operation, context.CollectionId, result.Status, result.Error));
        EventDispatchDuration.Record(ElapsedSeconds(startedAt), Tags(context.Operation, context.CollectionId, result.Status, result.Error));
        return result;
    }

    private static void RecordRuntimeMetrics(OperationStatus status, BaseError? error, BaseOperationKind operation, string collectionId, double elapsedSeconds)
    {
        var tags = Tags(operation, collectionId, status, error);
        Operations.Add(1, tags);
        OperationDuration.Record(elapsedSeconds, tags);
        if (!status.IsSuccess())
        {
            Failures.Add(1, tags);
        }
    }

    private static void SetCommonTags(Activity activity, BaseOperationKind operation, string? collectionId, OperationContext context)
    {
        activity.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleRuntime);
        activity.SetTag(HPDBaseTelemetryTags.OperationKind, ToTelemetryValue(operation));
        if (!string.IsNullOrWhiteSpace(collectionId))
        {
            activity.SetTag(HPDBaseTelemetryTags.CollectionId, collectionId);
        }

        activity.SetTag(HPDBaseTelemetryTags.CorrelationIdPresent, !string.IsNullOrWhiteSpace(context.CorrelationId));
    }

    private static void SetResultTags(Activity? activity, OperationStatus status, BaseError? error)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(HPDBaseTelemetryTags.ResultStatus, ToTelemetryValue(status));
        if (error is not null)
        {
            activity.SetTag(HPDBaseTelemetryTags.ErrorCode, error.Code);
            activity.SetTag(HPDBaseTelemetryTags.ErrorCategory, ToTelemetryValue(error.Category));
            if (error.Store is not null)
            {
                activity.SetTag(HPDBaseTelemetryTags.ErrorRetryable, error.Store.Retryable);
            }
        }

        if (status is OperationStatus.StoreError)
        {
            activity.SetStatus(ActivityStatusCode.Error, error?.Code);
        }
        else if (status.IsSuccess())
        {
            activity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    private static OperationResult<T> Enrich<T>(OperationResult<T> result, OperationContext context, Activity? activity)
    {
        var traceId = activity?.TraceId.ToString();
        if (string.IsNullOrWhiteSpace(traceId) && Activity.Current is { } current)
        {
            traceId = current.TraceId.ToString();
        }

        var diagnostics = result.Diagnostics;
        if (!string.IsNullOrWhiteSpace(traceId) || !string.IsNullOrWhiteSpace(context.CorrelationId))
        {
            diagnostics ??= new OperationDiagnostics();
            diagnostics = diagnostics with
            {
                TraceId = diagnostics.TraceId ?? traceId,
                CorrelationId = diagnostics.CorrelationId ?? context.CorrelationId
            };
        }

        var error = result.Error;
        if (error is not null)
        {
            error = error with
            {
                TraceId = error.TraceId ?? traceId,
                CorrelationId = error.CorrelationId ?? context.CorrelationId
            };
        }

        return result with { Diagnostics = diagnostics, Error = error };
    }

    private static TagList Tags(BaseOperationKind operation, string? collectionId, OperationStatus status, BaseError? error)
    {
        var tags = new TagList
        {
            { HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleRuntime },
            { HPDBaseTelemetryTags.OperationKind, ToTelemetryValue(operation) },
            { HPDBaseTelemetryTags.ResultStatus, ToTelemetryValue(status) }
        };
        if (!string.IsNullOrWhiteSpace(collectionId))
        {
            tags.Add(HPDBaseTelemetryTags.CollectionId, collectionId);
        }

        if (error is not null)
        {
            tags.Add(HPDBaseTelemetryTags.ErrorCode, error.Code);
            tags.Add(HPDBaseTelemetryTags.ErrorCategory, ToTelemetryValue(error.Category));
        }

        return tags;
    }

    private static double ElapsedSeconds(long startedAt) =>
        (double)(Stopwatch.GetTimestamp() - startedAt) / Stopwatch.Frequency;

    private static string ToTelemetryValue(BaseOperationKind value) => value switch
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

    private static string ToTelemetryValue(OperationStatus value) => value switch
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

    private static string ToTelemetryValue(VisibilityLevel value) => value switch
    {
        VisibilityLevel.Public => "public",
        VisibilityLevel.Authenticated => "authenticated",
        VisibilityLevel.Admin => "admin",
        VisibilityLevel.Internal => "internal",
        _ => "unknown"
    };

    private static string ToTelemetryValue(ErrorCategory value) => value switch
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
