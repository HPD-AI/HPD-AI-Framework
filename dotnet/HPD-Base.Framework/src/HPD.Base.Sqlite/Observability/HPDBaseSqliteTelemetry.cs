using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using HPD.Base;
using HPD.Base.Sqlite;

namespace HPD.Base.Sqlite;

internal static class HPDBaseSqliteTelemetry
{
    private static readonly ConcurrentDictionary<string, long> LastObservedMissingSchemaParts = new(StringComparer.Ordinal);

    private static readonly Counter<long> StoreOperations = HPDBaseSqliteObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.StoreOperations,
        unit: "{operation}",
        description: "Counts HPD.BASE SQLite store operations.");

    private static readonly Histogram<double> StoreOperationDuration = HPDBaseSqliteObservability.Meter.CreateHistogram<double>(
        HPDBaseTelemetryInstruments.StoreOperationDuration,
        unit: "s",
        description: "Records HPD.BASE SQLite store operation duration.");

    private static readonly Counter<long> ConnectionsOpened = HPDBaseSqliteObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.SqliteConnectionsOpened,
        unit: "{connection}",
        description: "Counts HPD.BASE SQLite connection opens.");

    private static readonly Histogram<double> ConnectionOpenDuration = HPDBaseSqliteObservability.Meter.CreateHistogram<double>(
        HPDBaseTelemetryInstruments.SqliteConnectionOpenDuration,
        unit: "s",
        description: "Records HPD.BASE SQLite connection open duration.");

    private static readonly Counter<long> SchemaInitializations = HPDBaseSqliteObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.SqliteSchemaInitializations,
        unit: "{operation}",
        description: "Counts HPD.BASE SQLite schema initialization or validation operations.");

    private static readonly Counter<long> QueryPlans = HPDBaseSqliteObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.SqliteQueryPlans,
        unit: "{plan}",
        description: "Counts HPD.BASE SQLite query plans.");

    private static readonly ObservableGauge<long> SchemaMissingParts = HPDBaseSqliteObservability.Meter.CreateObservableGauge(
        HPDBaseTelemetryInstruments.SqliteSchemaMissingParts,
        ObserveMissingSchemaParts,
        unit: "{part}",
        description: "Reports the last observed count of missing HPD.BASE SQLite provider-owned schema parts.");

    private static readonly Counter<long> Errors = HPDBaseSqliteObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.SqliteErrors,
        unit: "{error}",
        description: "Counts HPD.BASE SQLite provider errors.");

    private static readonly Counter<long> AdministrationOperations = HPDBaseSqliteObservability.Meter.CreateCounter<long>(
        HPDBaseTelemetryInstruments.SqliteAdministrationOperations,
        unit: "{operation}",
        description: "Counts bounded HPD.BASE SQLite administration operations.");

    private static readonly Histogram<double> AdministrationDuration = HPDBaseSqliteObservability.Meter.CreateHistogram<double>(
        HPDBaseTelemetryInstruments.SqliteAdministrationDuration,
        unit: "s",
        description: "Records HPD.BASE SQLite administration duration.");

    public static async ValueTask<OperationResult<T>> TraceAdministrationAsync<T>(
        string operationKind,
        string storeId,
        Func<ValueTask<OperationResult<T>>> invoke)
    {
        using Activity? activity = StartProviderSpan(HPDBaseTelemetrySpans.SqliteAdministration, storeId);
        activity?.SetTag(HPDBaseTelemetryTags.OperationKind, operationKind);
        long startedAt = Stopwatch.GetTimestamp();
        OperationResult<T> result = await invoke().ConfigureAwait(false);
        activity?.SetTag(HPDBaseTelemetryTags.ResultStatus, StatusValue(result.Status));
        if (activity is not null && result.Error is { } error) SetErrorTags(activity, error);
        activity?.SetStatus(result.Status.IsSuccess() ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        TagList tags = ProviderTags(storeId, StatusValue(result.Status));
        tags.Add(HPDBaseTelemetryTags.OperationKind, operationKind);
        AdministrationOperations.Add(1, tags);
        AdministrationDuration.Record((double)(Stopwatch.GetTimestamp() - startedAt) / Stopwatch.Frequency, tags);
        return result;
    }

    /// <summary>Executes the trace store async operation.</summary>
    public static async ValueTask<OperationResult<T>> TraceStoreAsync<T>(
        string spanName,
        BaseOperationKind operation,
        string storeId,
        string collectionId,
        Func<ValueTask<OperationResult<T>>> invoke)
    {
        using var activity = HPDBaseSqliteObservability.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        SetStoreStartTags(activity, operation, storeId, collectionId);
        var startedAt = Stopwatch.GetTimestamp();
        var result = await invoke().ConfigureAwait(false);
        FinishStore(activity, result.Status, result.Error, operation, storeId, collectionId, startedAt);
        return result;
    }

    /// <summary>Executes the trace connection open async operation.</summary>
    public static async ValueTask<T> TraceConnectionOpenAsync<T>(string storeId, Func<ValueTask<T>> invoke)
    {
        using var activity = StartProviderSpan(HPDBaseTelemetrySpans.SqliteConnectionOpen, storeId);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var value = await invoke().ConfigureAwait(false);
            FinishProvider(activity, storeId, "ok", startedAt);
            return value;
        }
        catch
        {
            FinishProvider(activity, storeId, "error", startedAt);
            throw;
        }
    }

    /// <summary>Executes the trace schema async operation.</summary>
    public static async ValueTask TraceSchemaAsync(string spanName, string storeId, Func<ValueTask> invoke)
    {
        using var activity = StartProviderSpan(spanName, storeId);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await invoke().ConfigureAwait(false);
            SchemaInitializations.Add(1, ProviderTags(storeId, "ok"));
            FinishProvider(activity, storeId, "ok", startedAt);
        }
        catch
        {
            SchemaInitializations.Add(1, ProviderTags(storeId, "error"));
            FinishProvider(activity, storeId, "error", startedAt);
            throw;
        }
    }

    /// <summary>Executes the trace schema async operation.</summary>
    public static async ValueTask<TResult> TraceSchemaAsync<TResult>(string spanName, string storeId, Func<ValueTask<TResult>> invoke)
    {
        using var activity = StartProviderSpan(spanName, storeId);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var result = await invoke().ConfigureAwait(false);
            SchemaInitializations.Add(1, ProviderTags(storeId, "ok"));
            FinishProvider(activity, storeId, "ok", startedAt);
            return result;
        }
        catch
        {
            SchemaInitializations.Add(1, ProviderTags(storeId, "error"));
            FinishProvider(activity, storeId, "error", startedAt);
            throw;
        }
    }

    /// <summary>Executes the record schema missing parts operation.</summary>
    public static void RecordSchemaMissingParts(string storeId, int missingPartCount)
    {
        LastObservedMissingSchemaParts[storeId] = Math.Max(0, missingPartCount);
    }

    /// <summary>Executes the trace query plan operation.</summary>
    public static SqliteQueryPlan TraceQueryPlan(string storeId, string collectionId, RecordQuery query, Func<SqliteQueryPlan> plan)
    {
        using var activity = HPDBaseSqliteObservability.ActivitySource.StartActivity(HPDBaseTelemetrySpans.SqliteQueryPlan, ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleSqlite);
            activity.SetTag(HPDBaseTelemetryTags.ProviderKind, HPDBaseTelemetryValues.ProviderSqlite);
            activity.SetTag(HPDBaseTelemetryTags.StoreId, storeId);
            activity.SetTag(HPDBaseTelemetryTags.CollectionId, collectionId);
            activity.SetTag(HPDBaseTelemetryTags.QueryCountMode, CountModeValue(query.Count));
            activity.SetTag(HPDBaseTelemetryTags.QueryPageSizeBucket, HPDBaseTelemetryBuckets.PageSize(query.Page?.PerPage ?? query.Page?.Limit));
            activity.SetTag(HPDBaseTelemetryTags.QueryHasFilter, query.Filter is not null);
            activity.SetTag(HPDBaseTelemetryTags.QueryHasSort, query.Sort is { Length: > 0 });
            activity.SetTag(HPDBaseTelemetryTags.QueryHasInclude, query.Include is { Length: > 0 });
        }

        var result = plan();
        var status = result.Supported ? "supported" : "unsupported";
        activity?.SetTag(HPDBaseTelemetryTags.RelationalPlanStatus, status);
        QueryPlans.Add(1, ProviderTags(storeId, status));
        activity?.SetStatus(ActivityStatusCode.Ok);
        return result;
    }

    /// <summary>Executes the trace transaction async operation.</summary>
    public static async ValueTask<T> TraceTransactionAsync<T>(string storeId, Func<ValueTask<T>> invoke)
    {
        using var activity = StartProviderSpan(HPDBaseTelemetrySpans.SqliteTransaction, storeId);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var result = await invoke().ConfigureAwait(false);
            FinishProvider(activity, storeId, "ok", startedAt);
            return result;
        }
        catch
        {
            FinishProvider(activity, storeId, "failed", startedAt);
            throw;
        }
    }

    private static Activity? StartProviderSpan(string spanName, string storeId)
    {
        var activity = HPDBaseSqliteObservability.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleSqlite);
            activity.SetTag(HPDBaseTelemetryTags.ProviderKind, HPDBaseTelemetryValues.ProviderSqlite);
            activity.SetTag(HPDBaseTelemetryTags.StoreId, storeId);
        }

        return activity;
    }

    private static void SetStoreStartTags(Activity? activity, BaseOperationKind operation, string storeId, string collectionId)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleSqlite);
        activity.SetTag(HPDBaseTelemetryTags.ProviderKind, HPDBaseTelemetryValues.ProviderSqlite);
        activity.SetTag(HPDBaseTelemetryTags.StoreId, storeId);
        activity.SetTag(HPDBaseTelemetryTags.CollectionId, collectionId);
        activity.SetTag(HPDBaseTelemetryTags.OperationKind, OperationValue(operation));
    }

    private static void FinishStore(Activity? activity, OperationStatus status, BaseError? error, BaseOperationKind operation, string storeId, string collectionId, long startedAt)
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

        var tags = StoreTags(operation, storeId, collectionId, status, error);
        StoreOperations.Add(1, tags);
        StoreOperationDuration.Record((double)(Stopwatch.GetTimestamp() - startedAt) / Stopwatch.Frequency, tags);
        if (error?.Store is not null || status == OperationStatus.StoreError)
        {
            Errors.Add(1, tags);
        }
    }

    private static void FinishProvider(Activity? activity, string storeId, string status, long startedAt)
    {
        activity?.SetTag(HPDBaseTelemetryTags.ResultStatus, status);
        if (status is "error" or "failed")
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
        }

        if (activity?.OperationName == HPDBaseTelemetrySpans.SqliteConnectionOpen)
        {
            var tags = ProviderTags(storeId, status);
            ConnectionsOpened.Add(1, tags);
            ConnectionOpenDuration.Record((double)(Stopwatch.GetTimestamp() - startedAt) / Stopwatch.Frequency, tags);
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
        if (error.Store is { } store)
        {
            activity.SetTag(HPDBaseTelemetryTags.ErrorRetryable, store.Retryable);
            activity.SetTag(HPDBaseTelemetryTags.SqliteNativeCode, store.NativeCode);
            activity.SetTag(HPDBaseTelemetryTags.SqliteNativeSubcode, store.NativeSubcode);
        }
    }

    private static TagList StoreTags(BaseOperationKind operation, string storeId, string collectionId, OperationStatus status, BaseError? error)
    {
        var tags = new TagList
        {
            { HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleSqlite },
            { HPDBaseTelemetryTags.ProviderKind, HPDBaseTelemetryValues.ProviderSqlite },
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

        if (error?.Store is { } store)
        {
            tags.Add(HPDBaseTelemetryTags.ErrorRetryable, store.Retryable);
            tags.Add(HPDBaseTelemetryTags.SqliteNativeCode, store.NativeCode);
            tags.Add(HPDBaseTelemetryTags.SqliteNativeSubcode, store.NativeSubcode);
        }

        return tags;
    }

    private static TagList ProviderTags(string storeId, string status) => new()
    {
        { HPDBaseTelemetryTags.ModuleId, HPDBaseTelemetryValues.ModuleSqlite },
        { HPDBaseTelemetryTags.ProviderKind, HPDBaseTelemetryValues.ProviderSqlite },
        { HPDBaseTelemetryTags.StoreId, storeId },
        { HPDBaseTelemetryTags.ResultStatus, status }
    };

    private static IEnumerable<Measurement<long>> ObserveMissingSchemaParts()
    {
        foreach (var pair in LastObservedMissingSchemaParts)
        {
            yield return new Measurement<long>(
                pair.Value,
                ProviderTags(pair.Key, pair.Value == 0 ? "ok" : "missing"));
        }
    }

    private static string OperationValue(BaseOperationKind value) => value switch
    {
        BaseOperationKind.List => "list",
        BaseOperationKind.Get => "get",
        BaseOperationKind.Create => "create",
        BaseOperationKind.Patch => "patch",
        BaseOperationKind.Replace => "replace",
        BaseOperationKind.Delete => "delete",
        _ => "unknown"
    };

    private static string CountModeValue(QueryCountMode value) => value switch
    {
        QueryCountMode.None => "none",
        QueryCountMode.Exact => "exact",
        QueryCountMode.IfAvailable => "ifAvailable",
        QueryCountMode.Estimated => "estimated",
        QueryCountMode.Limited => "limited",
        _ => "unknown"
    };

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
