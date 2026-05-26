using System.Runtime.CompilerServices;
using Rhodium.Primitives;

namespace Rhodium.Data;

/// <summary>
/// Discovers and retrieves canonical instrument contracts.
/// </summary>
public interface IInstrumentCatalog
{
    Task<InstrumentContract?> GetAsync(
        Instrument instrument,
        CancellationToken ct = default);

    Task<InstrumentContract?> GetAsync(
        string instrumentId,
        CancellationToken ct = default);

    IAsyncEnumerable<InstrumentContract> SearchAsync(
        Venue? venue = null,
        AssetClass? assetClass = null,
        Instrument? underlying = null,
        string? symbolPattern = null,
        ContractQuery? query = null,
        CancellationToken ct = default);
}

public sealed record ContractQuery
{
    public bool? Tradable { get; init; }
    public bool? Observable { get; init; }
    public bool? Expiring { get; init; }
    public bool? Perpetual { get; init; }
    public bool? HasOptionPayoff { get; init; }
    public bool? HasBinaryPayoff { get; init; }
    public bool? HasCfdPayoff { get; init; }
    public bool? HasBettingPayoff { get; init; }
    public bool? HasLegs { get; init; }
    public bool? HasFunding { get; init; }
    public ObservableKind? ObservableKind { get; init; }
    public Currency? QuoteCurrency { get; init; }
    public Currency? SettlementCurrency { get; init; }
    public DateRange? ExpiryRange { get; init; }
}

/// <summary>
/// Simple in-memory instrument catalog for tests, examples, and static universes.
/// </summary>
public sealed class StaticInstrumentCatalog : IInstrumentCatalog
{
    private readonly Dictionary<Instrument, InstrumentContract> _contracts;
    private readonly Dictionary<string, InstrumentContract> _contractsById;

    public StaticInstrumentCatalog(IEnumerable<InstrumentContract> contracts)
    {
        _contracts = contracts.ToDictionary(c => c.Instrument);
        _contractsById = new Dictionary<string, InstrumentContract>(StringComparer.OrdinalIgnoreCase);
        foreach (var contract in _contracts.Values)
        {
            _contractsById.TryAdd(contract.Instrument.ToString(), contract);
            _contractsById.TryAdd(contract.Instrument.Asset.Symbol, contract);
        }
    }

    public Task<InstrumentContract?> GetAsync(Instrument instrument, CancellationToken ct = default)
    {
        _contracts.TryGetValue(instrument, out var contract);
        return Task.FromResult<InstrumentContract?>(contract);
    }

    public Task<InstrumentContract?> GetAsync(string instrumentId, CancellationToken ct = default)
    {
        _contractsById.TryGetValue(instrumentId, out var contract);
        return Task.FromResult<InstrumentContract?>(contract);
    }

    public async IAsyncEnumerable<InstrumentContract> SearchAsync(
        Venue? venue = null,
        AssetClass? assetClass = null,
        Instrument? underlying = null,
        string? symbolPattern = null,
        ContractQuery? query = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var contract in _contracts.Values)
        {
            if (ct.IsCancellationRequested) yield break;

            if (venue.HasValue && contract.Instrument.Venue != venue.Value) continue;
            if (assetClass.HasValue && contract.Instrument.Asset.Class != assetClass.Value) continue;
            if (underlying.HasValue && !References(contract, underlying.Value)) continue;
            if (symbolPattern != null && !contract.Instrument.Asset.Symbol.Contains(symbolPattern, StringComparison.OrdinalIgnoreCase)) continue;
            if (query is not null && !MatchesQuery(contract, query)) continue;

            yield return contract;
        }

        await Task.CompletedTask;
    }

    private static bool References(InstrumentContract contract, Instrument instrument) =>
        contract.Legs.Any(leg => leg.Instrument == instrument)
        || contract.Payoff is PayoffTerms.Option option && option.Terms.Underlying == instrument
        || contract.Payoff is PayoffTerms.Cfd cfd && cfd.Underlying == instrument;

    private static bool MatchesQuery(InstrumentContract contract, ContractQuery query)
    {
        if (query.Tradable.HasValue && contract.VenueRules.IsTradable != query.Tradable.Value) return false;
        if (query.Observable.HasValue && IsObservable(contract) != query.Observable.Value) return false;
        if (query.Expiring.HasValue && contract.Lifecycle is ContractLifecycle.Expiring != query.Expiring.Value) return false;
        if (query.Perpetual.HasValue && contract.Lifecycle is ContractLifecycle.Perpetual != query.Perpetual.Value) return false;
        if (query.HasOptionPayoff.HasValue && contract.Payoff is PayoffTerms.Option != query.HasOptionPayoff.Value) return false;
        if (query.HasBinaryPayoff.HasValue && contract.Payoff is PayoffTerms.Binary != query.HasBinaryPayoff.Value) return false;
        if (query.HasCfdPayoff.HasValue && contract.Payoff is PayoffTerms.Cfd != query.HasCfdPayoff.Value) return false;
        if (query.HasBettingPayoff.HasValue && contract.Payoff is PayoffTerms.Betting != query.HasBettingPayoff.Value) return false;
        if (query.HasLegs.HasValue && contract.Legs.Count > 0 != query.HasLegs.Value) return false;
        if (query.HasFunding.HasValue && contract.Financing is not FinancingTerms.NoFinancing != query.HasFunding.Value) return false;
        if (query.ObservableKind.HasValue
            && (contract.Data is not DataSemantics.Observable observable
                || observable.Kind != query.ObservableKind.Value))
            return false;
        if (query.QuoteCurrency.HasValue && contract.Exposure.QuoteCurrency() != query.QuoteCurrency.Value) return false;
        if (query.SettlementCurrency.HasValue && contract.Exposure.SettlementCurrency() != query.SettlementCurrency.Value) return false;
        if (query.ExpiryRange.HasValue && !ExpiryInRange(contract, query.ExpiryRange.Value)) return false;

        return true;
    }

    private static bool IsObservable(InstrumentContract contract) =>
        contract.Data is DataSemantics.Observable or DataSemantics.Custom
        || contract.Instrument.Asset.Class == AssetClass.Observable;

    private static bool ExpiryInRange(InstrumentContract contract, DateRange range) =>
        contract.Lifecycle is ContractLifecycle.Expiring expiring && range.Contains(expiring.Expiry);
}
