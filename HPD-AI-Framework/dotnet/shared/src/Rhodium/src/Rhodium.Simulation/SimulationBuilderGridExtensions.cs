using Rhodium.Platform;

namespace Rhodium.Simulation;

public static class SimulationBuilderGridExtensions
{
    /// <summary>Run the strategy across a generated parameter grid.</summary>
    public static SimulationBuilder<TStrategy> WithGrid<TStrategy>(
        this SimulationBuilder<TStrategy> builder,
        ParameterGrid grid)
        where TStrategy : Strategy, IStrategyParameterFactory<TStrategy>, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithGeneratedGrid(grid, TStrategy.CreateVariant);
    }
}
