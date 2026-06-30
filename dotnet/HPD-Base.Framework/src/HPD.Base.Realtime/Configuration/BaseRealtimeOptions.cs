using HPD.Events;

namespace HPD.Base.Realtime.Configuration;

public sealed class BaseRealtimeOptions
{
    public bool Enabled { get; set; } = true;
    public BaseRealtimeLimits Limits { get; set; } = new();
    public AsyncStreamBackpressureMode Backpressure { get; set; } = AsyncStreamBackpressureMode.Wait;
    public bool RequireAuthenticatedPrivateChannels { get; set; } = true;
}
