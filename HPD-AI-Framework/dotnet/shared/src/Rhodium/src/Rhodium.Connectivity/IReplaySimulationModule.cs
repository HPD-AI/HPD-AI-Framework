using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Connectivity;

/// <summary>
/// Small replay extension seam for deterministic simulator-side event generation.
/// </summary>
public interface IReplaySimulationModule
{
    void PreProcess(FinanceEvent evt, ReplayModuleContext context);

    IEnumerable<FinanceEvent> Process(Instant now, ReplayModuleContext context);

    void Reset();
}

