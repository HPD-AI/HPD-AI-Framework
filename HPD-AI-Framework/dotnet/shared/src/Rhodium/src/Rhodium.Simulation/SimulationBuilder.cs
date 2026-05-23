using System.Diagnostics.CodeAnalysis;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Primitives;

namespace Rhodium.Simulation;

public static class Rhodium
{
    public static SimulationBuilder<TStrategy> Simulate<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TStrategy>()
        where TStrategy : Strategy, new()
        => new();
}

public sealed class SimulationBuilder<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)]
    TStrategy>
    where TStrategy : Strategy, new()
{
    private SharedHistory? _history;
    private ParameterGrid? _grid;
    private SimulationRunOptions _options = new();

    public SimulationBuilder<TStrategy> WithHistory(SharedHistory history)
    {
        _history = history;
        return this;
    }

    public SimulationBuilder<TStrategy> WithGrid(ParameterGrid grid)
    {
        _grid = grid;
        return this;
    }

    public SimulationBuilder<TStrategy> WithFidelity(SimulationFidelity fidelity)
    {
        var config = fidelity == SimulationFidelity.Vector
            ? SimulationConfig.Vector()
            : SimulationConfig.Queue();
        _options = _options with { Config = config };
        return this;
    }

    public SimulationBuilder<TStrategy> WithConfig(SimulationConfig config)
    {
        _options = _options with { Config = config };
        return this;
    }

    public SimulationBuilder<TStrategy> WithExecutionModel(ISimulationExecutionModel executionModel)
    {
        _options = _options with { ExecutionModel = executionModel };
        return this;
    }

    public SimulationBuilder<TStrategy> WithInitialCash(Money cash)
    {
        _options = _options with { InitialCash = cash };
        return this;
    }

    public SimulationBuilder<TStrategy> WithMaxDegreeOfParallelism(int degree)
    {
        if (degree < 1)
            throw new ArgumentOutOfRangeException(nameof(degree));

        _options = _options with { MaxDegreeOfParallelism = degree };
        return this;
    }

    public SimulationResult Run()
    {
        var history = _history ?? throw new InvalidOperationException("Simulation history is required.");
        using var runtime = new SimulationRuntime(new RhodiumRuntime());
        if (_grid is null)
        {
            runtime.RegisterStrategy<TStrategy>();
        }
        else
        {
            var strategyGrid = new StrategyGrid<TStrategy>(_grid);
            strategyGrid.RegisterAll(runtime.Strategies, depth: 0);
            runtime.SetVariantDescriptors(strategyGrid.Variants.ToArray());
        }

        return runtime.Run(history, _options);
    }
}
