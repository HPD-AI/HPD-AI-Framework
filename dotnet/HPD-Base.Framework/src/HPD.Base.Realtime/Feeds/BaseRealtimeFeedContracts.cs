using HPD.Base.Runtime;
using HPD.Events;

namespace HPD.Base.Realtime.Feeds;

public sealed record BaseRealtimeFeedRequest
{
    public required string Channel { get; init; }
    public required BaseRealtimeChannelJoinRequest Join { get; init; }
    public required PrincipalContext Principal { get; init; }
    public required OperationContext Operation { get; init; }
}

public interface IBaseRealtimeFeedSource
{
    ValueTask<AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>>> OpenAsync(
        BaseRealtimeFeedRequest request,
        CancellationToken cancellationToken = default);
}
