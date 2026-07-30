using HPD.Base.Application.Collections;
using HPD.Base.Application.Sessions;
using HPD.Base.Realtime;
using HPD.Base.Realtime.Feeds;
using HPD.Events;

namespace HPD.Base.Application.Realtime;

/// <summary>Creates only valid live, durable, or resume feed requests.</summary>
public sealed class BaseSessionRealtime(
    IBaseRealtimeFeedSource source,
    BaseSession session)
{
    public BaseRealtimeBuilder<T> Live<T>(BaseCollection<T> collection) =>
        new(source, session, collection, durable: false, resumeCursor: null);

    public BaseRealtimeBuilder<T> Durable<T>(BaseCollection<T> collection) =>
        new(source, session, collection, durable: true, resumeCursor: null);

    public BaseRealtimeBuilder<T> Resume<T>(
        BaseCollection<T> collection,
        string cursor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cursor);
        return new BaseRealtimeBuilder<T>(
            source,
            session,
            collection,
            durable: true,
            resumeCursor: cursor);
    }
}

/// <summary>Builds one valid record-change feed without exposing HPD.Events.</summary>
public sealed class BaseRealtimeBuilder<T>
{
    private readonly IBaseRealtimeFeedSource _source;
    private readonly BaseSession _session;
    private readonly BaseCollection<T> _collection;
    private readonly bool _durable;
    private readonly string? _resumeCursor;
    private readonly bool _snapshots;
    private readonly bool _before;
    private readonly string? _recordId;
    private readonly BaseOperationKind[]? _operations;

    internal BaseRealtimeBuilder(
        IBaseRealtimeFeedSource source,
        BaseSession session,
        BaseCollection<T> collection,
        bool durable,
        string? resumeCursor,
        bool snapshots = false,
        bool before = false,
        string? recordId = null,
        BaseOperationKind[]? operations = null)
    {
        _source = source;
        _session = session;
        _collection = collection;
        _durable = durable;
        _resumeCursor = resumeCursor;
        _snapshots = snapshots;
        _before = before;
        _recordId = recordId;
        _operations = operations;
    }

    public BaseRealtimeBuilder<T> ForRecord(HPD.Base.RecordId recordId) =>
        Copy(recordId: recordId.Value);

    public BaseRealtimeBuilder<T> Operations(params ReadOnlySpan<BaseOperationKind> operations)
    {
        if (operations.Length == 0)
        {
            throw new ArgumentException("At least one realtime operation is required.", nameof(operations));
        }

        return Copy(operations: operations.ToArray());
    }

    public BaseRealtimeBuilder<T> IncludeSnapshots(bool includeBefore = false) =>
        Copy(snapshots: true, before: includeBefore);

    public async ValueTask<BaseRealtimeFeed> OpenAsync(
        CancellationToken cancellationToken = default)
    {
        var join = new BaseRealtimeChannelJoinRequest
        {
            Kind = BaseRealtimeChannelKinds.RecordChanges,
            Private = true,
            CollectionId = _collection.Id,
            RecordId = _recordId,
            Operations = _operations,
            TenantId = _session.Operation(BaseOperationKind.Query, _collection.Id).TenantId,
            IncludeSnapshots = _snapshots,
            IncludeBefore = _before,
            Durable = _durable,
            ResumeCursor = _resumeCursor,
        };
        AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>> opened =
            await _source.OpenAsync(
                new BaseRealtimeFeedRequest
                {
                    Channel = $"base:application:{Guid.NewGuid():N}",
                    Join = join,
                    Principal = _session.Principal,
                    Operation = _session.Operation(BaseOperationKind.Query, _collection.Id),
                },
                cancellationToken).ConfigureAwait(false);

        if (!opened.Succeeded || opened.Value is null)
        {
            throw new BaseRealtimeOpenException(
                opened.Error?.Code ?? "base.realtime.openFailed",
                opened.Error?.Message ?? "The realtime feed could not be opened.");
        }

        return new BaseRealtimeFeed(
            opened.Value.Items,
            new BaseRealtimeFeedMetadata
            {
                StreamId = opened.Value.Descriptor.StreamId,
                Cursor = opened.Value.Descriptor.Cursor,
                Replayable = opened.Value.Descriptor.Replayable,
                Resumable = opened.Value.Descriptor.Resumable,
            });
    }

    private BaseRealtimeBuilder<T> Copy(
        bool? snapshots = null,
        bool? before = null,
        string? recordId = null,
        BaseOperationKind[]? operations = null) =>
        new(
            _source,
            _session,
            _collection,
            _durable,
            _resumeCursor,
            snapshots ?? _snapshots,
            before ?? _before,
            recordId ?? _recordId,
            operations ?? _operations);
}

/// <summary>Opened standard async-enumerable feed and its honest capabilities.</summary>
public sealed class BaseRealtimeFeed(
    IAsyncEnumerable<BaseRealtimeEvent> events,
    BaseRealtimeFeedMetadata metadata) : IAsyncDisposable
{
    public IAsyncEnumerable<BaseRealtimeEvent> Events { get; } = events;
    public BaseRealtimeFeedMetadata Metadata { get; } = metadata;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed record BaseRealtimeFeedMetadata
{
    public string? StreamId { get; init; }
    public string? Cursor { get; init; }
    public bool Replayable { get; init; }
    public bool Resumable { get; init; }
}

public sealed class BaseRealtimeOpenException(string code, string safeMessage)
    : Exception(safeMessage)
{
    public string Code { get; } = Validate(code, nameof(code));
    public string SafeMessage { get; } = Validate(safeMessage, nameof(safeMessage));

    private static string Validate(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }
}
