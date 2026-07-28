using System.Runtime.CompilerServices;
using HPD.Base.Events;
using HPD.Base.Realtime.Configuration;
using HPD.Base.Realtime.Observability;
using HPD.Base.Realtime.Observability.Logging;
using HPD.Base.Realtime.Projection;
using HPD.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HPD.Base.Realtime.Feeds;

internal sealed class DefaultBaseRealtimeFeedSource : IBaseRealtimeFeedSource
{
    public const string RecordChangesStreamId = "base.realtime.record_changes";

    private readonly IEventStreamSource<BaseRecordMutationEvent> _events;
    private readonly IBaseRealtimeProjectionService _projection;
    private readonly BaseRealtimeOptions _options;
    private readonly BaseRealtimeStats _stats;
    private readonly ILogger<DefaultBaseRealtimeFeedSource> _logger;

    public DefaultBaseRealtimeFeedSource(
        IEventStreamSource<BaseRecordMutationEvent> events,
        IBaseRealtimeProjectionService projection,
        IOptions<BaseRealtimeOptions> options,
        BaseRealtimeStats stats,
        ILogger<DefaultBaseRealtimeFeedSource> logger)
    {
        _events = events;
        _projection = projection;
        _options = options.Value;
        _stats = stats;
        _logger = logger;
    }

    public async ValueTask<AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>>> OpenAsync(
        BaseRealtimeFeedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await HPDBaseRealtimeTelemetry.TraceJoinAsync(
            ChannelKindValue(request.Join.Kind),
            () => OpenCoreAsync(request, cancellationToken)).ConfigureAwait(false);
    }

    private async ValueTask<AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>>> OpenCoreAsync(
        BaseRealtimeFeedRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.Enabled)
        {
            return AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>>.Failed(
                AsyncStreamOpenStatus.CapabilityUnavailable,
                new AsyncStreamError
                {
                    Code = BaseRealtimeErrorCodes.Disabled,
                    Message = "HPD.BASE realtime is disabled.",
                    Category = AsyncStreamErrorCategory.Capability
                });
        }

        if (request.Join.Kind != BaseRealtimeChannelKinds.RecordChanges)
        {
            return AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>>.Failed(
                AsyncStreamOpenStatus.Unsupported,
                new AsyncStreamError
                {
                    Code = BaseRealtimeErrorCodes.ChannelUnsupported,
                    Message = "The requested realtime channel kind is not supported.",
                    Target = request.Join.Kind,
                    Category = AsyncStreamErrorCategory.Unsupported
                });
        }

        var opened = await _events.OpenAsync(new EventStreamRequest<BaseRecordMutationEvent>
        {
            StreamId = RecordChangesStreamId,
            Capacity = _options.Limits.StreamCapacity,
            Backpressure = _options.Backpressure,
            IncludeDerivedTypes = false
        }, cancellationToken).ConfigureAwait(false);

        if (!opened.Succeeded || opened.Value is null)
        {
            _stats.RecordStreamOpenFailure();
            HPDBaseRealtimeLog.EventStreamOpenFailed(
                _logger,
                "dependency",
                BaseRealtimeErrorCodes.CapabilityUnavailable);
            return AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>>.Failed(
                opened.Status,
                opened.Error ?? new AsyncStreamError
                {
                    Code = BaseRealtimeErrorCodes.CapabilityUnavailable,
                    Message = "The underlying HPD.Events stream could not be opened.",
                    Category = AsyncStreamErrorCategory.Dependency
                });
        }

        return AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>>.Opened(new AsyncStream<BaseRealtimeEvent>
        {
            Descriptor = opened.Value.Descriptor,
            Items = ProjectAsync(request, opened.Value.Items, cancellationToken)
        });
    }

    private async IAsyncEnumerable<BaseRealtimeEvent> ProjectAsync(
        BaseRealtimeFeedRequest request,
        IAsyncEnumerable<BaseRecordMutationEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _stats.RecordChannelOpened();
        try
        {
            await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (!Matches(request.Join, evt))
                    continue;

                BaseRealtimeEvent? projected;
                try
                {
                    projected = await _projection.ProjectAsync(new BaseRealtimeProjectionRequest
                    {
                        Event = evt,
                        Join = request.Join,
                        Principal = request.Principal,
                        Operation = request.Operation
                    }, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    HPDBaseRealtimeLog.EventProjectionFailed(
                        _logger,
                        "unexpected",
                        BaseRealtimeErrorCodes.CapabilityUnavailable);
                    yield break;
                }

                if (projected is null)
                {
                    _stats.RecordPolicySkip();
                    continue;
                }

                HPDBaseRealtimeTelemetry.RecordEventProjected();
                yield return projected;
            }
        }
        finally
        {
            _stats.RecordChannelClosed();
        }
    }

    private static bool Matches(BaseRealtimeChannelJoinRequest join, BaseRecordMutationEvent evt)
    {
        if (!string.IsNullOrWhiteSpace(join.CollectionId)
            && !string.Equals(join.CollectionId, evt.Resource.CollectionId, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(join.RecordId)
            && !string.Equals(join.RecordId, evt.Resource.RecordId?.Value, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(join.TenantId)
            && !string.Equals(join.TenantId, evt.TenantId, StringComparison.Ordinal))
            return false;

        if (join.Operations is { Length: > 0 } operations && !operations.Contains(evt.Operation))
            return false;

        if (join.EventTypes is { Length: > 0 } eventTypes && !eventTypes.Contains(evt.Type, StringComparer.Ordinal))
            return false;

        return true;
    }

    private static string ChannelKindValue(string value) => value switch
    {
        BaseRealtimeChannelKinds.RecordChanges => "recordChanges",
        _ => "unknown"
    };
}
