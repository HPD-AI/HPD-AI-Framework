using System.Runtime.CompilerServices;
using HPD.Events;
using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Data;

/// <summary>
/// Adapts an existing HPD replay source into the simulation catalog surface.
/// </summary>
public sealed class ReplaySourceSimulationCatalogAdapter : ISimulationCatalog
{
    private readonly IReplaySource<FinanceEvent> _source;
    private readonly IReadOnlyList<Instrument> _instruments;
    private readonly IReadOnlyDictionary<(Instrument Instrument, Type DataType), DateRange> _availableRanges;

    /// <summary>Create a catalog adapter around an existing finance replay source.</summary>
    public ReplaySourceSimulationCatalogAdapter(
        IReplaySource<FinanceEvent> source,
        IReadOnlyList<Instrument>? instruments = null,
        IReadOnlyDictionary<(Instrument Instrument, Type DataType), DateRange>? availableRanges = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _instruments = instruments ?? [];
        _availableRanges = availableRanges ?? new Dictionary<(Instrument Instrument, Type DataType), DateRange>();
    }

    /// <inheritdoc />
    public IReplaySource<FinanceEvent> CreateReplaySource(SimulationDataQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new FilteringReplaySource(_source, query);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Instrument> ListInstrumentsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var instrument in _instruments)
        {
            ct.ThrowIfCancellationRequested();
            yield return instrument;
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public Task<DateRange?> GetAvailableRangeAsync<T>(
        Instrument instrument,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(
            _availableRanges.TryGetValue((instrument, typeof(T)), out var range)
                ? range
                : (DateRange?)null);
    }

    private sealed class FilteringReplaySource(
        IReplaySource<FinanceEvent> source,
        SimulationDataQuery query) : IReplaySource<FinanceEvent>
    {
        public async IAsyncEnumerable<FinanceEvent> ReadAsync(
            ReplayReadOptions options,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            var (from, to) = GetBounds(options, query.Range);
            var emitted = 0;
            var sourceOptions = options with { From = null, To = null, Limit = null };
            await foreach (var evt in source.ReadAsync(sourceOptions, ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                if (!MatchesQuery(evt, query, from, to))
                    continue;

                yield return evt;
                emitted++;
                if (options.Limit is { } limit && emitted >= limit)
                    yield break;
            }
        }

        private static (DateTimeOffset? From, DateTimeOffset? To) GetBounds(
            ReplayReadOptions options,
            DateRange? range)
        {
            if (!range.HasValue)
                return (options.From, options.To);

            var from = range.Value.Start.ToDateTimeOffset();
            var to = range.Value.End.ToDateTimeOffset();
            if (options.From is { } existingFrom && existingFrom > from)
                from = existingFrom;
            if (options.To is { } existingTo && existingTo < to)
                to = existingTo;

            return (from, to);
        }

        private static bool MatchesQuery(
            FinanceEvent evt,
            SimulationDataQuery query,
            DateTimeOffset? from,
            DateTimeOffset? to)
        {
            if (query.Instruments.Count > 0
                && evt is MarketEvent market
                && !query.Instruments.Contains(market.Instrument))
            {
                return false;
            }

            var eventTime = GetFinanceEventTime(evt).ToDateTimeOffset();
            if (from.HasValue && eventTime < from.Value)
                return false;
            if (to.HasValue && eventTime >= to.Value)
                return false;

            return MatchesKind(evt, query.Kinds);
        }

        private static Instant GetFinanceEventTime(FinanceEvent evt)
            => evt switch
            {
                QuoteReceived quote => quote.Quote.Time.ExchangeTime,
                TradeOccurred trade => trade.Trade.Time.ExchangeTime,
                BarClosed bar => bar.Bar.Time,
                BookSnapshotReceived book => book.Book.Time,
                BookDepthSnapshotReceived snapshot => snapshot.Time,
                BookDepth10Received snapshot => snapshot.Time,
                BookLevelDeltaReceived delta => delta.Time,
                BookLevelDeltasReceived deltas => deltas.Time,
                BookOrderAdded added => added.Time,
                BookOrderModified modified => modified.Time,
                BookOrderDeleted deleted => deleted.Time,
                BookOrderExecuted executed => executed.Time,
                SettlementReferencePricePublished settlement => settlement.EffectiveAt,
                OptionAssignmentNoticePublished assignment => assignment.EffectiveAt,
                InstrumentStatusChanged status => status.Time,
                InstrumentClosed closed => closed.Time,
                _ => evt.Time
            };

        private static bool MatchesKind(FinanceEvent evt, SimulationDataKind kinds)
            => evt switch
            {
                BarClosed => kinds.HasFlag(SimulationDataKind.Bars),
                TradeOccurred => kinds.HasFlag(SimulationDataKind.Trades),
                QuoteReceived => kinds.HasFlag(SimulationDataKind.Quotes),
                BookSnapshotReceived or BookDepthSnapshotReceived or BookDepth10Received => kinds.HasFlag(SimulationDataKind.Books),
                BookLevelDeltaReceived or BookLevelDeltasReceived => kinds.HasFlag(SimulationDataKind.BookLevelDeltas),
                BookOrderAdded or BookOrderModified or BookOrderDeleted or BookOrderExecuted => kinds.HasFlag(SimulationDataKind.BookOrders),
                VenueStatusChanged or InstrumentStatusChanged or InstrumentClosed => kinds.HasFlag(SimulationDataKind.Status),
                ExecutionEvent => kinds.HasFlag(SimulationDataKind.Execution),
                LifecycleEvent => kinds.HasFlag(SimulationDataKind.Lifecycle),
                DiagnosticEvent => kinds.HasFlag(SimulationDataKind.Diagnostics),
                ControlEvent => kinds.HasFlag(SimulationDataKind.Control),
                _ => kinds == SimulationDataKind.All
            };
    }
}
