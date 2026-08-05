
namespace HPD.Base;

/// <summary>Represents a base realtime projection request.</summary>
public sealed record BaseRealtimeProjectionRequest
{
    /// <summary>Gets or sets the event.</summary>
    public required BaseRecordMutationEvent Event { get; init; }
    /// <summary>Gets or sets the join.</summary>
    public required BaseRealtimeChannelJoinRequest Join { get; init; }
    /// <summary>Gets or sets the principal.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets or sets the operation.</summary>
    public required OperationContext Operation { get; init; }
}

/// <summary>Defines the ibase realtime projection service contract.</summary>
public interface IBaseRealtimeProjectionService
{
    /// <summary>Executes the project async operation.</summary>
    ValueTask<BaseRealtimeEvent?> ProjectAsync(
        BaseRealtimeProjectionRequest request,
        CancellationToken cancellationToken = default);
}
