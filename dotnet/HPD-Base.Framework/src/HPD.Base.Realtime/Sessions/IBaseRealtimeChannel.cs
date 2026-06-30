using HPD.Events;

namespace HPD.Base.Realtime.Sessions;

public interface IBaseRealtimeChannel : IAsyncDisposable
{
    string Channel { get; }
    BaseRealtimeChannelJoinRequest Join { get; }
    AsyncStreamDescriptor Descriptor { get; }
}

public interface IBaseRealtimeSession : IAsyncDisposable
{
    string ConnectionId { get; }
    int ActiveChannelCount { get; }
    BaseRealtimeConnectionDescriptor Describe();
}
