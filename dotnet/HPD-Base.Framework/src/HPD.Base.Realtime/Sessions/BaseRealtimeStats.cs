using System.Threading;
using HPD.Base.Realtime.Observability;

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

    public BaseRealtimeStats()
    {
        HPDBaseRealtimeTelemetry.RegisterStats(this);
    }

    public void RecordConnectionOpened()
    {
        Interlocked.Increment(ref _activeConnections);
        HPDBaseRealtimeTelemetry.RecordConnectionOpened();
    }

    public void RecordConnectionClosed()
    {
        Interlocked.Decrement(ref _activeConnections);
        HPDBaseRealtimeTelemetry.RecordConnectionClosed();
    }

    public void RecordChannelOpened()
    {
        Interlocked.Increment(ref _activeChannels);
        HPDBaseRealtimeTelemetry.RecordChannelOpened();
    }

    public void RecordChannelClosed()
    {
        Interlocked.Decrement(ref _activeChannels);
        HPDBaseRealtimeTelemetry.RecordChannelClosed();
    }
    public void RecordStreamOpenFailure()
    {
        Interlocked.Increment(ref _streamOpenFailures);
        HPDBaseRealtimeTelemetry.RecordStreamOpenFailure();
    }

    public void RecordPolicySkip()
    {
        Interlocked.Increment(ref _policySkips);
        HPDBaseRealtimeTelemetry.RecordPolicySkip();
    }

    public void RecordSendFailure()
    {
        Interlocked.Increment(ref _sendFailures);
        HPDBaseRealtimeTelemetry.RecordSendFailure();
    }

    public void RecordHeartbeatTimeout()
    {
        Interlocked.Increment(ref _heartbeatTimeouts);
        HPDBaseRealtimeTelemetry.RecordHeartbeatTimeout();
    }

    public void RecordPayloadLimitDrop()
    {
        Interlocked.Increment(ref _payloadLimitDrops);
        HPDBaseRealtimeTelemetry.RecordPayloadDrop();
    }
}
