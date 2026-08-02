using HPD.Events;

namespace HPD.Base;

/// <summary>Represents a base realtime feed request.</summary>
public sealed record BaseRealtimeFeedRequest
{
    /// <summary>Gets or sets the channel.</summary>
    public required string Channel { get; init; }
    /// <summary>Gets or sets the join.</summary>
    public required BaseRealtimeChannelJoinRequest Join { get; init; }
    /// <summary>Gets or sets the principal.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets or sets the operation.</summary>
    public required OperationContext Operation { get; init; }
}

/// <summary>Defines the ibase realtime feed source contract.</summary>
public interface IBaseRealtimeFeedSource
{
    /// <summary>Executes the open async operation.</summary>
    ValueTask<AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>>> OpenAsync(
        BaseRealtimeFeedRequest request,
        CancellationToken cancellationToken = default);
}
