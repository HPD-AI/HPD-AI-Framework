
namespace HPD.Base;

public sealed record BaseRealtimeProjectionRequest
{
    public required BaseRecordMutationEvent Event { get; init; }
    public required BaseRealtimeChannelJoinRequest Join { get; init; }
    public required PrincipalContext Principal { get; init; }
    public required OperationContext Operation { get; init; }
}

public interface IBaseRealtimeProjectionService
{
    ValueTask<BaseRealtimeEvent?> ProjectAsync(
        BaseRealtimeProjectionRequest request,
        CancellationToken cancellationToken = default);
}
