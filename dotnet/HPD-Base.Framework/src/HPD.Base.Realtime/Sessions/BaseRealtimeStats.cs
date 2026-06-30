using System.Threading;

namespace HPD.Base.Realtime;

public sealed class BaseRealtimeStats
{
    private long _activeConnections;
    private long _activeChannels;
    private long _streamOpenFailures;
    private long _policySkips;
    private long _sendFailures;
    private long _heartbeatTimeouts;
    private long _payloadLimitDrops;

    public long ActiveConnections => Volatile.Read(ref _activeConnections);
    public long ActiveChannels => Volatile.Read(ref _activeChannels);
    public long StreamOpenFailures => Volatile.Read(ref _streamOpenFailures);
    public long PolicySkips => Volatile.Read(ref _policySkips);
    public long SendFailures => Volatile.Read(ref _sendFailures);
    public long HeartbeatTimeouts => Volatile.Read(ref _heartbeatTimeouts);
    public long PayloadLimitDrops => Volatile.Read(ref _payloadLimitDrops);

    public void RecordConnectionOpened() => Interlocked.Increment(ref _activeConnections);
    public void RecordConnectionClosed() => Interlocked.Decrement(ref _activeConnections);
    public void RecordChannelOpened() => Interlocked.Increment(ref _activeChannels);
    public void RecordChannelClosed() => Interlocked.Decrement(ref _activeChannels);
    public void RecordStreamOpenFailure() => Interlocked.Increment(ref _streamOpenFailures);
    public void RecordPolicySkip() => Interlocked.Increment(ref _policySkips);
    public void RecordSendFailure() => Interlocked.Increment(ref _sendFailures);
    public void RecordHeartbeatTimeout() => Interlocked.Increment(ref _heartbeatTimeouts);
    public void RecordPayloadLimitDrop() => Interlocked.Increment(ref _payloadLimitDrops);
}
