using Rhodium.Primitives;

namespace Rhodium.Simulation.Exchange;

/// <summary>
/// Venue-local cold path scheduler for contract lifecycle work such as option expiry.
/// </summary>
internal sealed class ContractLifecycleScheduler
{
    private readonly SortedDictionary<Instant, HashSet<Instrument>> _expiries = [];
    private readonly Dictionary<Instrument, Instant> _expiryByInstrument = [];
    private readonly HashSet<Instrument> _completed = [];

    public void Register(InstrumentContract contract)
    {
        Remove(contract.Instrument);

        if (contract.Lifecycle is not ContractLifecycle.Expiring expiry ||
            contract.Payoff is not PayoffTerms.Option)
        {
            return;
        }

        _expiryByInstrument[contract.Instrument] = expiry.Expiry;
        if (!_expiries.TryGetValue(expiry.Expiry, out var instruments))
        {
            instruments = [];
            _expiries[expiry.Expiry] = instruments;
        }

        instruments.Add(contract.Instrument);
        _completed.Remove(contract.Instrument);
    }

    public void CopyDue(Instant now, List<ScheduledContractLifecycle> destination)
    {
        destination.Clear();
        foreach (var (expiry, instruments) in _expiries)
        {
            if (expiry > now)
                break;

            foreach (var instrument in instruments)
            {
                if (!_completed.Contains(instrument))
                    destination.Add(new ScheduledContractLifecycle(instrument, expiry));
            }
        }
    }

    public void MarkCompleted(Instrument instrument) => _completed.Add(instrument);

    public void MarkPending(Instrument instrument) => _completed.Remove(instrument);

    private void Remove(Instrument instrument)
    {
        if (!_expiryByInstrument.Remove(instrument, out var existingExpiry))
            return;

        _completed.Remove(instrument);
        if (!_expiries.TryGetValue(existingExpiry, out var instruments))
            return;

        instruments.Remove(instrument);
        if (instruments.Count == 0)
            _expiries.Remove(existingExpiry);
    }
}

internal readonly record struct ScheduledContractLifecycle(
    Instrument Instrument,
    Instant Expiry);
