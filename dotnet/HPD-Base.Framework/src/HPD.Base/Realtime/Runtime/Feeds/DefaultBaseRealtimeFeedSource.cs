using System.Runtime.CompilerServices;
using HPD.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class DefaultBaseRealtimeFeedSource : IBaseRealtimeFeedSource
{
    public const string RecordChangesStreamId = "base.realtime.record_changes";

    private readonly IEventStreamSource<BaseRecordMutationEvent> _events;
    private readonly IBaseRealtimeProjectionService _projection;
    private readonly BaseRealtimeOptions _options;
    private readonly BaseRealtimeStats _stats;
    private readonly ILogger<DefaultBaseRealtimeFeedSource> _logger;
    private readonly IBaseSchemaProvider _schema;
    private readonly IRecordStoreResolver _stores;
    private readonly BaseRealtimeCursorProtector _cursors;
    private readonly TimeProvider _timeProvider;

    public DefaultBaseRealtimeFeedSource(
        IEventStreamSource<BaseRecordMutationEvent> events,
        IBaseRealtimeProjectionService projection,
        IOptions<BaseRealtimeOptions> options,
        BaseRealtimeStats stats,
        IBaseSchemaProvider schema,
        IRecordStoreResolver stores,
        BaseRealtimeCursorProtector cursors,
        TimeProvider timeProvider,
        ILogger<DefaultBaseRealtimeFeedSource> logger)
    {
        _events = events;
        _projection = projection;
        _options = options.Value;
        _stats = stats;
        _schema = schema;
        _stores = stores;
        _cursors = cursors;
        _timeProvider = timeProvider;
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

        if (request.Join.Durable)
            return await OpenDurableAsync(request, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.Join.ResumeCursor))
        {
            return Failure(
                AsyncStreamOpenStatus.ValidationFailed,
                BaseRealtimeErrorCodes.CursorInvalid,
                "A resume cursor requires a durable realtime channel.",
                AsyncStreamErrorCategory.Validation);
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

    private async ValueTask<AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>>> OpenDurableAsync(
        BaseRealtimeFeedRequest request,
        CancellationToken cancellationToken)
    {
        if (!_cursors.Enabled)
        {
            return Failure(
                AsyncStreamOpenStatus.CapabilityUnavailable,
                BaseRealtimeErrorCodes.CapabilityUnavailable,
                "Durable realtime cursors are not configured.",
                AsyncStreamErrorCategory.Capability);
        }

        if (string.IsNullOrWhiteSpace(request.Join.CollectionId))
        {
            return Failure(
                AsyncStreamOpenStatus.ValidationFailed,
                BaseRealtimeErrorCodes.DurableCollectionRequired,
                "Durable realtime channels require one collection filter.",
                AsyncStreamErrorCategory.Validation);
        }

        var collection = await _schema.GetCollectionAsync(
            request.Join.CollectionId,
            request.Principal,
            request.Operation,
            VisibilityLevel.Internal,
            cancellationToken).ConfigureAwait(false);
        if (collection.Value is null)
        {
            return Failure(
                AsyncStreamOpenStatus.NotFound,
                BaseRealtimeErrorCodes.ChannelUnauthorized,
                "The durable realtime collection is unavailable.",
                AsyncStreamErrorCategory.NotFound);
        }

        var resolved = _stores.Resolve(collection.Value, request.Operation);
        if (resolved.Value is not ITransactionalMutationJournalStore journal)
        {
            return Failure(
                AsyncStreamOpenStatus.CapabilityUnavailable,
                BaseRealtimeErrorCodes.CapabilityUnavailable,
                "The selected collection does not support durable realtime replay.",
                AsyncStreamErrorCategory.Capability);
        }

        var bounds = await journal.GetMutationJournalBoundsAsync(cancellationToken).ConfigureAwait(false);
        var position = bounds.HighWatermark;
        var cursorScope = CursorScope(request);
        if (!string.IsNullOrWhiteSpace(request.Join.ResumeCursor))
        {
            var cursor = _cursors.Unprotect(
                request.Join.ResumeCursor,
                resolved.Value.Capabilities.StoreId,
                cursorScope);
            if (cursor.Status != BaseRealtimeCursorStatus.Valid)
                return CursorFailure(cursor.Status);

            position = cursor.Position;
            if (bounds.Earliest.Value > 0 && position.Value < bounds.Earliest.Value - 1)
            {
                _stats.RecordDurableCursorRejection();
                return Failure(
                    AsyncStreamOpenStatus.ValidationFailed,
                    BaseRealtimeErrorCodes.CursorExpired,
                    "The resume cursor is older than the retained mutation journal.",
                    AsyncStreamErrorCategory.Validation);
            }
            if (position.Value > bounds.HighWatermark.Value)
            {
                _stats.RecordDurableCursorRejection();
                return Failure(
                    AsyncStreamOpenStatus.ValidationFailed,
                    BaseRealtimeErrorCodes.CursorInvalid,
                    "The resume cursor is ahead of the committed mutation journal.",
                    AsyncStreamErrorCategory.Validation);
            }
        }

        return AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>>.Opened(
            new AsyncStream<BaseRealtimeEvent>
            {
                Descriptor = new AsyncStreamDescriptor
                {
                    StreamId = RecordChangesStreamId,
                    Cursor = _cursors.Protect(
                        position,
                        resolved.Value.Capabilities.StoreId,
                        cursorScope),
                    Replayable = true,
                    Resumable = true,
                    Backpressure = AsyncStreamBackpressureMode.Wait,
                    DeliveryGuarantee = AsyncStreamDeliveryGuarantee.AtLeastOnce
                },
                Items = ProjectDurableAsync(
                    request,
                    journal,
                    resolved.Value.Capabilities.StoreId,
                    cursorScope,
                    position,
                    cancellationToken)
            });
    }

    private async IAsyncEnumerable<BaseRealtimeEvent> ProjectDurableAsync(
        BaseRealtimeFeedRequest request,
        ITransactionalMutationJournalStore journal,
        string storeId,
        BaseRealtimeChannelJoinRequest cursorScope,
        BaseMutationJournalPosition startingPosition,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _stats.RecordChannelOpened();
        var position = startingPosition;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var page = await journal.ReadMutationJournalAsync(
                    new BaseMutationJournalReadRequest
                    {
                        After = position,
                        Limit = _options.Limits.ReplayBatchSize
                    },
                    cancellationToken).ConfigureAwait(false);
                _stats.RecordDurableJournalRead();

                if (page.Earliest.Value > 0 && position.Value < page.Earliest.Value - 1)
                {
                    _stats.RecordDurableCursorRejection();
                    throw new BaseRealtimeFeedException(
                        BaseRealtimeErrorCodes.CursorExpired,
                        "The durable realtime cursor is older than the retained mutation journal.");
                }

                foreach (var entry in page.Entries)
                {
                    position = entry.Position;
                    var evt = JournalEvent(entry);
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
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (BaseDependencyInvalidationException)
                    {
                        HPDBaseRealtimeLog.EventProjectionFailed(
                            _logger,
                            "dependencyInvalidation",
                            BaseRealtimeErrorCodes.DependencyInvalidationFailed);
                        throw new BaseRealtimeFeedException(
                            BaseRealtimeErrorCodes.DependencyInvalidationFailed,
                            "Realtime dependency invalidation could not be produced safely.");
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        HPDBaseRealtimeLog.EventProjectionFailed(
                            _logger,
                            "unexpected",
                            BaseRealtimeErrorCodes.ProjectionFailed);
                        throw new BaseRealtimeFeedException(
                            BaseRealtimeErrorCodes.ProjectionFailed,
                            "Realtime event projection failed.");
                    }

                    if (projected is null)
                    {
                        _stats.RecordPolicySkip();
                        continue;
                    }

                    HPDBaseRealtimeTelemetry.RecordEventProjected();
                    _stats.RecordDurableEventProjected();
                    yield return projected with
                    {
                        Cursor = _cursors.Protect(position, storeId, cursorScope)
                    };
                }

                if (page.HasMore)
                    continue;

                await Task.Delay(
                    TimeSpan.FromMilliseconds(_options.Limits.DurablePollIntervalMilliseconds),
                    _timeProvider,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _stats.RecordChannelClosed();
        }
    }

    private static BaseRecordMutationEvent JournalEvent(BaseMutationJournalEntry entry) => new()
    {
        EventId = entry.EventId,
        Type = entry.Type,
        SchemaVersion = entry.SchemaVersion,
        Timestamp = entry.OccurredAt,
        TenantId = entry.TenantId,
        Visibility = entry.Visibility,
        Operation = entry.Operation,
        Resource = new EventResource
        {
            Kind = EventResourceKind.Record,
            CollectionId = entry.CollectionId,
            RecordId = entry.RecordId
        },
        Before = entry.Before,
        After = entry.After
    };

    private AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>> CursorFailure(
        BaseRealtimeCursorStatus status)
    {
        _stats.RecordDurableCursorRejection();
        return status switch
        {
        BaseRealtimeCursorStatus.ScopeMismatch => Failure(
            AsyncStreamOpenStatus.ValidationFailed,
            BaseRealtimeErrorCodes.CursorScopeMismatch,
            "The resume cursor does not match the requested durable channel.",
            AsyncStreamErrorCategory.Validation),
        BaseRealtimeCursorStatus.Expired => Failure(
            AsyncStreamOpenStatus.ValidationFailed,
            BaseRealtimeErrorCodes.CursorExpired,
            "The resume cursor has expired.",
            AsyncStreamErrorCategory.Validation),
        BaseRealtimeCursorStatus.VersionUnsupported => Failure(
            AsyncStreamOpenStatus.Unsupported,
            BaseRealtimeErrorCodes.CursorVersionUnsupported,
            "The resume cursor version is not supported.",
            AsyncStreamErrorCategory.Unsupported),
        _ => Failure(
            AsyncStreamOpenStatus.ValidationFailed,
            BaseRealtimeErrorCodes.CursorInvalid,
            "The resume cursor is invalid.",
            AsyncStreamErrorCategory.Validation)
        };
    }

    private static AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>> Failure(
        AsyncStreamOpenStatus status,
        string code,
        string message,
        AsyncStreamErrorCategory category) =>
        AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>>.Failed(
            status,
            new AsyncStreamError
            {
                Code = code,
                Message = message,
                Category = category
            });

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
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (BaseDependencyInvalidationException)
                {
                    HPDBaseRealtimeLog.EventProjectionFailed(
                        _logger,
                        "dependencyInvalidation",
                        BaseRealtimeErrorCodes.DependencyInvalidationFailed);
                    throw new BaseRealtimeFeedException(
                        BaseRealtimeErrorCodes.DependencyInvalidationFailed,
                        "Realtime dependency invalidation could not be produced safely.");
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    HPDBaseRealtimeLog.EventProjectionFailed(
                        _logger,
                        "unexpected",
                        BaseRealtimeErrorCodes.ProjectionFailed);
                    throw new BaseRealtimeFeedException(
                        BaseRealtimeErrorCodes.ProjectionFailed,
                        "Realtime event projection failed.");
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

    private static BaseRealtimeChannelJoinRequest CursorScope(BaseRealtimeFeedRequest request) =>
        request.Join with
        {
            ResumeCursor = null,
            TenantId = request.Join.TenantId
                ?? request.Operation.TenantId
                ?? request.Principal.CurrentTenantId
        };

    private static string ChannelKindValue(string value) => value switch
    {
        BaseRealtimeChannelKinds.RecordChanges => "recordChanges",
        _ => "unknown"
    };
}
