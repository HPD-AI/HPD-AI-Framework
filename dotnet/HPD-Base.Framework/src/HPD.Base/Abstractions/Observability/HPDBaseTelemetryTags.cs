namespace HPD.Base;

/// <summary>
/// Defines safe, low-cardinality tag names used by BASE spans, metrics, and structured logs.
/// </summary>
public static class HPDBaseTelemetryTags
{
    /// <summary>Stable module/package id.</summary>
    public const string ModuleId = "hpd.base.module.id";

    /// <summary>BASE operation kind or module operation kind.</summary>
    public const string OperationKind = "hpd.base.operation.kind";

    /// <summary>Schema-defined collection id, when bounded.</summary>
    public const string CollectionId = "hpd.base.collection.id";

    /// <summary>Configured store id, when bounded.</summary>
    public const string StoreId = "hpd.base.store.id";

    /// <summary>Provider kind such as <c>inmemory</c> or <c>sqlite</c>.</summary>
    public const string ProviderKind = "hpd.base.provider.kind";

    /// <summary>Operation result status.</summary>
    public const string ResultStatus = "hpd.base.result.status";

    /// <summary>Stable BASE error code.</summary>
    public const string ErrorCode = "hpd.base.error.code";

    /// <summary>Stable BASE error category.</summary>
    public const string ErrorCategory = "hpd.base.error.category";

    /// <summary>Whether the error can be retried.</summary>
    public const string ErrorRetryable = "hpd.base.error.retryable";

    /// <summary>Stable capability id.</summary>
    public const string CapabilityId = "hpd.base.capability.id";

    /// <summary>Stable capability failure reason.</summary>
    public const string CapabilityReason = "hpd.base.capability.reason";

    /// <summary>Policy effect.</summary>
    public const string PolicyEffect = "hpd.base.policy.effect";

    /// <summary>Stable policy reason code.</summary>
    public const string PolicyReasonCode = "hpd.base.policy.reason_code";

    /// <summary>Policy resource kind.</summary>
    public const string PolicyResourceKind = "hpd.base.policy.resource_kind";

    /// <summary>Query count mode.</summary>
    public const string QueryCountMode = "hpd.base.query.count_mode";

    /// <summary>Bucketed query page size.</summary>
    public const string QueryPageSizeBucket = "hpd.base.query.page_size.bucket";

    /// <summary>Whether query shape has a filter.</summary>
    public const string QueryHasFilter = "hpd.base.query.has_filter";

    /// <summary>Whether query shape has sorting.</summary>
    public const string QueryHasSort = "hpd.base.query.has_sort";

    /// <summary>Whether query shape has includes.</summary>
    public const string QueryHasInclude = "hpd.base.query.has_include";

    /// <summary>Safe relational plan status.</summary>
    public const string RelationalPlanStatus = "hpd.base.relational.plan.status";

    /// <summary>Relational filter pushdown support.</summary>
    public const string RelationalPushdownFilter = "hpd.base.relational.pushdown.filter";

    /// <summary>Relational sort pushdown support.</summary>
    public const string RelationalPushdownSort = "hpd.base.relational.pushdown.sort";

    /// <summary>Relational page pushdown support.</summary>
    public const string RelationalPushdownPage = "hpd.base.relational.pushdown.page";

    /// <summary>Relational count pushdown support.</summary>
    public const string RelationalPushdownCount = "hpd.base.relational.pushdown.count";

    /// <summary>Relational residual kind.</summary>
    public const string RelationalResidualKind = "hpd.base.relational.residual.kind";

    /// <summary>Result or descriptor visibility level.</summary>
    public const string VisibilityLevel = "hpd.base.visibility.level";

    /// <summary>BASE-owned event type.</summary>
    public const string EventType = "hpd.base.event.type";

    /// <summary>Event delivery guarantee.</summary>
    public const string EventDeliveryGuarantee = "hpd.base.event.delivery_guarantee";

    /// <summary>Configured file bucket id.</summary>
    public const string FileBucketId = "hpd.base.file.bucket.id";

    /// <summary>Bucketed file size.</summary>
    public const string FileSizeBucket = "hpd.base.file.size.bucket";

    /// <summary>Whether file overwrite was requested.</summary>
    public const string FileOverwriteRequested = "hpd.base.file.overwrite_requested";

    /// <summary>Realtime transport.</summary>
    public const string RealtimeTransport = "hpd.base.realtime.transport";

    /// <summary>Realtime channel kind, not channel name.</summary>
    public const string RealtimeChannelKind = "hpd.base.realtime.channel.kind";

    /// <summary>Bucketed realtime payload size.</summary>
    public const string RealtimePayloadSizeBucket = "hpd.base.realtime.payload_size.bucket";

    /// <summary>Authentication state.</summary>
    public const string AuthState = "hpd.base.auth.state";

    /// <summary>Bounded subject kind.</summary>
    public const string AuthSubjectKind = "hpd.base.auth.subject.kind";

    /// <summary>HPD.Auth policy composition mode.</summary>
    public const string AuthCompositionMode = "hpd.base.auth.composition_mode";

    /// <summary>Bucketed count value.</summary>
    public const string CountBucket = "hpd.base.count.bucket";

    /// <summary>Whether an operation correlation id exists, without recording its value.</summary>
    public const string CorrelationIdPresent = "hpd.base.correlation_id.present";

    /// <summary>SQLite native numeric error code.</summary>
    public const string SqliteNativeCode = "hpd.base.sqlite.error.code";

    /// <summary>SQLite native numeric extended error code.</summary>
    public const string SqliteNativeSubcode = "hpd.base.sqlite.error.subcode";
}
