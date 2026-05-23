using Rhodium.Control;
using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Control.Tests;

public class StateTransitionsTests
{
    [Fact]
    public void Apply_UnhandledEvent_DoesNotThrow()
    {
        using var runtime = new RhodiumRuntime();
        var evt = new OrderAccepted(OrderId.New(), new StrategyId(1), 0);

        StateTransitions.Apply(runtime.WorldState, runtime.Tensors, runtime.BatchMap, evt);
    }
}
