using HPD.Events;
using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class BaseRealtimeHealthContributor : IBaseHealthContributor, IBaseDiagnosticContributor
{
    private readonly BaseRealtimeOptions _options;
    private readonly BaseRealtimeStats _stats;
    private readonly IEventCoordinator _events;
    private readonly TimeProvider _timeProvider;

    public BaseRealtimeHealthContributor(
        IOptions<BaseRealtimeOptions> options,
        BaseRealtimeStats stats,
        IEventCoordinator events,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _stats = stats;
        _events = events;
        _timeProvider = timeProvider;
    }

    public string Id => BaseRealtimeModuleIds.Module;

    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();
        var eventStats = _events.GetStats();
        return ValueTask.FromResult<HealthDescriptor[]>(
        [
            new HealthDescriptor
            {
                Id = BaseRealtimeDescriptorContributor.HealthIds.Registration,
                Scope = HealthScope.Module,
                TargetRef = BaseRealtimeModuleIds.Module,
                Status = _options.Enabled ? HealthStatus.Healthy : HealthStatus.Disabled,
                CheckedAt = now,
                Summary = _options.Enabled ? "HPD.BASE realtime is enabled." : "HPD.BASE realtime is disabled.",
                PublicSafe = true,
                Visibility = VisibilityLevel.Public,
                Metrics =
                [
                    Metric("activeConnections", _stats.ActiveConnections),
                    Metric("activeChannels", _stats.ActiveChannels),
                    Metric("policySkips", _stats.PolicySkips),
                    Metric("sendFailures", _stats.SendFailures),
                    Metric("receiveIdleTimeouts", _stats.ReceiveIdleTimeouts),
                    Metric("payloadLimitDrops", _stats.PayloadLimitDrops),
                    Metric("durableJournalReads", _stats.DurableJournalReads),
                    Metric("durableEventsProjected", _stats.DurableEventsProjected),
                    Metric("durableCursorRejections", _stats.DurableCursorRejections),
                    Metric("hpdEventsSubscriberCount", eventStats.SubscriberCount),
                    Metric("hpdEventsInboxCount", eventStats.InboxCount),
                    Metric("hpdEventsTotalQueued", eventStats.TotalQueued),
                    Metric("hpdEventsTotalDropped", eventStats.TotalDropped),
                    Metric("hpdEventsMaxSubscriberDepth", eventStats.MaxSubscriberDepth),
                    new HealthMetric
                    {
                        Name = "backpressure",
                        Kind = HealthMetricValueKind.Text,
                        TextValue = _options.Backpressure.ToString()
                    }
                ]
            },
            new HealthDescriptor
            {
                Id = BaseRealtimeDescriptorContributor.HealthIds.EventStream,
                Scope = HealthScope.Dependency,
                TargetRef = "hpd.events",
                Status = _options.Enabled ? HealthStatus.Healthy : HealthStatus.Disabled,
                CheckedAt = now,
                Summary = "Realtime record feeds use HPD.Events IEventStreamSource<BaseRecordMutationEvent>.",
                PublicSafe = false,
                Visibility = VisibilityLevel.Admin
            }
        ]);
    }

    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();
        var eventStats = _events.GetStats();
        return ValueTask.FromResult<DiagnosticDescriptor[]>(
        [
            Diagnostic(
                BaseRealtimeDescriptorContributor.DiagnosticIds.Options,
                _options.CursorProtectionKey is null
                    ? "Realtime options are registered; durable replay/resume is not configured."
                    : "Realtime options are registered; durable replay/resume requires a transactional journal store per collection.",
                now),
            Diagnostic(BaseRealtimeDescriptorContributor.DiagnosticIds.StreamOpenFailures, $"HPD.Events stream open failures: {_stats.StreamOpenFailures}.", now),
            Diagnostic("hpd.base.realtime.hpdEventsCoordinatorStats", $"HPD.Events stats: subscribers={eventStats.SubscriberCount}, inboxes={eventStats.InboxCount}, queued={eventStats.TotalQueued}, dropped={eventStats.TotalDropped}, maxDepth={eventStats.MaxSubscriberDepth}.", now),
            Diagnostic("hpd.base.realtime.connectionStats", $"Realtime active connections={_stats.ActiveConnections}, active channels={_stats.ActiveChannels}, sendFailures={_stats.SendFailures}, receiveIdleTimeouts={_stats.ReceiveIdleTimeouts}, joinRateRejections={_stats.JoinRateRejections}, slowConsumerTerminations={_stats.SlowConsumerTerminations}, payloadLimitDrops={_stats.PayloadLimitDrops}.", now)
        ]);
    }

    private static HealthMetric Metric(string name, long value) => new()
    {
        Name = name,
        Kind = HealthMetricValueKind.Number,
        NumberValue = value
    };

    private static DiagnosticDescriptor Diagnostic(string id, string message, DateTimeOffset emittedAt) => new()
    {
        Id = id,
        Code = id,
        Severity = DiagnosticSeverity.Info,
        Message = message,
        Category = DiagnosticCategory.Capability,
        Visibility = VisibilityLevel.Admin,
        EmittedAt = emittedAt,
        RelatedFeatureIds =
        [
            BaseRealtimeFeatureIds.RecordChanges,
            BaseRealtimeFeatureIds.WebSocketTransport
        ]
    };
}
