using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Rhodium.Primitives;

namespace Rhodium.Data;

/// <summary>
/// Discovers and retrieves instrument metadata.
/// Implementations: StaticSecurityLookup, ExchangeSecurityLookup, etc.
/// </summary>
public interface ISecurityLookup
{
    /// <summary>
    /// Get metadata for a specific instrument.
    /// Returns null if not found.
    /// </summary>
    Task<SecurityMetadata?> GetAsync(
        Instrument instrument,
        CancellationToken ct = default);

    /// <summary>
    /// Search for instruments matching criteria.
    /// </summary>
    IAsyncEnumerable<SecurityMetadata> SearchAsync(
        Venue? venue = null,
        AssetClass? assetClass = null,
        Instrument? underlying = null,
        string? symbolPattern = null,
        CancellationToken ct = default);
}

/// <summary>
/// Simple in-memory security lookup for testing and static universes.
/// </summary>
public sealed class StaticSecurityLookup : ISecurityLookup
{
    private readonly Dictionary<Instrument, SecurityMetadata> _securities;

    public StaticSecurityLookup(IEnumerable<SecurityMetadata> securities)
    {
        _securities = securities.ToDictionary(s => s.Instrument);
    }

    public Task<SecurityMetadata?> GetAsync(Instrument instrument, CancellationToken ct = default)
    {
        _securities.TryGetValue(instrument, out var meta);
        return Task.FromResult<SecurityMetadata?>(meta);
    }

    public async IAsyncEnumerable<SecurityMetadata> SearchAsync(
        Venue? venue = null,
        AssetClass? assetClass = null,
        Instrument? underlying = null,
        string? symbolPattern = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var meta in _securities.Values)
        {
            if (ct.IsCancellationRequested) yield break;

            if (venue.HasValue && meta.Instrument.Venue != venue.Value) continue;
            if (assetClass.HasValue && meta.Instrument.Asset.Class != assetClass.Value) continue;
            if (underlying.HasValue && meta.Underlying != underlying.Value) continue;
            if (symbolPattern != null && !meta.Instrument.Asset.Symbol.Contains(symbolPattern)) continue;

            yield return meta;
        }

        await Task.CompletedTask; // Suppress async warning
    }
}
