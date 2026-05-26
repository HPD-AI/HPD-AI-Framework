using Rhodium.Primitives;

namespace Rhodium.Simulation.Identity;

/// <summary>
/// Deterministic simulator-owned identity generator.
/// </summary>
public sealed class SimulationIdentityGenerator
{
    private long _clientOrderSequence;
    private readonly Dictionary<Venue, long> _venueOrderSequences = [];
    private readonly Dictionary<Instrument, long> _executionSequences = [];
    private readonly Dictionary<(Instrument Instrument, StrategyId StrategyId), long> _positionSequences = [];

    public OrderId NextClientOrderId()
        => new(++_clientOrderSequence);

    public VenueOrderId NextVenueOrderId(Instrument instrument)
    {
        var next = _venueOrderSequences.GetValueOrDefault(instrument.Venue) + 1;
        _venueOrderSequences[instrument.Venue] = next;
        return new VenueOrderId(next);
    }

    public ExecutionId NextExecutionId(Instrument instrument)
    {
        var next = _executionSequences.GetValueOrDefault(instrument) + 1;
        _executionSequences[instrument] = next;
        return new ExecutionId(next);
    }

    public PositionId NextPositionId(Instrument instrument, StrategyId strategyId)
    {
        var key = (instrument, strategyId);
        var next = _positionSequences.GetValueOrDefault(key) + 1;
        _positionSequences[key] = next;
        return new PositionId(next);
    }

    public void Reset()
    {
        _clientOrderSequence = 0;
        _venueOrderSequences.Clear();
        _executionSequences.Clear();
        _positionSequences.Clear();
    }
}
