using System.Collections.Generic;
using System.Threading;
using Rhodium.Primitives;

namespace Rhodium.Data;

/// <summary>
/// Fetches market data from external sources.
/// Implementations: YahooDataProvider, PolygonDataProvider, BinanceDataProvider, etc.
/// </summary>
public interface IDataProvider
{
    /// <summary>
    /// Provider identifier (e.g., "Yahoo", "Polygon", "Binance").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Fetch historical bars.
    /// </summary>
    IAsyncEnumerable<Bar> GetBarsAsync(
        Instrument instrument,
        Duration period,
        DateRange range,
        CancellationToken ct = default);

    /// <summary>
    /// Fetch historical trades (tick data).
    /// </summary>
    IAsyncEnumerable<Trade> GetTradesAsync(
        Instrument instrument,
        DateRange range,
        CancellationToken ct = default);

    /// <summary>
    /// Fetch historical quotes.
    /// </summary>
    IAsyncEnumerable<Quote> GetQuotesAsync(
        Instrument instrument,
        DateRange range,
        CancellationToken ct = default);
}
