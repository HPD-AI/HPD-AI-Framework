using Rhodium.Events;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Control;

/// <summary>
/// Engine state at a single point in time.
/// </summary>
public readonly record struct EngineState(
    WorldState World,
    ITensorStore Tensors,
    Instant Time,
    Sequence Sequence
);

/// <summary>
/// Core engine loop for tick-by-tick execution.
/// State transition is pure in semantics but implemented with in-place updates
/// on pre-allocated buffers for performance.
/// </summary>
public static class EngineLoop
{
    /// <summary>
    /// Process a single tick (event).
    /// Mutates state in-place for performance while maintaining semantic purity.
    /// </summary>
    public static void Tick(ref EngineState state, FinanceEvent evt, IBatchMap map)
    {
        // Apply state transitions (mutates WorldState and ITensorStore)
        StateTransitions.Apply(state.World, state.Tensors, map, evt);

        // Run adjustment kernel to update adjusted fields from raw fields
        state.Tensors.ForEachPage(new AdjustmentKernel());

        // Note: Strategy execution (StrategyExecutor.Execute) should be called by the
        // host/runner after Tick() completes. This maintains layer separation:
        // Control layer provides tick processing, Platform layer adds strategy execution.

        // Update time and sequence
        state = state with
        {
            Time = evt.Time,
            Sequence = state.Sequence.Next()
        };
    }
}
