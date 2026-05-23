using HPD.Events;

namespace Rhodium.Events;

/// <summary>
/// Replay ordering policy for finance events with domain-specific same-time tie breaks.
/// </summary>
public sealed class FinanceReplayOrderingPolicy : IReplayOrderingPolicy<FinanceEvent>
{
    private static readonly long UnixEpochTicks = DateTimeOffset.UnixEpoch.Ticks;

    /// <summary>
    /// Singleton default finance replay ordering policy.
    /// </summary>
    public static FinanceReplayOrderingPolicy Default { get; } = new();

    private FinanceReplayOrderingPolicy()
    {
    }

    /// <inheritdoc />
    public ReplayKey GetKey(FinanceEvent evt, ReplaySourceInfo source, long sourceSequence)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(source);

        return new ReplayKey(
            GetTimestampNs(evt),
            source.Priority,
            GetEventPriority(evt),
            source.SourceOrdinal,
            sourceSequence,
            evt.SequenceNumber);
    }

    private static long GetTimestampNs(FinanceEvent evt)
        => evt.ExchangeTimestampNs != 0
            ? evt.ExchangeTimestampNs
            : checked((evt.Timestamp.ToUniversalTime().Ticks - UnixEpochTicks) * 100L);

    private static int GetEventPriority(FinanceEvent evt)
        => evt switch
        {
            VenueStatusChanged => 0,
            InstrumentStatusChanged => 0,
            InstrumentClosed => 0,
            LifecycleEvent => 0,
            BookUpdated => 10,
            BookDeltaReceived => 10,
            BookDeltasReceived => 10,
            BookDepthSnapshotReceived => 10,
            QuoteReceived => 20,
            TradeOccurred => 30,
            BarClosed => 40,
            ExecutionEvent => 50,
            ControlEvent => 60,
            DiagnosticEvent => 70,
            _ => 100
        };
}
