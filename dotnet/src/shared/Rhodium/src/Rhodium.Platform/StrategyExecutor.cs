using Rhodium.Control;

namespace Rhodium.Platform;

/// <summary>
/// Executes strategy tick logic with safety guards.
/// Called by EngineLoop during each tick.
/// </summary>
public static class StrategyExecutor
{
    private static readonly List<StrategyBase> _strategies = new();

    /// <summary>
    /// Registers a strategy for execution.
    /// Should be called during initialization before any ticks.
    /// </summary>
    public static void Register(StrategyBase strategy)
    {
        _strategies.Add(strategy);
    }

    /// <summary>
    /// Clears all registered strategies.
    /// Useful for testing or resetting the executor.
    /// </summary>
    public static void Clear()
    {
        _strategies.Clear();
    }

    /// <summary>
    /// Executes all registered strategies for the current tick.
    /// Each strategy's OnTick is called with safety guards.
    /// </summary>
    public static void Execute(EngineState state)
    {
        foreach (var strategy in _strategies)
        {
            strategy.RunTickGuarded();
        }
    }

    /// <summary>
    /// Gets the count of registered strategies.
    /// </summary>
    public static int StrategyCount => _strategies.Count;
}
