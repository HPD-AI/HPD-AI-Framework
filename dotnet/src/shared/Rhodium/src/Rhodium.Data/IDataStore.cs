using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rhodium.Primitives;

namespace Rhodium.Data;

/// <summary>
/// Read/write persistence for market data.
/// Implementations: FileDataStore, SqliteDataStore, ParquetDataStore, etc.
/// </summary>
public interface IDataStore
{
    /// <summary>
    /// Store market data.
    /// </summary>
    Task StoreAsync<T>(
        Instrument instrument,
        IAsyncEnumerable<T> data,
        CancellationToken ct = default);

    /// <summary>
    /// Load market data for a time range.
    /// </summary>
    IAsyncEnumerable<T> LoadAsync<T>(
        Instrument instrument,
        DateRange range,
        CancellationToken ct = default);

    /// <summary>
    /// Get available data range for an instrument and data type.
    /// Returns null if no data exists.
    /// </summary>
    Task<DateRange?> GetAvailableRangeAsync<T>(
        Instrument instrument,
        CancellationToken ct = default);

    /// <summary>
    /// Check if data exists for the given range.
    /// </summary>
    Task<bool> ExistsAsync<T>(
        Instrument instrument,
        DateRange range,
        CancellationToken ct = default);

    /// <summary>
    /// Delete data for a time range.
    /// </summary>
    Task DeleteAsync<T>(
        Instrument instrument,
        DateRange range,
        CancellationToken ct = default);

    /// <summary>
    /// List all instruments with stored data.
    /// </summary>
    IAsyncEnumerable<Instrument> ListInstrumentsAsync<T>(
        CancellationToken ct = default);
}
