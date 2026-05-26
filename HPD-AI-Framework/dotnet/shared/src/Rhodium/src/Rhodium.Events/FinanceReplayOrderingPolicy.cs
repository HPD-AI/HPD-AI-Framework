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
        => evt switch
        {
            QuoteReceived quote => quote.Quote.Time.ExchangeTime.Nanos,
            TradeOccurred trade => trade.Trade.Time.ExchangeTime.Nanos,
            BarClosed bar => bar.Bar.Time.Nanos,
            BookSnapshotReceived book => book.Book.Time.Nanos,
            BookLevelDeltaReceived delta when delta.ExchangeTimestampNs != 0 => delta.ExchangeTimestampNs,
            BookLevelDeltasReceived deltas when deltas.ExchangeTimestampNs != 0 => deltas.ExchangeTimestampNs,
            BookOrderAdded added when added.ExchangeTimestampNs != 0 => added.ExchangeTimestampNs,
            BookOrderModified modified when modified.ExchangeTimestampNs != 0 => modified.ExchangeTimestampNs,
            BookOrderDeleted deleted when deleted.ExchangeTimestampNs != 0 => deleted.ExchangeTimestampNs,
            BookOrderExecuted executed when executed.ExchangeTimestampNs != 0 => executed.ExchangeTimestampNs,
            BookDepthSnapshotReceived snapshot when snapshot.ExchangeTimestampNs != 0 => snapshot.ExchangeTimestampNs,
            BookDepth10Received snapshot when snapshot.ExchangeTimestampNs != 0 => snapshot.ExchangeTimestampNs,
            SettlementReferencePricePublished settlement => settlement.EffectiveAt.Nanos,
            OptionAssignmentNoticePublished assignment => assignment.EffectiveAt.Nanos,
            _ => evt.ExchangeTimestampNs != 0
                ? evt.ExchangeTimestampNs
                : checked((evt.Timestamp.ToUniversalTime().Ticks - UnixEpochTicks) * 100L)
        };

    private static int GetEventPriority(FinanceEvent evt)
        => evt switch
        {
            VenueStatusChanged => 0,
            InstrumentStatusChanged => 0,
            InstrumentClosed => 0,
            LifecycleEvent => 0,
            BookSnapshotReceived => 10,
            BookLevelDeltaReceived => 10,
            BookLevelDeltasReceived => 10,
            BookOrderAdded => 10,
            BookOrderModified => 10,
            BookOrderDeleted => 10,
            BookOrderExecuted => 10,
            BookDepthSnapshotReceived => 10,
            BookDepth10Received => 10,
            QuoteReceived => 20,
            TradeOccurred => 30,
            BarClosed => 40,
            ExecutionEvent => 50,
            ControlEvent => 60,
            DiagnosticEvent => 70,
            _ => 100
        };
}
