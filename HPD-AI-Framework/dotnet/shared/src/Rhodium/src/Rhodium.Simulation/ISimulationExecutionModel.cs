using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Simulation;

public interface ISimulationExecutionModel : IDisposable
{
    void Initialize(in SimulationExecutionContext context);

    void OnMarketEvent(FinanceEvent evt, in MarketKernel market);

    void Submit(in OrderIntent intent, in MarketKernel market);

    int DrainExecutionEvents(Span<ExecutionEvent> destination);
}
