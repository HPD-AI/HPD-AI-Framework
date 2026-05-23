using Rhodium.Primitives;

namespace Rhodium.Simulation;

public sealed record SimulationRunOptions
{
    public SimulationConfig Config { get; init; } = SimulationConfig.Queue();
    public Money InitialCash { get; init; } = Money.USD(100_000m);
    public int MaxDegreeOfParallelism { get; init; } = 1;
    public ISimulationExecutionModel? ExecutionModel { get; init; }

    public SimulationFidelity Fidelity => Config.Fidelity;
}
