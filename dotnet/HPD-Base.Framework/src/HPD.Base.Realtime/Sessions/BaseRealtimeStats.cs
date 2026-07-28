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
    private long _receiveIdleTimeouts;
    private long _joinRateRejections;
    private long _slowConsumerTerminations;
    private long _payloadLimitDrops;
    private long _durableJournalReads;
    private long _durableEventsProjected;
    private long _durableCursorRejections;

    public long ActiveConnections => Volatile.Read(ref _activeConnections);
    public long ActiveChannels => Volatile.Read(ref _activeChannels);
    public long StreamOpenFailures => Volatile.Read(ref _streamOpenFailures);
    public long PolicySkips => Volatile.Read(ref _policySkips);
    public long SendFailures => Volatile.Read(ref _sendFailures);
    /// <summary>Gets the number of connections closed after exceeding the receive-idle limit.</summary>
    public long ReceiveIdleTimeouts => Volatile.Read(ref _receiveIdleTimeouts);
    /// <summary>Gets the number of channel joins rejected by the per-connection rate limit.</summary>
    public long JoinRateRejections => Volatile.Read(ref _joinRateRejections);
    /// <summary>Gets the number of channels terminated because their consumers were too slow.</summary>
    public long SlowConsumerTerminations => Volatile.Read(ref _slowConsumerTerminations);
    public long PayloadLimitDrops => Volatile.Read(ref _payloadLimitDrops);
    public long DurableJournalReads => Volatile.Read(ref _durableJournalReads);
    public long DurableEventsProjected => Volatile.Read(ref _durableEventsProjected);
    public long DurableCursorRejections => Volatile.Read(ref _durableCursorRejections);

    public BaseRealtimeStats()
    {
        HPDBaseRealtimeTelemetry.RegisterStats(this);
    }

    /// <summary>Attempts to reserve one active connection within the configured maximum.</summary>
    /// <param name="maximum">The positive maximum number of active connections.</param>
    /// <returns><see langword="true"/> when the connection was reserved; otherwise, <see langword="false"/>.</returns>
    public bool TryRecordConnectionOpened(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);

        while (true)
        {
            var current = Volatile.Read(ref _activeConnections);
            if (current >= maximum)
                return false;

            if (Interlocked.CompareExchange(ref _activeConnections, current + 1, current) != current)
                continue;

            HPDBaseRealtimeTelemetry.RecordConnectionOpened();
            return true;
        }
    }

    /// <summary>Releases one previously reserved active connection.</summary>
    public void RecordConnectionClosed()
    {
        DecrementActive(ref _activeConnections, "connection");
        HPDBaseRealtimeTelemetry.RecordConnectionClosed();
    }

    /// <summary>Records one active realtime channel.</summary>
    public void RecordChannelOpened()
    {
        Interlocked.Increment(ref _activeChannels);
        HPDBaseRealtimeTelemetry.RecordChannelOpened();
    }

    /// <summary>Records the closure of one active realtime channel.</summary>
    public void RecordChannelClosed()
    {
        DecrementActive(ref _activeChannels, "channel");
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

    /// <summary>Records a connection that exceeded its receive-idle limit.</summary>
    public void RecordReceiveIdleTimeout()
    {
        Interlocked.Increment(ref _receiveIdleTimeouts);
        HPDBaseRealtimeTelemetry.RecordReceiveIdleTimeout();
    }

    /// <summary>Records a channel join rejected by the per-connection rate limit.</summary>
    public void RecordJoinRateRejection()
    {
        Interlocked.Increment(ref _joinRateRejections);
        HPDBaseRealtimeTelemetry.RecordJoinRateRejection();
    }

    /// <summary>Records a channel terminated because its consumer was too slow.</summary>
    public void RecordSlowConsumerTermination()
    {
        Interlocked.Increment(ref _slowConsumerTerminations);
        HPDBaseRealtimeTelemetry.RecordSlowConsumerTermination();
    }

    public void RecordPayloadLimitDrop()
    {
        Interlocked.Increment(ref _payloadLimitDrops);
        HPDBaseRealtimeTelemetry.RecordPayloadDrop();
    }

    public void RecordDurableJournalRead()
    {
        Interlocked.Increment(ref _durableJournalReads);
        HPDBaseRealtimeTelemetry.RecordDurableJournalRead();
    }

    public void RecordDurableEventProjected()
    {
        Interlocked.Increment(ref _durableEventsProjected);
        HPDBaseRealtimeTelemetry.RecordDurableEventProjected();
    }

    public void RecordDurableCursorRejection()
    {
        Interlocked.Increment(ref _durableCursorRejections);
        HPDBaseRealtimeTelemetry.RecordDurableCursorRejection();
    }

    private static void DecrementActive(ref long value, string kind)
    {
        while (true)
        {
            var current = Volatile.Read(ref value);
            if (current <= 0)
                throw new InvalidOperationException($"Cannot close a realtime {kind} that is not active.");

            if (Interlocked.CompareExchange(ref value, current - 1, current) == current)
                return;
        }
    }
}
