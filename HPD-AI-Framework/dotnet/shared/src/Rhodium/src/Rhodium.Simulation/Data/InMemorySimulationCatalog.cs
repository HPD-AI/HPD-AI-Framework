using System.Runtime.CompilerServices;
using HPD.Events;
using HPD.Events.Core;
using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Data;

/// <summary>
/// Process-local simulation catalog for deterministic fixtures, generated data, and tests.
/// </summary>
public sealed class InMemorySimulationCatalog : ISimulationCatalog
{
    private readonly List<FinanceEvent> _events = [];

    /// <summary>Create an empty in-memory simulation catalog.</summary>
    public InMemorySimulationCatalog()
    {
    }

    /// <summary>Create an in-memory simulation catalog seeded with events.</summary>
    public InMemorySimulationCatalog(IEnumerable<FinanceEvent> events)
    {
        AddRange(events);
    }

    /// <summary>Number of events currently held by the catalog.</summary>
    public int EventCount => _events.Count;

    /// <summary>Add one event to the catalog.</summary>
    public InMemorySimulationCatalog Add(FinanceEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        _events.Add(evt);
        return this;
    }

    /// <summary>Add many events to the catalog in enumeration order.</summary>
    public InMemorySimulationCatalog AddRange(IEnumerable<FinanceEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var evt in events)
            Add(evt);

        return this;
    }

    /// <inheritdoc />
    public IReplaySource<FinanceEvent> CreateReplaySource(SimulationDataQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new ReplaySourceSimulationCatalogAdapter(
                new EnumerableReplaySource<FinanceEvent>(_events),
                ListInstruments(),
                BuildAvailableRanges())
            .CreateReplaySource(query);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Instrument> ListInstrumentsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var instrument in ListInstruments())
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
        var found = false;
        var start = default(Instant);
        var end = default(Instant);
        for (var i = 0; i < _events.Count; i++)
        {
            var evt = _events[i];
            if (!MatchesInstrument(evt, instrument) || !MatchesDataType<T>(evt))
                continue;

            var time = GetFinanceEventTime(evt);
            if (!found)
            {
                start = time;
                end = time;
                found = true;
            }
            else
            {
                if (time < start)
                    start = time;
                if (time > end)
                    end = time;
            }
        }

        return Task.FromResult(!found
            ? (DateRange?)null
            : new DateRange(start, end));
    }

    private IReadOnlyList<Instrument> ListInstruments()
    {
        var seen = new HashSet<Instrument>();
        var instruments = new List<Instrument>();
        for (var i = 0; i < _events.Count; i++)
        {
            if (_events[i] is not MarketEvent market || !seen.Add(market.Instrument))
                continue;

            instruments.Add(market.Instrument);
        }

        instruments.Sort(static (left, right) =>
        {
            var venue = string.CompareOrdinal(left.Venue.Name, right.Venue.Name);
            return venue != 0
                ? venue
                : string.CompareOrdinal(left.Asset.Symbol, right.Asset.Symbol);
        });
        return instruments;
    }

    private IReadOnlyDictionary<(Instrument Instrument, Type DataType), DateRange> BuildAvailableRanges()
    {
        var builders = new Dictionary<(Instrument Instrument, Type DataType), RangeBuilder>();
        for (var i = 0; i < _events.Count; i++)
        {
            if (_events[i] is not MarketEvent market)
                continue;

            var time = GetFinanceEventTime(market);
            AddAvailableRange(builders, market.Instrument, market.GetType(), time);
            AddAvailableRange(builders, market.Instrument, typeof(FinanceEvent), time);
            AddCatalogDataRanges(builders, market, time);
        }

        var ranges = new Dictionary<(Instrument Instrument, Type DataType), DateRange>(builders.Count);
        foreach (var entry in builders)
            ranges[entry.Key] = new DateRange(entry.Value.Start, entry.Value.End);

        return ranges;
    }

    private static bool MatchesInstrument(FinanceEvent evt, Instrument instrument)
        => evt is MarketEvent market && market.Instrument == instrument;

    private static bool MatchesDataType<T>(FinanceEvent evt)
    {
        var dataType = typeof(T);
        if (dataType == typeof(FinanceEvent) || dataType == evt.GetType())
            return true;

        return evt switch
        {
            BarClosed => dataType == typeof(Bar),
            TradeOccurred => dataType == typeof(Trade),
            QuoteReceived => dataType == typeof(Quote),
            BookSnapshotReceived or BookDepthSnapshotReceived or BookDepth10Received => dataType == typeof(Book),
            BookLevelDeltaReceived or BookLevelDeltasReceived => dataType == typeof(BookLevelDelta),
            BookOrderAdded or BookOrderModified or BookOrderDeleted or BookOrderExecuted => dataType == typeof(BookOrder),
            InstrumentStatusChanged or InstrumentClosed => dataType == typeof(MarketStatus),
            _ => false
        };
    }

    private static void AddCatalogDataRanges(
        Dictionary<(Instrument Instrument, Type DataType), RangeBuilder> ranges,
        MarketEvent evt,
        Instant time)
    {
        switch (evt)
        {
            case BarClosed:
                AddAvailableRange(ranges, evt.Instrument, typeof(Bar), time);
                break;
            case TradeOccurred:
                AddAvailableRange(ranges, evt.Instrument, typeof(Trade), time);
                break;
            case QuoteReceived:
                AddAvailableRange(ranges, evt.Instrument, typeof(Quote), time);
                break;
            case BookSnapshotReceived or BookDepthSnapshotReceived or BookDepth10Received:
                AddAvailableRange(ranges, evt.Instrument, typeof(Book), time);
                break;
            case BookLevelDeltaReceived or BookLevelDeltasReceived:
                AddAvailableRange(ranges, evt.Instrument, typeof(BookLevelDelta), time);
                break;
            case BookOrderAdded or BookOrderModified or BookOrderDeleted or BookOrderExecuted:
                AddAvailableRange(ranges, evt.Instrument, typeof(BookOrder), time);
                break;
            case InstrumentStatusChanged or InstrumentClosed:
                AddAvailableRange(ranges, evt.Instrument, typeof(MarketStatus), time);
                break;
        }
    }

    private static void AddAvailableRange(
        Dictionary<(Instrument Instrument, Type DataType), RangeBuilder> ranges,
        Instrument instrument,
        Type dataType,
        Instant time)
    {
        var key = (instrument, dataType);
        ranges.TryGetValue(key, out var range);
        range.Add(time);
        ranges[key] = range;
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
            _ => evt.Time
        };

    private struct RangeBuilder
    {
        public Instant Start { get; private set; }
        public Instant End { get; private set; }
        private bool _hasValue;

        public void Add(Instant time)
        {
            if (!_hasValue)
            {
                Start = time;
                End = time;
                _hasValue = true;
                return;
            }

            if (time < Start)
                Start = time;
            if (time > End)
                End = time;
        }
    }
}
