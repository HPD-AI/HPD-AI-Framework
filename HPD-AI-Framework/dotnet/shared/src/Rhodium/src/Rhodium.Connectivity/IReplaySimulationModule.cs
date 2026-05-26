using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Connectivity;

/// <summary>
/// Small replay extension seam for deterministic simulator-side event generation.
/// </summary>
public interface IReplaySimulationModule
{
    void PreProcess(
        in FinanceEvent evt,
        ReplayModuleContext context,
        ReplayModuleSinks sinks);

    void Process(
        Instant now,
        ReplayModuleContext context,
        ReplayModuleSinks sinks);

    void Reset();
}

/// <summary>
/// Explicit effect sinks for replay connector modules.
/// </summary>
public sealed class ReplayModuleSinks
{
    private readonly List<FinanceEvent> _pendingEvents;

    internal ReplayModuleSinks(List<FinanceEvent> pendingEvents)
        => _pendingEvents = pendingEvents;

    public void Emit(FinanceEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        _pendingEvents.Add(evt);
    }
}
